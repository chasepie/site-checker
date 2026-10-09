using Microsoft.Extensions.Configuration;

namespace SiteChecker.Scraper.Browsers;

/// <summary>
/// Chooses the browser a Site is scraped in, from configuration alone. The Site Check Runner needs
/// the choice before the scrape, to rotate the VPN, so it's separate from connecting to the browser.
/// </summary>
public sealed class BrowserSelector(IConfiguration config)
{
    public const string UseLocalBrowserKey = "USE_LOCAL_BROWSER";

    private readonly IConfiguration _config = config;

    /// <summary>
    /// The browser for a Site: local Playwright when <c>USE_LOCAL_BROWSER</c> is set, otherwise
    /// Browserless or Browserless VPN, depending on the Site and which URLs are configured.
    /// </summary>
    public BrowserType GetBrowserType(bool useVpn)
    {
        if (bool.TryParse(_config[UseLocalBrowserKey], out var useLocal) && useLocal)
        {
            return BrowserType.Local;
        }

        var browserlessUrlVpn = _config[BrowserProvider.BrowserlessUrlVpnKey];
        if (!string.IsNullOrWhiteSpace(browserlessUrlVpn) && useVpn)
        {
            return BrowserType.BrowserlessVpn;
        }

        var browserlessUrl = _config[BrowserProvider.BrowserlessUrlKey];
        if (!string.IsNullOrWhiteSpace(browserlessUrl) && !useVpn)
        {
            return BrowserType.Browserless;
        }

        return BrowserType.Local;
    }
}
