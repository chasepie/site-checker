using System.Threading.Channels;
using SiteChecker.Database;
using SiteChecker.Database.Services;
using SiteChecker.Domain.Entities;
using SiteChecker.Domain.Enums;

namespace SiteChecker.Backend.Services.CheckQueue;

public interface ISiteCheckQueueService
{
    ValueTask QueueCheckAsync(
        SiteCheck siteCheck,
        CancellationToken cancellationToken = default);

    ValueTask<int> DequeueAsync(CancellationToken cancellationToken);
}

public class SiteCheckQueueService : ISiteCheckQueueService, IEntityChangeService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Channel<int> _queue;

    public SiteCheckQueueService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;

        var options = new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        _queue = Channel.CreateBounded<int>(options);
    }

    public async ValueTask QueueCheckAsync(
        SiteCheck siteCheck,
        CancellationToken cancellationToken = default)
    {
        await _queue.Writer.WriteAsync(siteCheck.Id, cancellationToken);
    }

    public async ValueTask<int> DequeueAsync(CancellationToken cancellationToken)
    {
        return await _queue.Reader.ReadAsync(cancellationToken);
    }

    public async Task OnEntityCreated(
        CreatedEntityChange change,
        CancellationToken cancellationToken)
    {
        if (change.EntityTypeName == nameof(SiteCheck)
            && change.Entity is SiteCheck siteCheck
            && siteCheck.Status == CheckStatus.Created)
        {
            using var dbContext = _scopeFactory.CreateScope()
                .ServiceProvider.GetRequiredService<SiteCheckerDbContext>();
            siteCheck.Status = CheckStatus.Queued;
            await dbContext.SaveChangesAsync(cancellationToken);

            await QueueCheckAsync(siteCheck, cancellationToken);
        }
    }

    public Task OnEntityDeleted(
        DeletedEntityChange change,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task OnEntityUpdated(
        UpdatedEntityChange change,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

public static class SiteCheckQueueServiceExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSiteCheckQueueService()
        {
            return services
                .AddSingleton<SiteCheckQueueService>()
                .AddSingleton<ISiteCheckQueueService>(sp =>
                    sp.GetRequiredService<SiteCheckQueueService>())
                .AddSingleton<IEntityChangeService>(sp =>
                    sp.GetRequiredService<SiteCheckQueueService>());
        }
    }
}
