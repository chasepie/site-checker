using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using SiteChecker.Scraper.Extensions;

namespace SiteChecker.Scraper.Scrapers;

public partial class BotDetectionScraper(
    ILogger<BotDetectionScraper> logger)
    : ScraperBase(logger)
{
    public const string ScraperId = "BOT_DETECTION";
    public const string DefaultUrl = "https://www.browserscan.net/bot-detection";

    public override string Id => ScraperId;
    public override string Url => DefaultUrl;

    protected override async Task<IScrapeResult> DoScrapeAsync(IPage page, ScrapeRequest request)
    {
        await page.GotoAsync(Url);

        var resultsLocator = page.Locator("div", new() { HasTextRegex = TestResultsRegex() });
        await resultsLocator.WaitForAsync();

        var results = await resultsLocator.TextContentAsync();
        var screenshot = await page.TakeFullPageScreenshotAsync();

        if (results == null)
        {
            return new FailureScrapeResult
            {
                ErrorMessage = "Could not find test results on the page.",
                Screenshot = screenshot,
            };
        }

        return new SuccessScrapeResult
        {
            Content = results,
            Screenshot = screenshot,
        };
    }

    [GeneratedRegex(@"^Test Results:\s*(Normal|Robot)")]
    private static partial Regex TestResultsRegex();
}
