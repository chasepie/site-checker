using SiteChecker.Domain.Entities;
using SiteChecker.Domain.Ports;

namespace SiteChecker.Application.UseCases;

/// <summary>
/// Evaluates all scheduled sites and creates new <see cref="SiteCheck"/> records for those that
/// are due. Extracted from SiteCheckTimer.CheckForQueableSitesAsync.
/// </summary>
public class ScheduleSiteChecksUseCase(
    ISiteRepository siteRepository,
    ISiteCheckRepository siteCheckRepository)
{
    private readonly ISiteRepository _siteRepository = siteRepository;
    private readonly ISiteCheckRepository _siteCheckRepository = siteCheckRepository;

    /// <summary>
    /// Creates a new <see cref="SiteCheck"/> for each site whose schedule is due at
    /// <paramref name="now"/>.
    /// </summary>
    /// <param name="now">The current local date/time used for schedule evaluation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ExecuteAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var sites = await _siteRepository.GetAllAsync(cancellationToken);

        foreach (var site in sites)
        {
            var latestCheck = await _siteCheckRepository.GetLatestForSiteAsync(
                site.Id, cancellationToken);

            // Skip sites that already have an in-progress check.
            if (latestCheck != null && !latestCheck.IsComplete)
            {
                continue;
            }

            // Pass the start date of the last completed check (null if none) so the schedule can
            // determine whether the interval has elapsed.
            if (site.Schedule.IsDueForCheck(latestCheck?.StartDate, now))
            {
                var siteCheck = new SiteCheck(site.Id);
                await _siteCheckRepository.AddAsync(siteCheck, cancellationToken);
            }
        }
    }
}
