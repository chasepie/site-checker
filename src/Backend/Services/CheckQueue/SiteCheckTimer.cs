using SiteChecker.Application.UseCases;

namespace SiteChecker.Backend.Services.CheckQueue;

public class SiteCheckTimer(
    ILogger<SiteCheckTimer> logger,
    IServiceScopeFactory scopeFactory)
    : BackgroundService
{
    private readonly ILogger<SiteCheckTimer> _logger = logger;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await DoCheckAsync(stoppingToken);
        var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await DoCheckAsync(stoppingToken);
        }
    }

    private async Task DoCheckAsync(CancellationToken stoppingToken)
    {
        await _semaphore.WaitAsync(stoppingToken);
        try
        {
            await RunScheduleAsync(stoppingToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task RunScheduleAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Checking sites...");

        await using var scope = _scopeFactory.CreateAsyncScope();
        var useCase = scope.ServiceProvider.GetRequiredService<ScheduleSiteChecksUseCase>();
        await useCase.ExecuteAsync(DateTime.Now, stoppingToken);

        _logger.LogInformation("Sites checked.");
    }
}
