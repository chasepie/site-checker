namespace SiteChecker.ScrapeWorker.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SiteChecker.Scraper;
using SiteChecker.Scraper.Scripts;
using SiteChecker.Scripting;
using BrowserType = SiteChecker.Scraper.BrowserType;

/// <summary>
/// The worker's API, end to end through the real pipeline and compiler.
/// </summary>
[TestClass]
public sealed class ScrapeWorkerTests
{
    private const string HelloScript = """
        public sealed class Hello : IScript
        {
            public Task<ScriptOutcome> RunAsync(ScriptContext ctx)
            {
                ctx.Logger.LogInformation("Saying hello to {Site}", ctx.Site.Name);
                return Task.FromResult<ScriptOutcome>("hello");
            }
        }
        """;

    private const string BlockedScript = """
        public sealed class Blocked : IScript
        {
            public Task<ScriptOutcome> RunAsync(ScriptContext ctx)
                => Task.FromResult(ScriptOutcome.KnownFailure("Blocked", RequestedAction.ChangeVpnLocation, RequestedAction.Retry));
        }
        """;

    private const string BrokenScript = """
        public sealed class Broken : IScript
        {
        }
        """;

    private const string HangingScript = """
        public sealed class Hanging : IScript
        {
            public async Task<ScriptOutcome> RunAsync(ScriptContext ctx)
            {
                await Task.Delay(Timeout.Infinite);
                return "unreachable";
            }
        }
        """;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    private static ScrapeRequest Request(
        string source,
        int? siteCheckId = 7,
        TimeSpan? timeout = null,
        BrowserType browserType = BrowserType.Browserless) => new()
    {
        SiteCheckId = siteCheckId,
        Site = new ScrapeSite(siteCheckId is null ? null : 3, "Site", new Uri("https://example.com/"), UseVpn: browserType == BrowserType.BrowserlessVpn),
        Scraper = new ScriptSpec("Script.cs", source, ScriptSource.Hash(source)),
        BrowserType = browserType,
        Timeout = timeout,
    };

    private async Task<ScrapeResult> ScrapeAsync(HttpClient client, ScrapeRequest request)
    {
        var response = await client.PostAsJsonAsync(ScrapeWorkerEndpoints.ScrapePath, request, ScrapeJson.Options, Ct);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ScrapeResult>(ScrapeJson.Options, Ct))!;
    }

    [TestMethod]
    public async Task Scrape_RunsTheScript_InTheRequestedBrowser()
    {
        await using var factory = new ScrapeWorkerFactory();
        using var client = factory.CreateClient();

        var result = await ScrapeAsync(client, Request(HelloScript, browserType: BrowserType.BrowserlessVpn));

        Assert.AreEqual(ScrapeOutcome.Succeeded, result.Outcome);
        Assert.AreEqual("hello", result.Content);
        Assert.AreEqual(BrowserType.BrowserlessVpn, Assert.ContainsSingle(factory.OpenedBrowsers));
        var log = Assert.ContainsSingle(result.Logs);
        Assert.AreEqual(LogLevel.Information, log.Level);
        Assert.AreEqual("Saying hello to Site", log.Message);
    }

    [TestMethod]
    public async Task Scrape_ReturnsAKnownFailure_WithItsRequestedActionsAndScreenshot()
    {
        await using var factory = new ScrapeWorkerFactory();
        using var client = factory.CreateClient();

        var result = await ScrapeAsync(client, Request(BlockedScript));

        Assert.AreEqual(ScrapeOutcome.KnownFailure, result.Outcome);
        Assert.AreEqual("Blocked", result.Message);
        CollectionAssert.AreEqual(new[] { RequestedAction.ChangeVpnLocation, RequestedAction.Retry }, result.RequestedActions.ToArray());
        CollectionAssert.AreEqual(ScrapeWorkerFactory.ScreenshotBytes, result.Screenshot);
    }

    [TestMethod]
    public async Task Scrape_ReturnsCompileErrors_AsAnUnexpectedFailure()
    {
        await using var factory = new ScrapeWorkerFactory();
        using var client = factory.CreateClient();

        var result = await ScrapeAsync(client, Request(BrokenScript));

        Assert.AreEqual(ScrapeOutcome.UnexpectedFailure, result.Outcome);
        Assert.AreEqual("CS0535", result.Diagnostics[0].Id);
        Assert.AreEqual("<html>page</html>", result.PageHtml);
    }

    [TestMethod]
    public async Task Scrape_ThatTimesOut_IsAnUnexpectedFailure_AndItsRunIsTracked()
    {
        await using var factory = new ScrapeWorkerFactory();
        using var client = factory.CreateClient();

        var result = await ScrapeAsync(client, Request(HangingScript, timeout: TimeSpan.FromMilliseconds(300)));

        Assert.AreEqual(ScrapeOutcome.UnexpectedFailure, result.Outcome);
        Assert.AreEqual("Timed out after 0.3 s.", result.Message);
        Assert.AreEqual(1, factory.AbandonedRuns.Tracked);
    }

    [TestMethod]
    public async Task Scrape_IsRefused_AndHealthIsUnhealthy_WhileARunIsAbandoned()
    {
        await using var factory = new ScrapeWorkerFactory();
        using var client = factory.CreateClient();
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync(ScrapeWorkerEndpoints.HealthPath, Ct)).StatusCode);

        factory.AbandonedRuns.HasAbandonedRuns = true;
        var refused = await client.PostAsJsonAsync(ScrapeWorkerEndpoints.ScrapePath, Request(HelloScript), ScrapeJson.Options, Ct);
        var health = await client.GetAsync(ScrapeWorkerEndpoints.HealthPath, Ct);

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, health.StatusCode);
        Assert.IsEmpty(factory.OpenedBrowsers);
    }

    [TestMethod]
    public async Task EvictScript_IsAccepted()
    {
        await using var factory = new ScrapeWorkerFactory();
        using var client = factory.CreateClient();
        await ScrapeAsync(client, Request(HelloScript));

        var response = await client.DeleteAsync($"{ScrapeWorkerEndpoints.ScriptsPath}/3", Ct);

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
    }

    [TestMethod]
    public async Task Scrape_WithoutABody_IsABadRequest()
    {
        await using var factory = new ScrapeWorkerFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            ScrapeWorkerEndpoints.ScrapePath, new StringContent("null", System.Text.Encoding.UTF8, "application/json"), Ct);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
