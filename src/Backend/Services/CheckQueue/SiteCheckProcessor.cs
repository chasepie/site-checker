namespace SiteChecker.Backend.Services.CheckQueue;

/// <summary>
/// Background service that runs Queued Site Checks through the <see cref="SiteCheckRunner"/>,
/// one at a time.
/// </summary>
public class SiteCheckQueueProcessor(
    SiteCheckRunner runner,
    ILogger<SiteCheckQueueProcessor> logger)
    : BackgroundService
{
    private readonly SiteCheckRunner _runner = runner;
    private readonly ILogger<SiteCheckQueueProcessor> _logger = logger;

    /// <summary>
    /// Re-queues interrupted checks, then runs Queued checks until the service is stopped.
    /// </summary>
    /// <param name="stoppingToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _runner.RecoverAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            bool ranCheck;
            try
            {
                ranCheck = await _runner.RunNextAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error occurred running the next site check.");
                ranCheck = false;
            }

            if (!ranCheck)
            {
                await _runner.WaitForWorkAsync(stoppingToken);
            }
        }
    }
}
