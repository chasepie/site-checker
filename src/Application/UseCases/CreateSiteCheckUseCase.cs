using SiteChecker.Domain;
using SiteChecker.Domain.Entities;
using SiteChecker.Domain.Ports;

namespace SiteChecker.Application.UseCases;

/// <summary>
/// Creates a new <see cref="SiteCheck"/> for a site. Extracted from
/// SiteCheckController.CreateSiteCheck and CreateEmptyCheck.
/// </summary>
public class CreateSiteCheckUseCase(
    ISiteRepository siteRepository,
    ISiteCheckRepository siteCheckRepository)
{
    private readonly ISiteRepository _siteRepository = siteRepository;
    private readonly ISiteCheckRepository _siteCheckRepository = siteCheckRepository;

    /// <summary>
    /// Creates a pending site check that will be picked up and processed by the queue.
    /// </summary>
    /// <param name="siteId">ID of the site to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created check, or null if the site does not exist.</returns>
    public async Task<SiteCheck?> ExecuteAsync(int siteId, CancellationToken cancellationToken = default)
    {
        var site = await _siteRepository.GetByIdAsync(siteId, cancellationToken);
        if (site == null)
        {
            return null;
        }

        var siteCheck = new SiteCheck(site.Id);
        await _siteCheckRepository.AddAsync(siteCheck, cancellationToken);
        return siteCheck;
    }

    /// <summary>
    /// Creates a completed check with empty content — useful for establishing a baseline without
    /// triggering a real scrape. Equivalent to the legacy CreateEmptyCheck controller action.
    /// </summary>
    /// <param name="siteId">ID of the site.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created check, or null if the site does not exist.</returns>
    public async Task<SiteCheck?> ExecuteEmptyAsync(int siteId, CancellationToken cancellationToken = default)
    {
        var site = await _siteRepository.GetByIdAsync(siteId, cancellationToken);
        if (site == null)
        {
            return null;
        }

        var siteCheck = new SiteCheck(site.Id);
        siteCheck.CompleteWithResult(new SuccessScrapeResult { Content = "[Empty Check]" });
        await _siteCheckRepository.AddAsync(siteCheck, cancellationToken);
        return siteCheck;
    }
}
