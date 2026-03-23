using Microsoft.EntityFrameworkCore;
using SiteChecker.Database;
using SiteChecker.Database.Model;

namespace SiteChecker.Backend.Services.CheckQueue;

public class SiteCheckTimer(
    ILogger<SiteCheckTimer> logger,
    IServiceScopeFactory scopeFactory)
    : BackgroundService
{
    private readonly ILogger<SiteCheckTimer> _logger = logger;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await DoCheckAsync(stoppingToken);
        var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await DoCheckAsync(stoppingToken);
        }
    }

    private async Task DoCheckAsync(CancellationToken stoppingToken)
    {
        await _semaphore.WaitAsync(stoppingToken);
        try
        {
            await CheckForQueableSitesAsync(stoppingToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task CheckForQueableSitesAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Checking sites...");

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SiteCheckerDbContext>();

        // Schedule start/end are in local time
        var now = DateTime.Now;
        var nowTime = TimeOnly.FromDateTime(now);
        var sites = await dbContext.Sites
            .Where(s =>
                s.Schedule.Enabled
                && s.Schedule.Interval.HasValue
                && s.Schedule.Start.HasValue
                && s.Schedule.End.HasValue)
            .ToListAsync(stoppingToken);

        _logger.LogDebug("Beginning scheduled site checks ({Time})", nowTime);
        if (sites.Count == 0)
        {
            _logger.LogInformation("No sites to check.");
            return;
        }

        foreach (var site in sites)
        {
            _logger.LogDebug("Checking site {SiteName}: {Start} - {End}",
                site.Name, site.Schedule.Start!.Value, site.Schedule.End!.Value);
            if (!nowTime.IsBetween(site.Schedule.Start!.Value, site.Schedule.End!.Value))
            {
                continue;
            }

            async Task CreateSiteCheck(Site site)
            {
                _logger.LogInformation("Queueing check for {SiteName}.", site.Name);
                var siteCheck = new SiteCheck(site);
                await dbContext.SiteChecks.AddAsync(siteCheck, stoppingToken);
                await dbContext.SaveChangesAsync(stoppingToken);
            }

            var latestCheck = await dbContext.SiteChecks
                .Where(sc => sc.SiteId == site.Id)
                .OrderByDescending(sc => sc.StartDate)
                .FirstOrDefaultAsync(stoppingToken);
            if (latestCheck == null)
            {
                await CreateSiteCheck(site);
            }
            else if (latestCheck.Status == CheckStatus.Done || latestCheck.Status == CheckStatus.Failed)
            {
                var lastStartDateLocal = latestCheck.StartDate.ToLocalTime(); // Start date is stored in UTC
                var ts = TimeSpan.FromMinutes(site.Schedule.Interval!.Value);
                var intervalTimeAgo = now.Subtract(ts); // current time - interval
                if (lastStartDateLocal <= intervalTimeAgo)
                {
                    await CreateSiteCheck(site);
                }
            }
        }

        _logger.LogInformation("Sites checked.");
    }
}
