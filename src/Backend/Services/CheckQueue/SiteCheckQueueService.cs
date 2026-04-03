using System.Threading.Channels;
using SiteChecker.Domain.Entities;
using SiteChecker.Domain.Ports;

namespace SiteChecker.Backend.Services.CheckQueue;

public class SiteCheckQueueService : ISiteCheckQueue
{
    private readonly Channel<int> _queue;

    public SiteCheckQueueService()
    {
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
}

public static class SiteCheckQueueServiceExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSiteCheckQueueService()
        {
            return services
                .AddSingleton<SiteCheckQueueService>()
                .AddSingleton<ISiteCheckQueue>(sp =>
                    sp.GetRequiredService<SiteCheckQueueService>());
        }
    }
}

