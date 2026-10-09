namespace SiteChecker.Scraper.UnitTests;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using SiteChecker.Scraper;
using SiteChecker.Scraper.Scripts;
using SiteChecker.Scripting;
using BrowserType = SiteChecker.Scraper.BrowserType;

/// <summary>
/// Requests and results survive a trip through <see cref="ScrapeJson"/>, as they do between the
/// app and the Scrape Worker.
/// </summary>
[TestClass]
public sealed class ScrapeJsonTests
{
    private static T RoundTrip<T>(T value)
        => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, ScrapeJson.Options), ScrapeJson.Options)!;

    [TestMethod]
    public void Request_RoundTrips_WithItsScraperKind()
    {
        var request = new ScrapeRequest
        {
            SiteCheckId = 7,
            Site = new ScrapeSite(3, "Site", new Uri("https://example.com/a%20b?q=1"), UseVpn: true),
            Scraper = new ScriptSpec("Script.cs", "public sealed class S {}", "abc123"),
            BrowserType = BrowserType.BrowserlessVpn,
            Timeout = TimeSpan.FromSeconds(45),
            AlwaysTakeScreenshot = true,
        };

        var json = JsonSerializer.Serialize(request, ScrapeJson.Options);
        var copy = RoundTrip(request);

        Assert.AreEqual(request, copy);
        Assert.Contains("\"kind\":\"script\"", json);
        Assert.Contains("\"browserType\":\"BrowserlessVpn\"", json);
        Assert.DoesNotContain("isTestRun", json);
    }

    [TestMethod]
    public void TestRunRequest_RoundTrips_AsATestRun()
    {
        var request = new ScrapeRequest
        {
            Site = new ScrapeSite(null, "Test Run", new Uri("https://example.com/"), UseVpn: false),
            Scraper = new ScriptSpec("Script.cs", "source", "hash"),
        };

        var copy = RoundTrip(request);

        Assert.IsTrue(copy.IsTestRun);
        Assert.IsNull(copy.Site.Id);
        Assert.IsNull(copy.Timeout);
    }

    [TestMethod]
    public void SucceededResult_RoundTrips()
    {
        var result = ScrapeResult.Succeeded("content") with
        {
            Screenshot = [1, 2, 3],
            Duration = TimeSpan.FromMilliseconds(1234),
            Logs = [new ScraperLogEntry(LogLevel.Information, "found it", null)],
        };

        var copy = RoundTrip(result);

        Assert.AreEqual(ScrapeOutcome.Succeeded, copy.Outcome);
        Assert.AreEqual("content", copy.Content);
        CollectionAssert.AreEqual(result.Screenshot, copy.Screenshot);
        Assert.AreEqual(result.Duration, copy.Duration);
        Assert.AreEqual(result.Logs[0], Assert.ContainsSingle(copy.Logs));
    }

    [TestMethod]
    public void KnownFailure_RoundTrips_WithItsRequestedActions()
    {
        var result = ScrapeResult.KnownFailure("Blocked", [RequestedAction.ChangeVpnLocation, RequestedAction.Retry]);

        var copy = RoundTrip(result);

        Assert.AreEqual(ScrapeOutcome.KnownFailure, copy.Outcome);
        Assert.AreEqual("Blocked", copy.Message);
        CollectionAssert.AreEqual(new[] { RequestedAction.ChangeVpnLocation, RequestedAction.Retry }, copy.RequestedActions.ToArray());
    }

    [TestMethod]
    public void UnexpectedFailure_RoundTrips_WithItsExceptionDiagnosticsAndPageHtml()
    {
        Exception thrown;
        try
        {
            throw new InvalidOperationException("selector not found");
        }
        catch (InvalidOperationException ex)
        {
            thrown = ex;
        }
        var result = ScrapeResult.Unexpected(thrown) with
        {
            Diagnostics = [new ScriptDiagnostic("Script.cs", 3, 5, "CS0103", "The name 'x' does not exist")],
            PageHtml = "<html>page</html>",
        };

        var copy = RoundTrip(result);

        Assert.AreEqual(ScrapeOutcome.UnexpectedFailure, copy.Outcome);
        Assert.AreEqual("selector not found", copy.Message);
        Assert.AreEqual("System.InvalidOperationException", copy.ExceptionType);
        Assert.AreEqual(thrown.ToString(), copy.ExceptionDetail);
        Assert.AreEqual(result.Diagnostics[0], Assert.ContainsSingle(copy.Diagnostics));
        Assert.AreEqual("<html>page</html>", copy.PageHtml);
    }
}
