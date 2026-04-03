using SiteChecker.Domain.Entities;

namespace SiteChecker.Domain.Ports;

public interface ISiteCheckQueue
{
    ValueTask QueueCheckAsync(SiteCheck siteCheck, CancellationToken cancellationToken = default);

    ValueTask<int> DequeueAsync(CancellationToken cancellationToken);
}
