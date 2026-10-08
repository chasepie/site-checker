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
    /// <inheritdoc cref="IBrowserProvider.GetBrowserType"/>
    BrowserType GetBrowserType(bool useVpn);

    /// <summary>
    /// Runs a Scraper through the shared pipeline. Every failure comes back as a
    /// <see cref="ScrapeResult"/>; this throws only <see cref="OperationCanceledException"/>, when
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    Task<ScrapeResult> ScrapeAsync(ScrapeRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The shared scrape pipeline every kind of Scraper runs through: it opens the browser, navigates
/// to the Site's URL, runs the executor for the Scraper's kind under the Site's timeout, and takes
/// the screenshot and failure dumps. Exceptions are converted to Unexpected Failures here, and
/// only here.
/// </summary>
/// <remarks>
/// Playwright calls take no cancellation token, and a script only stops if it checks one, so the
/// timeout is enforced from outside: the executor runs on the thread pool, and at the timeout the
/// pipeline stops waiting, takes the screenshot, and closes the browser, which makes any pending
/// Playwright call throw. A script stuck in a synchronous loop keeps its thread until the app
/// restarts (see <c>docs/adr/0004-scripts-run-in-process-behind-a-trust-boundary.md</c>).
/// </remarks>
public sealed class ScraperService(
    IBrowserProvider browsers,
    IEnumerable<IScrapeExecutor> executors,
    ScrapeTimeouts timeouts,
    FailureArtifacts artifacts,
    TimeProvider timeProvider,
    ILogger<ScraperService> logger) : IScraperService
{
    private readonly IBrowserProvider _browsers = browsers;
    private readonly IReadOnlyList<IScrapeExecutor> _executors = executors.ToList();
    private readonly ScrapeTimeouts _timeouts = timeouts;
    private readonly FailureArtifacts _artifacts = artifacts;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<ScraperService> _logger = logger;

    public BrowserType GetBrowserType(bool useVpn) => _browsers.GetBrowserType(useVpn);

    public async Task<ScrapeResult> ScrapeAsync(ScrapeRequest request, CancellationToken cancellationToken)
    {
        var started = _timeProvider.GetTimestamp();
        var result = await ScrapeCoreAsync(request, cancellationToken);
        result = result with { Duration = _timeProvider.GetElapsedTime(started) };

        if (result.Outcome == ScrapeOutcome.UnexpectedFailure)
        {
            _logger.LogError(result.Exception, "Scraping {SiteName} (Site Check {SiteCheckId}) failed: {Message}",
                request.Site.Name, request.SiteCheckId, result.Message);
        }
        else if (result.Outcome == ScrapeOutcome.KnownFailure)
        {
            _logger.LogWarning("Scraping {SiteName} (Site Check {SiteCheckId}) found a Known Failure: {Message}",
                request.Site.Name, request.SiteCheckId, result.Message);
        }
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
            var timeout = _timeouts.Resolve(request.Timeout);
            using var timeoutCancellation = new CancellationTokenSource(timeout, _timeProvider);
            using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellation.Token);

            // Task.Run, so even a script that blocks before its first await can't block the runner.
            var run = Task.Run(
                () => NavigateAndExecuteAsync(executor, session.Page, request, runCancellation.Token),
                CancellationToken.None);

            ScrapeResult result;
            var timedOut = false;
            using (var stopWaiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var deadline = Task.Delay(timeout, _timeProvider, stopWaiting.Token);
                var first = await Task.WhenAny(run, deadline);
                await stopWaiting.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();

                if (first == run)
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

            // The screenshot and dumps get their own budget outside the timeout, so a timed-out
            // scrape, the failure most worth seeing, still gets them.
            using var artifactBudget = new CancellationTokenSource(ScrapeTimeouts.ArtifactBudget, _timeProvider);
            if (result.Outcome != ScrapeOutcome.Succeeded || request.AlwaysTakeScreenshot)
            {
                result = result with { Screenshot = await TryTakeScreenshotAsync(session.Page, artifactBudget.Token) };
            }

            if (result.Outcome == ScrapeOutcome.UnexpectedFailure && !request.IsTestRun)
            {
                await _artifacts.WriteAsync(request, session.Page, result, artifactBudget.Token);
            }

            if (timedOut)
            {
                // Makes any Playwright call the abandoned run is waiting on throw.
                await session.CloseAsync();
                ObserveAbandoned(run, request);
            }

            return result;
        }
    }

    private static async Task<ScrapeResult> NavigateAndExecuteAsync(
        IScrapeExecutor executor,
        IPage page,
        ScrapeRequest request,
        CancellationToken cancellationToken)
    {
        // Navigation errors don't fail the scrape: the Scraper decides what they mean, such as a
        // blocked VPN Location.
        NavigationResult navigation;
        try
        {
            navigation = NavigationResult.FromResponse(await page.GotoAsync(request.Site.Url.ToString()));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            navigation = NavigationResult.FromError(ex);
        }

        return await executor.ExecuteAsync(new ExecutorContext(page, navigation, request, cancellationToken));
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

    private void ObserveAbandoned(Task run, ScrapeRequest request)
    {
        _ = run.ContinueWith(
            t => _logger.LogInformation(t.Exception?.GetBaseException(),
                "The timed-out run for {SiteName} (Site Check {SiteCheckId}) has ended.", request.Site.Name, request.SiteCheckId),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}

public static class ScraperServiceExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddScraperServices()
        {
            services.TryAddSingleton(TimeProvider.System);
            return services
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
