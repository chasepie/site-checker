using SiteChecker.Domain.DTOs;
using SiteChecker.Domain.Entities;

namespace SiteChecker.Domain.Ports;

public interface ISiteCheckRepository
{
    Task<SiteCheck?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<SiteCheck?> GetByIdForSiteAsync(int siteId, int id, CancellationToken cancellationToken = default);

    Task<PagedResponse<SiteCheck>> GetPagedAsync(
        int siteId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<SiteCheck?> GetLatestForSiteAsync(int siteId, CancellationToken cancellationToken = default);

    Task<SiteCheck?> GetPreviousSuccessfulAsync(
        int siteId,
        int beforeId,
        CancellationToken cancellationToken = default);

    Task AddAsync(SiteCheck siteCheck, CancellationToken cancellationToken = default);

    Task UpdateAsync(SiteCheck siteCheck, CancellationToken cancellationToken = default);

    Task RemoveAsync(SiteCheck siteCheck, CancellationToken cancellationToken = default);

    Task RemoveAllForSiteAsync(int siteId, CancellationToken cancellationToken = default);

    Task AddScreenshotAsync(SiteCheckScreenshot screenshot, CancellationToken cancellationToken = default);

    Task<SiteCheckScreenshot?> GetScreenshotAsync(int siteCheckId, CancellationToken cancellationToken = default);
}
