using SiteChecker.Domain.Entities;
using SiteChecker.Domain.Ports;
using SiteChecker.Scraper.Scrapers;

namespace SiteChecker.Backend.Services;

public class DataSeeder(ISiteRepository siteRepository)
{
    public async Task SeedDataAsync(CancellationToken cancellationToken = default)
    {
        List<Site> seeds = [
            piaSite,
            botDetectionSite,
        ];

        var existing = await siteRepository.GetAllAsync(cancellationToken);

        foreach (var seed in seeds)
        {
            if (!existing.Any(s => s.ScraperId == seed.ScraperId))
            {
                await siteRepository.AddAsync(seed, cancellationToken);
            }
        }

        var scraperIds = seeds.Select(s => s.ScraperId).ToHashSet();
        foreach (var site in existing.Where(s => !scraperIds.Contains(s.ScraperId)))
        {
            await siteRepository.RemoveAsync(site, cancellationToken);
        }
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
