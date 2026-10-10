namespace SiteChecker.Scraper.UnitTests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiteChecker.Scraper;

[TestClass]
public sealed class ScrapeTimeoutsTests
{
    private static ScrapeTimeouts Create(string? scrapeTimeout = null, string? browserlessTimeout = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ScrapeTimeouts.ScrapeTimeoutKey] = scrapeTimeout,
                [ScrapeTimeouts.BrowserlessTimeoutKey] = browserlessTimeout,
            })
            .Build();
        return new ScrapeTimeouts(config, NullLogger<ScrapeTimeouts>.Instance);
    }

    [TestMethod]
    public void Defaults_Are120Seconds_UnderABrowserlessTimeoutOf180()
    {
        var timeouts = Create();

        Assert.AreEqual(TimeSpan.FromSeconds(120), timeouts.Default);
        Assert.AreEqual(TimeSpan.FromSeconds(180), timeouts.BrowserlessTimeout);
        Assert.AreEqual(170, timeouts.MaxSeconds);
    }

    [TestMethod]
    public void ScrapeTimeout_ThatDoesNotLeaveRoomForTheScreenshot_IsRejected()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => Create(scrapeTimeout: "175"));

        Assert.Contains("at most 170 s", ex.Message);
    }

    [TestMethod]
    [DataRow("abc")]
    [DataRow("0")]
    [DataRow("-5")]
    [DataRow("1.5")]
    public void ScrapeTimeout_ThatIsNotAPositiveWholeNumber_IsRejected(string value)
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => Create(scrapeTimeout: value));
    }

    [TestMethod]
    public void BrowserlessTimeout_SetsTheMaximum()
    {
        var timeouts = Create(scrapeTimeout: "30", browserlessTimeout: "60000");

        Assert.AreEqual(50, timeouts.MaxSeconds);
    }

    [TestMethod]
    public void Resolve_UsesTheSitesTimeout_OrTheDefault()
    {
        var timeouts = Create(scrapeTimeout: "30");

        Assert.AreEqual(TimeSpan.FromSeconds(30), timeouts.Resolve(null));
        Assert.AreEqual(TimeSpan.FromSeconds(45), timeouts.Resolve(TimeSpan.FromSeconds(45)));
    }

    [TestMethod]
    public void Resolve_ClampsAStoredTimeoutThatNoLongerFits()
    {
        var timeouts = Create(scrapeTimeout: "30", browserlessTimeout: "60000");

        Assert.AreEqual(TimeSpan.FromSeconds(50), timeouts.Resolve(TimeSpan.FromSeconds(150)));
    }
}
