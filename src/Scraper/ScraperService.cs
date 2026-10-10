using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using SiteChecker.Scraper.Browsers;
using SiteChecker.Scraper.Executors;
using SiteChecker.Scraper.Scripts;
using SiteChecker.Scripting;

namespace SiteChecker.Scraper;

public interface IScraperService
{
    /// <inheritdoc cref="BrowserSelector.GetBrowserType"/>
    BrowserType GetBrowserType(bool useVpn);

    /// <summary>
    /// Runs a Scraper through the shared pipeline. Every failure comes back as a
    /// <see cref="ScrapeResult"/>; this throws only <see cref="OperationCanceledException"/>, when
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    Task<ScrapeResult> ScrapeAsync(ScrapeRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Unloads the Site's compiled script, if any. Call after the Site's script is replaced or the
    /// Site is deleted. Never throws, except <see cref="OperationCanceledException"/>.
    /// </summary>
    Task EvictScriptAsync(int siteId, CancellationToken cancellationToken);
}

/// <summary>
/// The shared scrape pipeline every kind of Scraper runs through: it opens the browser, navigates
/// to the Site's URL, runs the executor for the Scraper's kind under the Site's timeout, and takes
/// the screenshot and, for a Site Check's Unexpected Failure, the page's HTML. Exceptions are
/// converted to Unexpected Failures here, and only here. It writes nothing: the result carries
/// everything, so the pipeline can run in another process (the Site Check Runner writes the
/// failure dumps).
/// </summary>
/// <remarks>
/// Playwright calls take no cancellation token, and a script only stops if it checks one, so the
/// timeout is enforced from outside: the executor runs on the thread pool, and at the timeout the
/// pipeline stops waiting, takes the screenshot, and closes the browser, which makes any pending
/// Playwright call throw. A script stuck in a synchronous loop keeps its thread; the
/// <see cref="IAbandonedRunMonitor"/> decides whether that ends the process.
/// </remarks>
public sealed class ScraperService(
    BrowserSelector browserSelector,
    IBrowserProvider browsers,
    IEnumerable<IScrapeExecutor> executors,
    ScriptCache scriptCache,
    ScrapeTimeouts timeouts,
    IAbandonedRunMonitor abandonedRuns,
    TimeProvider timeProvider,
    ILogger<ScraperService> logger) : IScraperService
{
    private readonly BrowserSelector _browserSelector = browserSelector;
    private readonly IBrowserProvider _browsers = browsers;
    private readonly IReadOnlyList<IScrapeExecutor> _executors = executors.ToList();
    private readonly ScriptCache _scriptCache = scriptCache;
    private readonly ScrapeTimeouts _timeouts = timeouts;
    private readonly IAbandonedRunMonitor _abandonedRuns = abandonedRuns;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<ScraperService> _logger = logger;

    public BrowserType GetBrowserType(bool useVpn) => _browserSelector.GetBrowserType(useVpn);

    public Task EvictScriptAsync(int siteId, CancellationToken cancellationToken)
    {
        _scriptCache.Evict(siteId);
        return Task.CompletedTask;
    }

    public async Task<ScrapeResult> ScrapeAsync(ScrapeRequest request, CancellationToken cancellationToken)
    {
        var started = _timeProvider.GetTimestamp();
        var result = await ScrapeCoreAsync(request, cancellationToken);
        result = result with { Duration = _timeProvider.GetElapsedTime(started) };

        _logger.LogScrapeOutcome(request, result);
        return result;
    }

    private async Task<ScrapeResult> ScrapeCoreAsync(ScrapeRequest request, CancellationToken cancellationToken)
    {
        var executor = _executors.FirstOrDefault(e => e.SpecType == request.Scraper.GetType());
        if (executor is null)
        {
            return ScrapeResult.Unexpected($"No executor runs {request.Scraper.GetType().Name} Scrapers.");
        }

        IBrowserSession session;
        try
        {
            session = await _browsers.OpenAsync(request.BrowserType, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return ScrapeResult.Unexpected($"Couldn't open the browser: {ex.Message}", ex);
        }

        await using (session)
        {
            var log = new ScraperLog();
            var timeout = _timeouts.Resolve(request.Timeout);
            using var timeoutCancellation = new CancellationTokenSource(timeout, _timeProvider);
            using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellation.Token);

            // Task.Run, so even a script that blocks before its first await can't block the runner.
            var run = Task.Run(
                () => NavigateAndExecuteAsync(executor, session.Page, request, log, runCancellation.Token),
                CancellationToken.None);

            ScrapeResult result;
            var timedOut = false;
            using (var stopWaiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var deadline = Task.Delay(timeout, _timeProvider, stopWaiting.Token);
                var first = await Task.WhenAny(run, deadline);
                await stopWaiting.CancelAsync();
                if (cancellationToken.IsCancellationRequested)
                {
                    // The caller gave up (in the Scrape Worker, the app's request was aborted), so
                    // the run is abandoned just as at a timeout: its token is already cancelled
                    // through runCancellation, and a run that ignores it must still be tracked.
                    if (!run.IsCompleted)
                    {
                        await session.CloseAsync();
                        _abandonedRuns.Track(run, request);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                }

                // A run that threw once the timeout fired (such as a script stopping on its token)
                // ended because of the timeout, even if it beat the deadline here.
                if (first == run && (run.IsCompletedSuccessfully || !timeoutCancellation.IsCancellationRequested))
                {
                    result = await CompletedResultAsync(run);
                }
                else
                {
                    timedOut = true;
                    await runCancellation.CancelAsync();
                    result = ScrapeResult.Unexpected($"Timed out after {timeout.TotalSeconds:0.#} s.");
                }
            }

            // The screenshot and page HTML get their own budget outside the timeout, so a timed-out
            // scrape, the failure most worth seeing, still gets them.
            using var artifactBudget = new CancellationTokenSource(ScrapeTimeouts.ArtifactBudget, _timeProvider);
            if (result.Outcome != ScrapeOutcome.Succeeded || request.AlwaysTakeScreenshot)
            {
                result = result with { Screenshot = await TryTakeScreenshotAsync(session.Page, artifactBudget.Token) };
            }

            if (result.Outcome == ScrapeOutcome.UnexpectedFailure && !request.IsTestRun)
            {
                result = result with { PageHtml = await TryGetPageHtmlAsync(session.Page, artifactBudget.Token) };
            }

            result = result with { Logs = log.Entries };

            if (timedOut)
            {
                // Makes any Playwright call the abandoned run is waiting on throw.
                await session.CloseAsync();
                _abandonedRuns.Track(run, request);
            }

            return result;
        }
    }

    private static async Task<ScrapeResult> NavigateAndExecuteAsync(
        IScrapeExecutor executor,
        IPage page,
        ScrapeRequest request,
        ScraperLog log,
        CancellationToken cancellationToken)
    {
        // Navigation errors don't fail the scrape: the Scraper decides what they mean, such as a
        // blocked VPN Location.
        NavigationResult navigation;
        try
        {
            // AbsoluteUri keeps the URL's escaping; ToString() would unescape it.
            navigation = NavigationResult.FromResponse(await page.GotoAsync(request.Site.Url.AbsoluteUri));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            navigation = NavigationResult.FromError(ex);
        }

        return await executor.ExecuteAsync(new ExecutorContext(page, navigation, request, log, cancellationToken));
    }

    private static async Task<ScrapeResult> CompletedResultAsync(Task<ScrapeResult> run)
    {
        try
        {
            return await run;
        }
        catch (Exception ex)
        {
            return ScrapeResult.Unexpected(ex);
        }
    }

    private async Task<byte[]?> TryTakeScreenshotAsync(IPage page, CancellationToken budget)
    {
        try
        {
            return await page.TakeFullPageScreenshotAsync().WaitAsync(budget);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Couldn't take the screenshot.");
            return null;
        }
    }

    private async Task<string?> TryGetPageHtmlAsync(IPage page, CancellationToken budget)
    {
        try
        {
            return await page.ContentAsync().WaitAsync(budget);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Couldn't read the page's HTML.");
            return null;
        }
    }
}

public static class ScraperServiceExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddScraperServices()
        {
            services.TryAddSingleton(TimeProvider.System);
            // A host that can restart (the Scrape Worker) registers one that stops the process.
            services.TryAddSingleton<IAbandonedRunMonitor, LoggingAbandonedRunMonitor>();
            return services
                .AddSingleton<BrowserSelector>()
                .AddSingleton<IBrowserProvider, BrowserProvider>()
                .AddSingleton<IScriptCompiler, ScriptCompiler>()
                .AddSingleton<ScriptCache>()
                .AddSingleton<IScrapeExecutor, ScriptExecutor>()
                .AddSingleton<ScrapeTimeouts>()
                .AddSingleton<FailureArtifacts>()
                .AddSingleton<IScraperService, ScraperService>();
        }
    }
}
