using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SiteChecker.Backend.Extensions;
using SiteChecker.Backend.Services.VPN;
using SiteChecker.Database;
using SiteChecker.Database.Model;
using SiteChecker.Scraper;

namespace SiteChecker.Backend.Services.CheckQueue;

public class SiteCheckQueueProcessor : BackgroundService
{
    private readonly Stopwatch _stopwatch = new();
    private readonly int _vpnChangeInterval;

    private readonly IConfiguration _config;
    private readonly ISiteCheckQueueService _siteCheckQueue;
    private readonly ILogger<SiteCheckQueueProcessor> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IScraperService _scraperService;
    private readonly PiaService _piaService;

    public SiteCheckQueueProcessor(
        IConfiguration configuration,
        ISiteCheckQueueService siteCheckQueue,
        ILogger<SiteCheckQueueProcessor> logger,
        IServiceScopeFactory scopeFactory,
        IScraperService scraperService,
        PiaService piaService)
    {
        _config = configuration;
        _siteCheckQueue = siteCheckQueue;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _scraperService = scraperService;
        _piaService = piaService;

        if (!int.TryParse(_config["VPN_CHANGE_INTERVAL"], out var parsedInterval))
        {
            parsedInterval = 15;
        }
        _vpnChangeInterval = parsedInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stopwatch.Start();
        while (!stoppingToken.IsCancellationRequested)
        {
            var siteCheckId = await _siteCheckQueue.DequeueAsync(stoppingToken);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SiteCheckerDbContext>();
            var siteCheck = await dbContext.SiteChecks
                .Include(sc => sc.Site)
                .FirstOrDefaultAsync(sc => sc.Id == siteCheckId, stoppingToken);
            if (siteCheck == null)
            {
                _logger.LogWarning("Site check with ID {SiteCheckId} not found, skipping.", siteCheckId);
                continue;
            }

            try
            {
                await PerformCheckAsync(siteCheck, dbContext, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred executing {CheckName}.", siteCheck.Site.Name);

                siteCheck.Update(ex);
                await dbContext.SaveChangesAsync(stoppingToken);
            }
        }
    }

    public async Task PerformCheckAsync(
        SiteCheck siteCheck,
        SiteCheckerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        siteCheck.Status = CheckStatus.Checking;
        siteCheck.VpnLocationId = await GetVpnLocationAsync(siteCheck, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var request = new ScrapeRequest
        {
            Id = siteCheck.Id,
            ScraperId = siteCheck.Site.ScraperId,
            BrowserType = _scraperService.GetBrowserType(siteCheck.Site.UseVpn),
            AlwaysTakeScreenshot = siteCheck.Site.AlwaysTakeScreenshot,
        };

        var result = await _scraperService.ScrapeContentAsync(request);
        siteCheck.Update(result);

        if (result.Screenshot is not null)
        {
            var screenshot = new SiteCheckScreenshot(siteCheck, result.Screenshot);
            await dbContext.SiteCheckScreenshots.AddAsync(screenshot, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> GetVpnLocationAsync(SiteCheck siteCheck, CancellationToken cancellationToken)
    {
        var browserType = _scraperService.GetBrowserType(siteCheck.Site.UseVpn);
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
            var location = _stopwatch.Elapsed.TotalMinutes >= _vpnChangeInterval
                ? await ChangeVpnRegionAsync(false, cancellationToken)
                : await _piaService.GetCurrentLocationAsync(cancellationToken);
            return location.Name;
        }

        throw new InvalidOperationException($"Unsupported browser type: {browserType}");
    }

    private async Task<PiaLocation> ChangeVpnRegionAsync(bool excludeCurrent, CancellationToken cancellationToken)
    {
        _stopwatch.Restart();

        var newLocation = await _piaService.ChangeLocationAsync(excludeCurrent, cancellationToken);
        _logger.LogInformation("VPN region changed to {Region}.", newLocation.Name);
        return newLocation;
    }
}
