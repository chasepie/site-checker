using System.Net;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiteChecker.Scraper;
using SiteChecker.Scraper.Browsers;
using SiteChecker.Scraper.Executors;

namespace SiteChecker.Backend.Services.Scraping;

/// <summary>
/// How long <see cref="RemoteScraperService"/> waits for the Scrape Worker.
/// </summary>
/// <param name="ReadinessTimeout">How long to wait for the worker to report healthy before a
/// scrape, long enough for it to restart after stopping on an abandoned run.</param>
/// <param name="ReadinessPollInterval">How often to ask whether it's healthy.</param>
/// <param name="ResponseAllowance">How much longer than the Site's timeout a scrape may take to
/// come back: connecting to the browser (before the timeout starts), the screenshot and page HTML
/// budget, closing the browser context and connection, and the transfer. Giving up sooner would
/// release the scrape lock while the worker still uses the browser.</param>
public sealed record RemoteScraperOptions(
    TimeSpan ReadinessTimeout,
    TimeSpan ReadinessPollInterval,
    TimeSpan ResponseAllowance)
{
    public static RemoteScraperOptions Default { get; } = new(
        ReadinessTimeout: TimeSpan.FromSeconds(120),
        ReadinessPollInterval: TimeSpan.FromSeconds(1),
        ResponseAllowance: BrowserProvider.ConnectTimeout
            + ScrapeTimeouts.ArtifactBudget
            + (BrowserProvider.CloseTimeout * 2)
            + TimeSpan.FromSeconds(15));
}

/// <summary>
/// Runs scrapes in the Scrape Worker (<c>SCRAPE_WORKER_URL</c>), so scripts never run in the app.
/// Like the in-process pipeline, every failure, including the worker being down or not answering,
/// comes back as an Unexpected Failure. The app still chooses the browser, since the runner needs
/// the choice before the scrape. What the Scraper logged in the worker is logged again here, so it
/// reaches the app's logs.
/// </summary>
public sealed class RemoteScraperService(
    IHttpClientFactory httpClientFactory,
    BrowserSelector browserSelector,
    ScrapeTimeouts timeouts,
    RemoteScraperOptions options,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory) : IScraperService
{
    public const string ScrapeWorkerUrlKey = "SCRAPE_WORKER_URL";
    public const string HttpClientName = "ScrapeWorker";

    /// <summary>
    /// How much of a refusal's body goes into the Site Check's message.
    /// </summary>
    private const int MaxRefusalBodyLength = 500;

    /// <summary>
    /// How long evicting a script may take; the caller is a Site save or delete.
    /// </summary>
    private static readonly TimeSpan EvictTimeout = TimeSpan.FromSeconds(10);

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly BrowserSelector _browserSelector = browserSelector;
    private readonly ScrapeTimeouts _timeouts = timeouts;
    private readonly RemoteScraperOptions _options = options;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger _logger = loggerFactory.CreateLogger<RemoteScraperService>();
    private readonly ILogger _scriptLogger = loggerFactory.CreateLogger(ScriptExecutor.LoggerCategory);

    public BrowserType GetBrowserType(bool useVpn) => _browserSelector.GetBrowserType(useVpn);

    public async Task<ScrapeResult> ScrapeAsync(ScrapeRequest request, CancellationToken cancellationToken)
    {
        var started = _timeProvider.GetTimestamp();
        var result = await ScrapeCoreAsync(request, cancellationToken);
        if (result.Duration == TimeSpan.Zero)
        {
            result = result with { Duration = _timeProvider.GetElapsedTime(started) };
        }

        RelogScraperLogs(request, result);
        _logger.LogScrapeOutcome(request, result);
        return result;
    }

    public async Task EvictScriptAsync(int siteId, CancellationToken cancellationToken)
    {
        try
        {
            // The client has no timeout of its own, so a worker that doesn't answer would hang the save.
            using var timeout = new CancellationTokenSource(EvictTimeout, _timeProvider);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.DeleteAsync($"{ScrapeWorkerPaths.Scripts}/{siteId}", linked.Token);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // The worker compiles again when the source hash changes, so a missed eviction only
            // keeps an old script loaded until it restarts.
            _logger.LogWarning(ex, "Couldn't evict Site {SiteId}'s script from the Scrape Worker.", siteId);
        }
    }

    private async Task<ScrapeResult> ScrapeCoreAsync(ScrapeRequest request, CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient(HttpClientName);
        if (!await WaitUntilHealthyAsync(client, cancellationToken))
        {
            return ScrapeResult.Unexpected(
                $"The Scrape Worker isn't available: it didn't report healthy within {_options.ReadinessTimeout.TotalSeconds:0} s.");
        }

        var budget = _timeouts.Resolve(request.Timeout) + _options.ResponseAllowance;
        using var timeout = new CancellationTokenSource(budget, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            using var response = await client.PostAsJsonAsync(ScrapeWorkerPaths.Scrape, request, ScrapeJson.Options, linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                // Capped: it becomes the Site Check's message and the failure notification.
                var body = await response.Content.ReadAsStringAsync(linked.Token);
                if (body.Length > MaxRefusalBodyLength)
                {
                    body = body[..MaxRefusalBodyLength] + "…";
                }
                return ScrapeResult.Unexpected(
                    $"The Scrape Worker refused the scrape ({(int)response.StatusCode} {response.StatusCode}): {body}");
            }

            return await response.Content.ReadFromJsonAsync<ScrapeResult>(ScrapeJson.Options, linked.Token)
                ?? ScrapeResult.Unexpected("The Scrape Worker returned no result.");
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return ScrapeResult.Unexpected(
                $"The Scrape Worker stopped responding: no result within {budget.TotalSeconds:0} s.");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return ScrapeResult.Unexpected($"The Scrape Worker stopped responding: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Waits for the worker's health check to pass. It fails while a run abandoned at its timeout
    /// is still going, until the worker restarts.
    /// </summary>
    private async Task<bool> WaitUntilHealthyAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var started = _timeProvider.GetTimestamp();
        var loggedWait = false;
        while (true)
        {
            try
            {
                using var probeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                probeTimeout.CancelAfter(_options.ReadinessPollInterval * 5);
                using var response = await client.GetAsync(ScrapeWorkerPaths.Health, probeTimeout.Token);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return true;
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug(ex, "The Scrape Worker's health check failed.");
            }

            if (_timeProvider.GetElapsedTime(started) >= _options.ReadinessTimeout)
            {
                return false;
            }
            if (!loggedWait)
            {
                _logger.LogInformation("Waiting for the Scrape Worker to report healthy.");
                loggedWait = true;
            }
            await Task.Delay(_options.ReadinessPollInterval, _timeProvider, cancellationToken);
        }
    }

    private void RelogScraperLogs(ScrapeRequest request, ScrapeResult result)
    {
        if (result.Logs.Count == 0)
        {
            return;
        }

        using var scope = _scriptLogger.BeginScrapeScope(request);
        foreach (var entry in result.Logs)
        {
            if (entry.Exception is null)
            {
                _scriptLogger.Log(entry.Level, "{ScraperMessage}", entry.Message);
            }
            else
            {
                _scriptLogger.Log(entry.Level, "{ScraperMessage}{NewLine}{ScraperException}", entry.Message, Environment.NewLine, entry.Exception);
            }
        }
    }
}

public static class RemoteScraperServiceExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Runs scrapes in the Scrape Worker when <c>SCRAPE_WORKER_URL</c> is set, replacing the
        /// in-process pipeline that <c>AddScraperServices</c> registers.
        /// </summary>
        /// <returns>Whether the worker is configured.</returns>
        public bool TryAddRemoteScraperService(IConfiguration configuration)
        {
            var workerUrl = configuration[RemoteScraperService.ScrapeWorkerUrlKey];
            if (string.IsNullOrWhiteSpace(workerUrl))
            {
                return false;
            }

            // Relative paths resolve under the base address only when it ends with a slash.
            var baseAddress = new Uri(workerUrl.EndsWith('/') ? workerUrl : workerUrl + "/");
            services.AddHttpClient(RemoteScraperService.HttpClientName, client =>
            {
                client.BaseAddress = baseAddress;
                // Each call sets its own deadline from the Site's timeout.
                client.Timeout = Timeout.InfiniteTimeSpan;
            });
            services.AddSingleton(RemoteScraperOptions.Default);
            services.Replace(ServiceDescriptor.Singleton<IScraperService, RemoteScraperService>());
            return true;
        }
    }
}
