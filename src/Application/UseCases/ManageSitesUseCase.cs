using SiteChecker.Domain.DTOs;
using SiteChecker.Domain.Entities;
using SiteChecker.Domain.Ports;

namespace SiteChecker.Application.UseCases;

/// <summary>
/// CRUD operations for sites and site checks via domain repository ports.
/// Extracted from SiteController and SiteCheckController.
/// </summary>
public class ManageSitesUseCase(
    ISiteRepository siteRepository,
    ISiteCheckRepository siteCheckRepository)
{
    private readonly ISiteRepository _siteRepository = siteRepository;
    private readonly ISiteCheckRepository _siteCheckRepository = siteCheckRepository;

    // ── Sites ───────────────────────────────────────────────────────────────

    /// <summary>Returns all sites.</summary>
    public Task<IReadOnlyList<Site>> GetAllSitesAsync(CancellationToken cancellationToken = default)
        => _siteRepository.GetAllAsync(cancellationToken);

    /// <summary>Returns the site with the given <paramref name="id"/>, or null if not found.</summary>
    public Task<Site?> GetSiteAsync(int id, CancellationToken cancellationToken = default)
        => _siteRepository.GetByIdAsync(id, cancellationToken);

    /// <summary>
    /// Applies <paramref name="update"/> to the site with the matching ID.
    /// </summary>
    /// <returns>The updated site, or null if the site does not exist.</returns>
    public async Task<Site?> UpdateSiteAsync(SiteUpdate update, CancellationToken cancellationToken = default)
    {
        var site = await _siteRepository.GetByIdAsync(update.Id, cancellationToken);
        if (site == null)
        {
            return null;
        }

        site.Update(update);
        await _siteRepository.UpdateAsync(site, cancellationToken);
        return site;
    }

    // ── Site Checks ─────────────────────────────────────────────────────────

    /// <summary>Returns a paged list of checks for the given site.</summary>
    public Task<PagedResponse<SiteCheck>> GetSiteChecksPagedAsync(
        int siteId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
        => _siteCheckRepository.GetPagedAsync(siteId, pageNumber, pageSize, cancellationToken);

    /// <summary>
    /// Returns the site check with <paramref name="id"/> belonging to <paramref name="siteId"/>,
    /// or null if not found.
    /// </summary>
    public Task<SiteCheck?> GetSiteCheckAsync(int siteId, int id, CancellationToken cancellationToken = default)
        => _siteCheckRepository.GetByIdForSiteAsync(siteId, id, cancellationToken);

    /// <summary>Removes all checks (and their screenshots) for the given site.</summary>
    public Task DeleteSiteChecksAsync(int siteId, CancellationToken cancellationToken = default)
        => _siteCheckRepository.RemoveAllForSiteAsync(siteId, cancellationToken);

    /// <summary>
    /// Removes the check with <paramref name="id"/> belonging to <paramref name="siteId"/>.
    /// </summary>
    /// <returns>True if the check was found and removed; false if it did not exist.</returns>
    public async Task<bool> DeleteSiteCheckAsync(int siteId, int id, CancellationToken cancellationToken = default)
    {
        var siteCheck = await _siteCheckRepository.GetByIdForSiteAsync(siteId, id, cancellationToken);
        if (siteCheck == null)
        {
            return false;
        }

        await _siteCheckRepository.RemoveAsync(siteCheck, cancellationToken);
        return true;
    }

    /// <summary>
    /// Returns the screenshot for the given check, or null if none exists or the check was not
    /// found under the given site.
    /// </summary>
    public async Task<SiteCheckScreenshot?> GetScreenshotAsync(
        int siteId,
        int siteCheckId,
        CancellationToken cancellationToken = default)
    {
        var exists = await _siteCheckRepository.GetByIdForSiteAsync(siteId, siteCheckId, cancellationToken);
        if (exists == null)
        {
            return null;
        }

        return await _siteCheckRepository.GetScreenshotAsync(siteCheckId, cancellationToken);
    }
}
