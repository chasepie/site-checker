using System.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using SiteChecker.Domain;
using SiteChecker.Domain.Entities;
using SiteChecker.Domain.Exceptions;
using SiteChecker.Domain.Ports;
using SiteChecker.Scraper.Scrapers;

namespace SiteChecker.Scraper;

public interface IScraperService
{
    Task<IScrapeResult> ScrapeContentAsync(ScrapeRequest request);
    BrowserType GetBrowserType(bool useVpn);
}

public partial class ScraperService(
    IEnumerable<IScraper> scrapers,
    IConfiguration config,
    ILogger<ScraperService> logger) : IScraperService, IScrapingService
{
    public const string BrowserlessUrlKey = "BROWSERLESS_URL";
    public const string BrowserlessUrlVpnKey = "BROWSERLESS_URL_VPN";
    public const string UseLocalBrowserKey = "USE_LOCAL_BROWSER";

    private readonly IEnumerable<IScraper> _scrapers = scrapers;
    private readonly IConfiguration _config = config;
    private readonly ILogger<ScraperService> _logger = logger;

    private async Task<IBrowser> LaunchLocalBrowserAsync(IPlaywright playwright)
    {
        _logger.LogTrace("Launching local browser");
        return await playwright.Chromium.ConnectAsync("ws://localhost:3123/playwright");
    }

    private async Task<IBrowser> LaunchBrowserlessBrowserAsync(IPlaywright playwright, string baseUrl)
    {
        // https://docs.browserless.io/baas/launch-options#configuration-methods
        _logger.LogTrace("Launching Browserless browser");

        var query = HttpUtility.ParseQueryString(string.Empty);

        var token = _config["BROWSERLESS_TOKEN"];
        if (!string.IsNullOrWhiteSpace(token))
        {
            query["token"] = token;
        }

        query["headless"] = false.ToString().ToLowerInvariant();
        query["stealth"] = true.ToString().ToLowerInvariant();

        var browserlessUrl = $"{baseUrl}?{query}";
        return await playwright.Chromium.ConnectOverCDPAsync(browserlessUrl);
    }

    private async Task<IBrowser> GetBrowserAsync(IPlaywright playwright, ScrapeRequest request)
    {
        if (request.BrowserType == BrowserType.Local)
        {
            return await LaunchLocalBrowserAsync(playwright);
        }

        var configKey = request.BrowserType switch
        {
            BrowserType.BrowserlessVpn => BrowserlessUrlVpnKey,
            BrowserType.Browserless => BrowserlessUrlKey,
            _ => throw new InvalidOperationException($"Unsupported browser type: {request.BrowserType}"),
        };

        var configValue = _config[configKey];
        if (string.IsNullOrWhiteSpace(configValue))
        {
            throw new InvalidOperationException($"Tried to launch browser of type {request.BrowserType}, but no URL was configured via '{configKey}'");
        }
        return await LaunchBrowserlessBrowserAsync(playwright, configValue);
    }

    private async Task<IScrapeResult> RunScraperAsync(ScrapeRequest request)
    {
        var scraper = _scrapers.FirstOrDefault(s => s.Id.Equals(request.ScraperId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Scraper with ID '{request.ScraperId}' not found.");

        _logger.LogTrace("Getting Playwright instance...");
        using var playwright = await Playwright.CreateAsync();

        _logger.LogTrace("Getting browser instance ({BrowserType})...", request.BrowserType);
        await using var browser = await GetBrowserAsync(playwright, request);

        _logger.LogTrace("Getting browser context...");
        await using var context = browser.Contexts.FirstOrDefault()
            ?? await browser.NewContextAsync();

        _logger.LogTrace("Getting browser page...");
        var page = context.Pages.FirstOrDefault()
            ?? await context.NewPageAsync();
        await page.SetViewportSizeAsync(1920, 1080);

        try
        {
            request.LogInfo(_logger, "Running scraper");
            return await scraper.ScrapeAsync(page, request);
        }
        finally
        {
            await context.CloseAsync();
            await browser.CloseAsync();
        }
    }

    public async Task<IScrapeResult> ScrapeContentAsync(ScrapeRequest request)
    {
        try
        {
            return await RunScraperAsync(request);
        }
        catch (Exception ex)
        {
            if (ex is not ScraperException scraperEx)
            {
                scraperEx = new UnexpectedScraperException($"An unexpected error occurred during scraping ({nameof(ScraperService)})", ex);
            }

            request.LogError(_logger, "Scraper Failed", scraperEx);
            return FailureScrapeResult.FromException(scraperEx);
        }
    }

    public BrowserType GetBrowserType(bool useVpn)
    {
        if (bool.TryParse(_config[UseLocalBrowserKey], out var useLocal) && useLocal)
        {
            return BrowserType.Local;
        }

        var browserlessUrlVpn = _config[BrowserlessUrlVpnKey];
        if (!string.IsNullOrWhiteSpace(browserlessUrlVpn) && useVpn)
        {
            return BrowserType.BrowserlessVpn;
        }

        var browserlessUrl = _config[BrowserlessUrlKey];
        if (!string.IsNullOrWhiteSpace(browserlessUrl) && !useVpn)
        {
            return BrowserType.Browserless;
        }

        return BrowserType.Local;
    }

    public Task<IScrapeResult> ScrapeAsync(Site site, SiteCheck siteCheck, CancellationToken cancellationToken = default)
    {
        var request = new ScrapeRequest
        {
            Id = siteCheck.Id,
            ScraperId = site.ScraperId,
            BrowserType = GetBrowserType(site.UseVpn),
            AlwaysTakeScreenshot = site.AlwaysTakeScreenshot,
        };
        return ScrapeContentAsync(request);
    }
}

public static class ScraperServiceExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddScraperServices()
        {
            return services
                .AddSingleton<ScraperService>()
                .AddSingleton<IScraperService>(sp => sp.GetRequiredService<ScraperService>())
                .AddSingleton<IScrapingService>(sp => sp.GetRequiredService<ScraperService>())
                .AddScraper<PiaLocationScraper>()
                .AddScraper<BotDetectionScraper>();
        }
    }
}
