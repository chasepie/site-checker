using Microsoft.AspNetCore.SignalR;
using SiteChecker.Backend.Models;
using SiteChecker.Backend.Services.CheckQueue;
using SiteChecker.Backend.Services.SignalR;
using SiteChecker.Scraper;
using SiteChecker.Scraper.Scripts;

namespace SiteChecker.Backend.Services.TestRuns;

/// <summary>
/// Runs Test Runs in the background and sends each result to the SignalR connection that started
/// it, not through the save interceptor's broadcast. Test Runs aren't persisted, so one lost to a
/// restart costs nothing (the same reasoning ADR 0001 uses for the wake-up channel).
/// </summary>
public sealed class TestRunService(
    SiteCheckRunner runner,
    IHubContext<DataHub> hubContext,
    IHostApplicationLifetime lifetime,
    ILogger<TestRunService> logger)
{
    private readonly SiteCheckRunner _runner = runner;
    private readonly IHubContext<DataHub> _hubContext = hubContext;
    private readonly IHostApplicationLifetime _lifetime = lifetime;
    private readonly ILogger<TestRunService> _logger = logger;

    /// <summary>
    /// Starts a validated Test Run and returns at once.
    /// </summary>
    public void Start(TestRunRequest request)
    {
        _ = Task.Run(() => RunAsync(request, _lifetime.ApplicationStopping), CancellationToken.None);
    }

    /// <summary>
    /// Runs a Test Run through the runner, which waits for any running Site Check, and sends the
    /// result to the requesting connection. Never throws.
    /// </summary>
    public async Task RunAsync(TestRunRequest request, CancellationToken cancellationToken)
    {
        TestRunResult result;
        try
        {
            var scrape = await _runner.RunTestAsync(ToScrapeRequest(request), cancellationToken);
            result = TestRunResult.From(request.TestRunId, scrape);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test Run {TestRunId} failed.", request.TestRunId);
            result = new TestRunResult
            {
                TestRunId = request.TestRunId,
                Outcome = ScrapeOutcome.UnexpectedFailure,
                Message = ex.Message,
            };
        }

        try
        {
            await _hubContext.Clients.Client(request.ConnectionId)
                .SendAsync(SignalRConstants.OnTestRunCompletedKey, result, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Couldn't deliver Test Run {TestRunId}'s result.", request.TestRunId);
        }
    }

    private static ScrapeRequest ToScrapeRequest(TestRunRequest request)
    {
        var script = request.Scraper.Script
            ?? throw new InvalidOperationException("A Test Run needs a script.");
        return new ScrapeRequest
        {
            SiteCheckId = null,
            Site = new ScrapeSite(null, request.Name ?? "Test Run", request.Url, request.UseVpn),
            Scraper = new ScriptSpec(script.FileName, script.Source, ScriptSource.Hash(script.Source)),
            Timeout = request.TimeoutSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
            AlwaysTakeScreenshot = request.AlwaysTakeScreenshot,
        };
    }
}
