namespace SiteChecker.Scraper.Test;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SiteChecker.Scraper;

[TestClass]
public sealed class ScraperServiceTests
{
    private static ScraperService CreateService(IConfiguration config)
        => new([], config, NullLogger<ScraperService>.Instance);

    [TestMethod]
    public void GetBrowserType_ReturnsLocal_WhenUseLocalBrowserIsTrue_UseVpnFalse()
    {
        var config = Substitute.For<IConfiguration>();
        config[ScraperService.UseLocalBrowserKey].Returns("true");
        var service = CreateService(config);

        Assert.AreEqual(BrowserType.Local, service.GetBrowserType(false));
    }

    [TestMethod]
    public void GetBrowserType_ReturnsLocal_WhenUseLocalBrowserIsTrue_UseVpnTrue()
    {
        var config = Substitute.For<IConfiguration>();
        config[ScraperService.UseLocalBrowserKey].Returns("true");
        config[ScraperService.BrowserlessUrlVpnKey].Returns("http://vpn-host");
        var service = CreateService(config);

        Assert.AreEqual(BrowserType.Local, service.GetBrowserType(true));
    }

    [TestMethod]
    public void GetBrowserType_DoesNotReturnLocal_WhenUseLocalBrowserIsFalse()
    {
        var config = Substitute.For<IConfiguration>();
        config[ScraperService.UseLocalBrowserKey].Returns("false");
        config[ScraperService.BrowserlessUrlKey].Returns("http://browserless-host");
        var service = CreateService(config);

        Assert.AreNotEqual(BrowserType.Local, service.GetBrowserType(false));
    }

    [TestMethod]
    public void GetBrowserType_FallsBackToLocal_WhenUseLocalBrowserIsNotSet()
    {
        var config = Substitute.For<IConfiguration>();
        var service = CreateService(config);

        Assert.AreEqual(BrowserType.Local, service.GetBrowserType(false));
    }

    [TestMethod]
    public void GetBrowserType_ReturnsBrowserlessVpn_WhenVpnUrlSetAndUseVpnTrue()
    {
        var config = Substitute.For<IConfiguration>();
        config[ScraperService.BrowserlessUrlVpnKey].Returns("http://vpn-host");
        var service = CreateService(config);

        Assert.AreEqual(BrowserType.BrowserlessVpn, service.GetBrowserType(true));
    }

    [TestMethod]
    public void GetBrowserType_DoesNotReturnBrowserlessVpn_WhenVpnUrlSetButUseVpnFalse()
    {
        var config = Substitute.For<IConfiguration>();
        config[ScraperService.BrowserlessUrlVpnKey].Returns("http://vpn-host");
        config[ScraperService.BrowserlessUrlKey].Returns("http://browserless-host");
        var service = CreateService(config);

        Assert.AreEqual(BrowserType.Browserless, service.GetBrowserType(false));
    }

    [TestMethod]
    public void GetBrowserType_ReturnsBrowserless_WhenBrowserlessUrlSetAndUseVpnFalse()
    {
        var config = Substitute.For<IConfiguration>();
        config[ScraperService.BrowserlessUrlKey].Returns("http://browserless-host");
        var service = CreateService(config);

        Assert.AreEqual(BrowserType.Browserless, service.GetBrowserType(false));
    }

    [TestMethod]
    public void GetBrowserType_FallsBackToLocal_WhenNoUrlsConfigured()
    {
        var config = Substitute.For<IConfiguration>();
        var service = CreateService(config);

        Assert.AreEqual(BrowserType.Local, service.GetBrowserType(false));
    }

    [TestMethod]
    public void GetBrowserType_FallsBackToLocal_WhenVpnUrlSetButUseVpnFalseAndNoBrowserlessUrl()
    {
        var config = Substitute.For<IConfiguration>();
        config[ScraperService.BrowserlessUrlVpnKey].Returns("http://vpn-host");
        var service = CreateService(config);

        Assert.AreEqual(BrowserType.Local, service.GetBrowserType(false));
    }
}
