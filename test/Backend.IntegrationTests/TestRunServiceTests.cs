namespace SiteChecker.Backend.IntegrationTests;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SiteChecker.Backend.Models;
using SiteChecker.Backend.Services.SignalR;
using SiteChecker.Backend.Services.TestRuns;
using SiteChecker.Database.Model;
using SiteChecker.Scraper;
using ScriptAction = SiteChecker.Scripting.RequestedAction;

[TestClass]
public sealed class TestRunServiceTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    private readonly IHubContext<DataHub> _hub = Substitute.For<IHubContext<DataHub>>();
    private readonly ISingleClientProxy _requester = Substitute.For<ISingleClientProxy>();

    [TestInitialize]
    public void Initialize() => _hub.Clients.Client("connection-1").Returns(_requester);

    private TestRunService CreateService(RunnerHarness harness)
        => new(harness.Runner, _hub, Substitute.For<IHostApplicationLifetime>(), NullLogger<TestRunService>.Instance);

    /// <summary>
    /// The one Test Run result sent to the requesting connection.
    /// </summary>
    private TestRunResult SentResult()
    {
        var call = Assert.ContainsSingle(_requester.ReceivedCalls());
        var arguments = call.GetArguments();
        Assert.AreEqual(nameof(ISingleClientProxy.SendCoreAsync), call.GetMethodInfo().Name);
        Assert.AreEqual(SignalRConstants.OnTestRunCompletedKey, arguments[0]);
        var payload = (object?[])arguments[1]!;
        return Assert.IsInstanceOfType<TestRunResult>(Assert.ContainsSingle(payload));
    }

    private static TestRunRequest Request() => new()
    {
        TestRunId = "run-1",
        ConnectionId = "connection-1",
        Url = new Uri("https://example.com/draft"),
        AlwaysTakeScreenshot = true,
        TimeoutSeconds = 30,
        Scraper = new ScraperRequest { Script = new ScriptUpload { FileName = "Draft.cs", Source = "// draft" } },
    };

    [TestMethod]
    public async Task SendsTheResultToTheRequestingConnectionOnly_AndRecordsNothing()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);
        harness.Scraper.OnScrape = _ => Task.FromResult(
            ScrapeResult.KnownFailure("Blocked", [ScriptAction.Retry]) with { Screenshot = [1, 2, 3], Duration = TimeSpan.FromSeconds(2) });

        await CreateService(harness).RunAsync(Request(), Ct);

        var result = SentResult();
        Assert.AreEqual("run-1", result.TestRunId);
        Assert.AreEqual(ScrapeOutcome.KnownFailure, result.Outcome);
        Assert.AreEqual("Blocked", result.Message);
        CollectionAssert.AreEqual(new[] { RequestedAction.Retry }, result.RequestedActions);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, result.Screenshot);
        Assert.AreEqual(2000, result.DurationMilliseconds);
        _ = _hub.Clients.DidNotReceive().All;
        _hub.Clients.DidNotReceive().AllExcept(Arg.Any<IReadOnlyList<string>>());

        var scrape = Assert.ContainsSingle(harness.Scraper.Requests);
        Assert.IsTrue(scrape.IsTestRun);
        Assert.AreEqual(new Uri("https://example.com/draft"), scrape.Site.Url);
        Assert.AreEqual(TimeSpan.FromSeconds(30), scrape.Timeout);
        Assert.IsTrue(scrape.AlwaysTakeScreenshot);
        Assert.AreEqual("// draft", Assert.IsInstanceOfType<ScriptSpec>(scrape.Scraper).Source);
        Assert.IsEmpty(await harness.GetChecksAsync(site.Id, Ct));
        Assert.IsFalse(await harness.Runner.RunNextAsync(Ct));
        Assert.IsEmpty(harness.Notifications.Sent);
    }

    [TestMethod]
    public async Task ARunThatThrows_StillSendsAnUnexpectedFailure()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        harness.Scraper.OnScrape = _ => throw new InvalidOperationException("browser exploded");

        await CreateService(harness).RunAsync(Request(), Ct);

        var result = SentResult();
        Assert.AreEqual(ScrapeOutcome.UnexpectedFailure, result.Outcome);
        Assert.AreEqual("browser exploded", result.Message);
    }
}
