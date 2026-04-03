using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using SiteChecker.Domain;

namespace SiteChecker.Scraper.Scrapers;

public class PiaLocationScraper(
    ILogger<PiaLocationScraper> logger)
    : ScraperBase(logger)
{
    public const string ScraperId = "PIA_LOCATION";
    public const string DefaultUrl = "https://www.privateinternetaccess.com/what-is-my-ip";

    public override string Id => ScraperId;
    public override string Url => DefaultUrl;

    protected override async Task<IScrapeResult> DoScrapeAsync(IPage page, ScrapeRequest request)
    {
        await page.GotoAsync(Url);

        var locator = page.Locator(".exposed-card-container-info .card-info-row:nth-of-type(3) .exposed-info span:nth-of-type(2)");
        var content = await locator.TextContentAsync();

        var result = new SuccessScrapeResult
        {
            Content = content?.Trim() ?? "[no content]",
        };
        return result;
    }
}
