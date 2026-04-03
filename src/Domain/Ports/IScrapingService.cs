using SiteChecker.Domain.Entities;

namespace SiteChecker.Domain.Ports;

public interface IScrapingService
{
    Task<IScrapeResult> ScrapeAsync(
        Site site,
        SiteCheck siteCheck,
        CancellationToken cancellationToken = default);
}
