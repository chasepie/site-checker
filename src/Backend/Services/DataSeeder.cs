using Microsoft.EntityFrameworkCore;
using SiteChecker.Database;
using SiteChecker.Database.Model;
using SiteChecker.Scraper.Scrapers;

namespace SiteChecker.Backend.Services;

public class DataSeeder(SiteCheckerDbContext dbContext)
{
    private readonly SiteCheckerDbContext _dbContext = dbContext;

    public async Task SeedDataAsync(CancellationToken cancellationToken = default)
    {
        List<Site> sites = [
            piaSite,
            botDetectionSite,
        ];

        foreach (var site in sites)
        {
            var exists = await _dbContext.Sites.AnyAsync(
                s => s.ScraperId == site.ScraperId,
                cancellationToken);
            if (!exists)
            {
                await _dbContext.Sites.AddAsync(site, cancellationToken);
            }
        }

        var scraperIds = sites.Select(s => s.ScraperId);
        var toRemove = _dbContext.Sites.Where(s => !scraperIds.Contains(s.ScraperId));
        _dbContext.Sites.RemoveRange(toRemove);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private readonly Site piaSite = new()
    {
        ScraperId = PiaLocationScraper.ScraperId,
        Url = new Uri(PiaLocationScraper.DefaultUrl),
        Name = "PIA Location",
        AlwaysTakeScreenshot = true,
        UseVpn = true,
    };

    private readonly Site botDetectionSite = new()
    {
        ScraperId = BotDetectionScraper.ScraperId,
        Url = new Uri(BotDetectionScraper.DefaultUrl),
        Name = "Bot Detection",
        AlwaysTakeScreenshot = true,
        UseVpn = false,
    };
}
