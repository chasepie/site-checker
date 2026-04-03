using SiteChecker.Domain.Entities;
using SiteChecker.Domain.Enums;
using SiteChecker.Domain.Ports;
using SiteChecker.Domain.ValueObjects;

namespace SiteChecker.Application.UseCases;

/// <summary>
/// Orchestrates a single site check: sets status, scrapes the site, persists the result and
/// screenshot, then dispatches domain events. Extracted from SiteCheckQueueProcessor.PerformCheckAsync.
/// </summary>
public class PerformSiteCheckUseCase(
    ISiteRepository siteRepository,
    ISiteCheckRepository siteCheckRepository,
    IVpnService vpnService,
    IScrapingService scrapingService)
{
    private readonly ISiteRepository _siteRepository = siteRepository;
    private readonly ISiteCheckRepository _siteCheckRepository = siteCheckRepository;
    private readonly IVpnService _vpnService = vpnService;
    private readonly IScrapingService _scrapingService = scrapingService;

    /// <summary>
    /// Executes the site check for the given <paramref name="siteCheckId"/>.
    /// </summary>
    /// <param name="siteCheckId">ID of the pending <see cref="SiteCheck"/> to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ExecuteAsync(int siteCheckId, CancellationToken cancellationToken = default)
    {
        var siteCheck = await _siteCheckRepository.GetByIdAsync(siteCheckId, cancellationToken)
            ?? throw new InvalidOperationException($"SiteCheck {siteCheckId} not found.");

        var site = await _siteRepository.GetByIdAsync(siteCheck.SiteId, cancellationToken)
            ?? throw new InvalidOperationException($"Site {siteCheck.SiteId} not found.");

        // Set status to Checking and record VPN location before starting the scrape so
        // connected clients receive a real-time status update via SignalR.
        siteCheck.Status = CheckStatus.Checking;
        var vpnLocation = site.UseVpn
            ? await _vpnService.GetCurrentLocationAsync(cancellationToken)
            : VpnLocation.NoVpn;
        siteCheck.VpnLocationId = vpnLocation.Id;
        await _siteCheckRepository.UpdateAsync(siteCheck, cancellationToken);

        var result = await _scrapingService.ScrapeAsync(site, siteCheck, cancellationToken);
        siteCheck.CompleteWithResult(result);

        if (result.Screenshot is not null)
        {
            var screenshot = new SiteCheckScreenshot(siteCheck.Id, result.Screenshot);
            await _siteCheckRepository.AddScreenshotAsync(screenshot, cancellationToken);
        }

        await _siteCheckRepository.UpdateAsync(siteCheck, cancellationToken);
    }
}
