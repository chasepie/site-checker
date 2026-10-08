using System.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace SiteChecker.Scraper.Browsers;

/// <summary>
/// Chooses and connects to the browser a scrape runs in.
/// </summary>
public interface IBrowserProvider
{
    /// <summary>
    /// The browser for a Site: local Playwright when <c>USE_LOCAL_BROWSER</c> is set, otherwise
    /// Browserless or Browserless VPN, depending on the Site and which URLs are configured.
    /// </summary>
    BrowserType GetBrowserType(bool useVpn);

    /// <summary>
    /// Connects to the browser and returns a 1920×1080 page in it.
    /// </summary>
    Task<IBrowserSession> OpenAsync(BrowserType browserType, CancellationToken cancellationToken);
}

/// <summary>
/// A connected browser and the page a scrape uses. Disposing it closes everything.
/// </summary>
public interface IBrowserSession : IAsyncDisposable
{
    IPage Page { get; }

    /// <summary>
    /// Closes the browser context and connection, which makes any pending Playwright call throw.
    /// Safe to call more than once.
    /// </summary>
    Task CloseAsync();
}

public sealed class BrowserProvider(
    IConfiguration config,
    ILogger<BrowserProvider> logger) : IBrowserProvider
{
    public const string BrowserlessUrlKey = "BROWSERLESS_URL";
    public const string BrowserlessUrlVpnKey = "BROWSERLESS_URL_VPN";
    public const string UseLocalBrowserKey = "USE_LOCAL_BROWSER";

    private readonly IConfiguration _config = config;
    private readonly ILogger<BrowserProvider> _logger = logger;

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

    public async Task<IBrowserSession> OpenAsync(BrowserType browserType, CancellationToken cancellationToken)
    {
        _logger.LogTrace("Getting Playwright instance...");
        var playwright = await Playwright.CreateAsync();
        IBrowser? browser = null;
        try
        {
            _logger.LogTrace("Getting browser instance ({BrowserType})...", browserType);
            browser = await GetBrowserAsync(playwright, browserType).WaitAsync(cancellationToken);

            _logger.LogTrace("Getting browser context...");
            var context = browser.Contexts.FirstOrDefault()
                ?? await browser.NewContextAsync();

            _logger.LogTrace("Getting browser page...");
            var page = context.Pages.FirstOrDefault()
                ?? await context.NewPageAsync();
            await page.SetViewportSizeAsync(1920, 1080);

            return new BrowserSession(playwright, browser, context, page, _logger);
        }
        catch
        {
            if (browser != null)
            {
                await browser.DisposeAsync();
            }
            playwright.Dispose();
            throw;
        }
    }

    private async Task<IBrowser> GetBrowserAsync(IPlaywright playwright, BrowserType browserType)
    {
        if (browserType == BrowserType.Local)
        {
            _logger.LogTrace("Launching local browser");
            return await playwright.Chromium.ConnectAsync("ws://localhost:3123/playwright");
        }

        var configKey = browserType switch
        {
            BrowserType.BrowserlessVpn => BrowserlessUrlVpnKey,
            BrowserType.Browserless => BrowserlessUrlKey,
            _ => throw new InvalidOperationException($"Unsupported browser type: {browserType}"),
        };

        var configValue = _config[configKey];
        if (string.IsNullOrWhiteSpace(configValue))
        {
            throw new InvalidOperationException($"Tried to launch browser of type {browserType}, but no URL was configured via '{configKey}'");
        }
        return await LaunchBrowserlessBrowserAsync(playwright, configValue);
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

    private sealed class BrowserSession(
        IPlaywright playwright,
        IBrowser browser,
        IBrowserContext context,
        IPage page,
        ILogger logger) : IBrowserSession
    {
        /// <summary>
        /// Closing a connection the browser already dropped can hang; don't let it hold the runner.
        /// </summary>
        private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(10);

        private int _closed;

        public IPage Page => page;

        public async Task CloseAsync()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 1)
            {
                return;
            }

            await CloseQuietlyAsync(() => context.CloseAsync(), "browser context");
            await CloseQuietlyAsync(() => browser.CloseAsync(), "browser");
            playwright.Dispose();
        }

        private async Task CloseQuietlyAsync(Func<Task> close, string what)
        {
            try
            {
                await close().WaitAsync(CloseTimeout);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Closing the {What} failed.", what);
            }
        }

        public async ValueTask DisposeAsync() => await CloseAsync();
    }
}
