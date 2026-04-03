using SiteChecker.Application.UseCases;
using SiteChecker.Domain.Entities;
using SiteChecker.Domain.Ports;

namespace SiteChecker.Backend.Services.CheckQueue;

/// <summary>
/// Background service that dequeues pending checks and delegates execution to
/// <see cref="PerformSiteCheckUseCase"/>. All orchestration logic lives in the use case;
/// this class only handles the BackgroundService loop and scope management.
/// </summary>
public class SiteCheckQueueProcessor(
    ISiteCheckQueueService siteCheckQueue,
    ILogger<SiteCheckQueueProcessor> logger,
    IServiceScopeFactory scopeFactory)
    : BackgroundService
{
    private readonly ISiteCheckQueueService _siteCheckQueue = siteCheckQueue;
    private readonly ILogger<SiteCheckQueueProcessor> _logger = logger;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;

    /// <summary>
    /// Processes queued checks until the service is stopped.
    /// </summary>
    /// <param name="stoppingToken">Token to monitor for cancellation requests.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var siteCheckId = await _siteCheckQueue.DequeueAsync(stoppingToken);

            // A new scope per check ensures each check gets fresh scoped services.
            await using var scope = _scopeFactory.CreateAsyncScope();
            var useCase = scope.ServiceProvider.GetRequiredService<PerformSiteCheckUseCase>();

            try
            {
                await useCase.ExecuteAsync(siteCheckId, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing site check {SiteCheckId}.", siteCheckId);

                // Attempt to mark the check as failed in the database.
                try
                {
                    await using var errorScope = _scopeFactory.CreateAsyncScope();
                    var repo = errorScope.ServiceProvider.GetRequiredService<ISiteCheckRepository>();
                    var siteCheck = await repo.GetByIdAsync(siteCheckId, stoppingToken);
                    if (siteCheck != null)
                    {
                        siteCheck.Update(ex);
                        await repo.UpdateAsync(siteCheck, stoppingToken);
                    }
                }
                catch (Exception innerEx)
                {
                    _logger.LogError(innerEx, "Failed to mark check {SiteCheckId} as failed.", siteCheckId);
                }
            }
        }
    }
}
