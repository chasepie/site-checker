using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SiteChecker.Backend.Extensions;
using SiteChecker.Backend.Services.VPN;
using SiteChecker.Database;
using SiteChecker.Domain.Entities;
using SiteChecker.Domain.Enums;
using SiteChecker.Scraper;

namespace SiteChecker.Backend.Services.CheckQueue;

/// <summary>
/// Background service that dequeues pending checks, executes scrapes, and persists results.
/// </summary>
public class SiteCheckQueueProcessor : BackgroundService
{
    /// <summary>
    /// Tracks elapsed time since the last VPN region change.
    /// </summary>
    private readonly Stopwatch _vpnStopwatch = new();

    /// <summary>
    /// Interval in minutes between VPN region changes.
    /// </summary>
    private readonly int _vpnChangeInterval;

    /// <summary>
    /// Queue service for pending site checks.
    /// </summary>
    private readonly ISiteCheckQueueService _siteCheckQueue;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<SiteCheckQueueProcessor> _logger;

    /// <summary>
    /// Scope factory for resolving scoped services.
    /// </summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// Scraper service.
    /// </summary>
    private readonly IScraperService _scraperService;

    /// <summary>
    /// PIA VPN service.
    /// </summary>
    private readonly PiaService _piaService;

    public SiteCheckQueueProcessor(
        IConfiguration configuration,
        ISiteCheckQueueService siteCheckQueue,
        ILogger<SiteCheckQueueProcessor> logger,
        IServiceScopeFactory scopeFactory,
        IScraperService scraperService,
        PiaService piaService)
    {
        _siteCheckQueue = siteCheckQueue;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _scraperService = scraperService;
        _piaService = piaService;

        if (!int.TryParse(configuration["VPN_CHANGE_INTERVAL"], out var parsedInterval))
        {
            parsedInterval = 15;
        }
        _vpnChangeInterval = parsedInterval;
    }

    /// <summary>
    /// Processes queued checks until the service is stopped.
    /// </summary>
    /// <param name="stoppingToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _vpnStopwatch.Start();

        while (!stoppingToken.IsCancellationRequested)
        {
            var siteCheckId = await _siteCheckQueue.DequeueAsync(stoppingToken);

            // A new scope per check ensures each check gets a fresh DbContext with an isolated
            // change tracker, preventing stale entity state from one check affecting the next.
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SiteCheckerDbContext>();
            var siteCheck = await dbContext.SiteChecks
                .FirstOrDefaultAsync(sc => sc.Id == siteCheckId, stoppingToken);
            if (siteCheck == null)
            {
                _logger.LogWarning("Site check with ID {SiteCheckId} not found, skipping.", siteCheckId);
                continue;
            }

            var site = await dbContext.Sites
                    .FirstOrDefaultAsync(s => s.Id == siteCheck.SiteId, stoppingToken);
            if (site == null)
            {
                _logger.LogWarning("Site for check {SiteCheckId} not found, skipping.", siteCheckId);
                continue;
            }

            try
            {
                await PerformCheckAsync(siteCheck, site, dbContext, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred executing {CheckName}.", site.Name);
                siteCheck.Update(ex);
                await dbContext.SaveChangesAsync(stoppingToken);
            }
        }
    }

    /// <summary>
    /// Performs a site check using the scraper service and updates the database with the results.
    /// </summary>
    /// <param name="siteCheck">The site check to perform.</param>
    /// <param name="site">The site being checked.</param>
    /// <param name="dbContext">The database context.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task PerformCheckAsync(
        SiteCheck siteCheck,
        Site site,
        SiteCheckerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        siteCheck.Status = CheckStatus.Checking;
        siteCheck.VpnLocationId = await GetVpnLocationAsync(site, cancellationToken);

        // Save before scraping so clients receive a real-time status update via SignalR while the
        // (potentially long-running) scrape is in progress. An EF Core SaveChanges interceptor
        // hooks into every save and automatically broadcasts entity changes to all connected clients.
        await dbContext.SaveChangesAsync(cancellationToken);

        var request = new ScrapeRequest
        {
            Id = siteCheck.Id,
            ScraperId = site.ScraperId,
            BrowserType = _scraperService.GetBrowserType(site.UseVpn),
            AlwaysTakeScreenshot = site.AlwaysTakeScreenshot,
        };

        var result = await _scraperService.ScrapeContentAsync(request);
        siteCheck.Update(result);

        if (result.Screenshot is not null)
        {
            var screenshot = new SiteCheckScreenshot(siteCheck.Id, result.Screenshot);
            await dbContext.SiteCheckScreenshots.AddAsync(screenshot, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Resolves the location label used for the current check based on browser mode. If using a
    /// VPN, may change the region if the configured interval has elapsed.
    /// </summary>
    /// <param name="site">The site being checked.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>The name of the VPN location to use.</returns>
    /// <exception cref="InvalidOperationException">Thrown if an unsupported browser type is encountered.</exception>
    private async Task<string> GetVpnLocationAsync(Site site, CancellationToken cancellationToken)
    {
        var browserType = _scraperService.GetBrowserType(site.UseVpn);
        if (browserType == BrowserType.Local)
        {
            return "Local browser";
        }

        if (browserType == BrowserType.Browserless)
        {
            return "No VPN";
        }

        if (browserType == BrowserType.BrowserlessVpn)
        {
            var location = _vpnStopwatch.Elapsed.TotalMinutes >= _vpnChangeInterval
                ? await ChangeVpnRegionAsync(false, cancellationToken)
                : await _piaService.GetCurrentLocationAsync(cancellationToken);
            return location.Name;
        }

        throw new InvalidOperationException($"Unsupported browser type: {browserType}");
    }

    /// <summary>
    /// Changes the VPN region and returns the selected location.
    /// </summary>
    /// <param name="excludeCurrent">Whether to exclude the current VPN location when selecting the next location.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>The new VPN location.</returns>
    private async Task<PiaLocation> ChangeVpnRegionAsync(bool excludeCurrent, CancellationToken cancellationToken)
    {
        _vpnStopwatch.Restart();

        var newLocation = await _piaService.ChangeLocationAsync(excludeCurrent, cancellationToken);
        _logger.LogInformation("VPN region changed to {Region}.", newLocation.Name);
        return newLocation;
    }
}
