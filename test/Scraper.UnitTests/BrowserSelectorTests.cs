namespace SiteChecker.Scraper.UnitTests;

using Microsoft.Extensions.Configuration;
using NSubstitute;
using SiteChecker.Scraper;
using SiteChecker.Scraper.Browsers;

[TestClass]
public sealed class BrowserSelectorTests
{
    private static BrowserSelector CreateService(IConfiguration config) => new(config);

    [TestMethod]
    public void GetBrowserType_ReturnsLocal_WhenUseLocalBrowserIsTrue_UseVpnFalse()
    {
        var config = Substitute.For<IConfiguration>();
        config[BrowserSelector.UseLocalBrowserKey].Returns("true");
        var service = CreateService(config);

        Assert.AreEqual(BrowserType.Local, service.GetBrowserType(false));
    }

    [TestMethod]
    public void GetBrowserType_ReturnsLocal_WhenUseLocalBrowserIsTrue_UseVpnTrue()
    {
        var config = Substitute.For<IConfiguration>();
        config[BrowserSelector.UseLocalBrowserKey].Returns("true");
        config[BrowserProvider.BrowserlessUrlVpnKey].Returns("http://vpn-host");
        var service = CreateService(config);

        Assert.AreEqual(BrowserType.Local, service.GetBrowserType(true));
    }

    [TestMethod]
    public void GetBrowserType_DoesNotReturnLocal_WhenUseLocalBrowserIsFalse()
    {
        var config = Substitute.For<IConfiguration>();
        config[BrowserSelector.UseLocalBrowserKey].Returns("false");
        config[BrowserProvider.BrowserlessUrlKey].Returns("http://browserless-host");
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
        config[BrowserProvider.BrowserlessUrlVpnKey].Returns("http://vpn-host");
        var service = CreateService(config);

        Assert.AreEqual(BrowserType.BrowserlessVpn, service.GetBrowserType(true));
    }

    [TestMethod]
    public void GetBrowserType_DoesNotReturnBrowserlessVpn_WhenVpnUrlSetButUseVpnFalse()
    {
        var config = Substitute.For<IConfiguration>();
        config[BrowserProvider.BrowserlessUrlVpnKey].Returns("http://vpn-host");
        config[BrowserProvider.BrowserlessUrlKey].Returns("http://browserless-host");
        var service = CreateService(config);

        Assert.AreEqual(BrowserType.Browserless, service.GetBrowserType(false));
    }

    [TestMethod]
    public void GetBrowserType_ReturnsBrowserless_WhenBrowserlessUrlSetAndUseVpnFalse()
    {
        var config = Substitute.For<IConfiguration>();
        config[BrowserProvider.BrowserlessUrlKey].Returns("http://browserless-host");
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
        config[BrowserProvider.BrowserlessUrlVpnKey].Returns("http://vpn-host");
        var service = CreateService(config);

        Assert.AreEqual(BrowserType.Local, service.GetBrowserType(false));
    }
}
