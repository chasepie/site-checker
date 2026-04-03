using SiteChecker.Domain.Entities;

namespace SiteChecker.Domain.Ports;

public interface INotificationService
{
    Task NotifyAsync(
        SiteCheck siteCheck,
        Site site,
        SiteCheckScreenshot? screenshot,
        CancellationToken cancellationToken = default);
}
