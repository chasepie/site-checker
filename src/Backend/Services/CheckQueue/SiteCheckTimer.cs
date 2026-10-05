namespace SiteChecker.Backend.Services.CheckQueue;

/// <summary>
/// Background service that asks the <see cref="SiteCheckRunner"/> to queue due Site Checks every minute.
/// </summary>
public class SiteCheckTimer(
    SiteCheckRunner runner,
    ILogger<SiteCheckTimer> logger)
    : BackgroundService
{
    private readonly SiteCheckRunner _runner = runner;
    private readonly ILogger<SiteCheckTimer> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await QueueDueChecksAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await QueueDueChecksAsync(stoppingToken);
        }
    }

    private async Task QueueDueChecksAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _runner.QueueDueChecksAsync(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Error occurred queueing due site checks.");
        }
    }
}
