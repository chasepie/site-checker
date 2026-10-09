namespace SiteChecker.Scraper.UnitTests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SiteChecker.Scraper;
using SiteChecker.Scraper.Browsers;
using SiteChecker.Scraper.Executors;
using SiteChecker.Scripting;
using BrowserType = SiteChecker.Scraper.BrowserType;

/// <summary>
/// The shared pipeline, with a substituted browser and page and fake executors.
/// </summary>
[TestClass]
public sealed class ScraperServiceTests : IDisposable
{
    private static readonly Uri SiteUrl = new("https://example.com/site");
    private static readonly byte[] ScreenshotBytes = [1, 2, 3];

    /// <summary>
    /// Short, because timeout tests wait for it in real time.
    /// </summary>
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(200);

    private readonly IPage _page = Substitute.For<IPage>();
    private readonly IBrowserSession _session = Substitute.For<IBrowserSession>();
    private readonly IBrowserProvider _browsers = Substitute.For<IBrowserProvider>();
    private readonly IResponse _response = Substitute.For<IResponse>();
    private readonly ManualResetEventSlim _releaseBlockedExecutors = new();
    private string _logsDirectory = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [TestInitialize]
    public void Initialize()
    {
        _logsDirectory = Path.Combine(Path.GetTempPath(), $"site-checker-tests-{Guid.NewGuid():N}");
        _response.Ok.Returns(true);
        _response.Status.Returns(200);
        _page.GotoAsync(Arg.Any<string>(), Arg.Any<PageGotoOptions?>()).Returns(_response);
        _page.ScreenshotAsync(Arg.Any<PageScreenshotOptions?>()).Returns(ScreenshotBytes);
        _page.ContentAsync().Returns("<html>page</html>");
        _session.Page.Returns(_page);
        _browsers.OpenAsync(Arg.Any<BrowserType>(), Arg.Any<CancellationToken>()).Returns(_session);
    }

    public void Dispose()
    {
        _releaseBlockedExecutors.Set();
        _releaseBlockedExecutors.Dispose();
        if (Directory.Exists(_logsDirectory))
        {
            Directory.Delete(_logsDirectory, recursive: true);
        }
    }

    /// <summary>
    /// The kind of Scraper <see cref="FakeExecutor"/> runs.
    /// </summary>
    private sealed record FakeSpec : ScraperSpec;

    private sealed class FakeExecutor(Func<ExecutorContext, Task<ScrapeResult>> execute) : IScrapeExecutor
    {
        public Type SpecType => typeof(FakeSpec);

        public Task<ScrapeResult> ExecuteAsync(ExecutorContext context) => execute(context);
    }

    private ScraperService CreateService(Func<ExecutorContext, Task<ScrapeResult>> execute) => new(
        _browsers,
        [new FakeExecutor(execute)],
        new ScrapeTimeouts(new ConfigurationBuilder().Build(), NullLogger<ScrapeTimeouts>.Instance),
        new FailureArtifacts(NullLogger<FailureArtifacts>.Instance, _logsDirectory),
        TimeProvider.System,
        NullLogger<ScraperService>.Instance);

    private static ScrapeRequest Request(int? siteCheckId = 7, bool alwaysTakeScreenshot = false, TimeSpan? timeout = null) => new()
    {
        SiteCheckId = siteCheckId,
        Site = new ScrapeSite(3, "Site", SiteUrl, UseVpn: false),
        Scraper = new FakeSpec(),
        AlwaysTakeScreenshot = alwaysTakeScreenshot,
        Timeout = timeout,
    };

    // ---- Navigation ----

    [TestMethod]
    public async Task NavigatesToTheSiteUrl_BeforeTheExecutorRuns()
    {
        var navigatedBeforeExecutor = false;
        IResponse? responseSeen = null;
        var service = CreateService(ctx =>
        {
            navigatedBeforeExecutor = _page.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IPage.GotoAsync));
            responseSeen = ctx.Navigation.Response;
            return Task.FromResult(ScrapeResult.Succeeded("content"));
        });

        var result = await service.ScrapeAsync(Request(), Ct);

        Assert.AreEqual(ScrapeOutcome.Succeeded, result.Outcome);
        Assert.AreEqual("content", result.Content);
        await _page.Received(1).GotoAsync(SiteUrl.AbsoluteUri, Arg.Any<PageGotoOptions?>());
        Assert.IsTrue(navigatedBeforeExecutor);
        Assert.AreSame(_response, responseSeen);
    }

    [TestMethod]
    public async Task NavigationError_ReachesTheExecutor_InsteadOfFailingTheScrape()
    {
        var error = new InvalidOperationException("net::ERR_CONNECTION_RESET");
        _page.GotoAsync(Arg.Any<string>(), Arg.Any<PageGotoOptions?>()).ThrowsAsync(error);
        Exception? errorSeen = null;
        var service = CreateService(ctx =>
        {
            errorSeen = ctx.Navigation.Error;
            return Task.FromResult(ScrapeResult.KnownFailure("Blocked", [RequestedAction.ChangeVpnLocation, RequestedAction.Retry]));
        });

        var result = await service.ScrapeAsync(Request(), Ct);

        Assert.AreSame(error, errorSeen);
        Assert.AreEqual(ScrapeOutcome.KnownFailure, result.Outcome);
        Assert.AreEqual("Blocked", result.Message);
        CollectionAssert.AreEqual(
            new[] { RequestedAction.ChangeVpnLocation, RequestedAction.Retry },
            result.RequestedActions.ToArray());
    }

    // ---- Failures ----

    [TestMethod]
    public async Task ExecutorThatThrows_IsAnUnexpectedFailure_AndWritesTheDumps()
    {
        var service = CreateService(_ => throw new InvalidOperationException("selector not found"));

        var result = await service.ScrapeAsync(Request(siteCheckId: 7), Ct);

        Assert.AreEqual(ScrapeOutcome.UnexpectedFailure, result.Outcome);
        Assert.AreEqual("selector not found", result.Message);
        Assert.IsInstanceOfType<InvalidOperationException>(result.Exception);
        Assert.AreEqual("<html>page</html>", await File.ReadAllTextAsync(Path.Combine(_logsDirectory, "7_3.html"), Ct));
        Assert.Contains("selector not found", await File.ReadAllTextAsync(Path.Combine(_logsDirectory, "7_3.log"), Ct));
    }

    [TestMethod]
    public async Task TestRun_WritesNoDumps()
    {
        var service = CreateService(_ => throw new InvalidOperationException("selector not found"));

        var result = await service.ScrapeAsync(Request(siteCheckId: null), Ct);

        Assert.AreEqual(ScrapeOutcome.UnexpectedFailure, result.Outcome);
        Assert.IsFalse(Directory.Exists(_logsDirectory));
    }

    [TestMethod]
    public async Task BrowserThatWontOpen_IsAnUnexpectedFailure()
    {
        _browsers.OpenAsync(Arg.Any<BrowserType>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("connection refused"));
        var service = CreateService(_ => Task.FromResult(ScrapeResult.Succeeded("unreachable")));

        var result = await service.ScrapeAsync(Request(), Ct);

        Assert.AreEqual(ScrapeOutcome.UnexpectedFailure, result.Outcome);
        Assert.AreEqual("Couldn't open the browser: connection refused", result.Message);
    }

    [TestMethod]
    public async Task ScraperWithNoExecutor_IsAnUnexpectedFailure()
    {
        var service = CreateService(_ => Task.FromResult(ScrapeResult.Succeeded("unreachable")));
        var request = Request() with { Scraper = new ScriptSpec("Script.cs", "", "") };

        var result = await service.ScrapeAsync(request, Ct);

        Assert.AreEqual(ScrapeOutcome.UnexpectedFailure, result.Outcome);
        await _browsers.DidNotReceive().OpenAsync(Arg.Any<BrowserType>(), Arg.Any<CancellationToken>());
    }

    // ---- Timeout ----

    [TestMethod]
    public async Task Timeout_EndsARunThatIgnoresItsToken()
    {
        var neverCompletes = new TaskCompletionSource<ScrapeResult>();
        var service = CreateService(_ => neverCompletes.Task);

        var result = await service.ScrapeAsync(Request(timeout: ShortTimeout), Ct);

        AssertTimedOut(result);
    }

    [TestMethod]
    public async Task Timeout_EndsARunThatBlocksSynchronously()
    {
        var service = CreateService(_ =>
        {
            _releaseBlockedExecutors.Wait(Ct);
            return Task.FromResult(ScrapeResult.Succeeded("too late"));
        });

        var result = await service.ScrapeAsync(Request(timeout: ShortTimeout), Ct);

        AssertTimedOut(result);
    }

    [TestMethod]
    public async Task Timeout_CancelsTheExecutorsToken()
    {
        var tokenCancelled = new TaskCompletionSource();
        var service = CreateService(async ctx =>
        {
            ctx.CancellationToken.Register(tokenCancelled.SetResult);
            await Task.Delay(Timeout.Infinite, CancellationToken.None);
            return ScrapeResult.Succeeded("unreachable");
        });

        var result = await service.ScrapeAsync(Request(timeout: ShortTimeout), Ct);

        AssertTimedOut(result);
        await tokenCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
    }

    [TestMethod]
    public async Task Timeout_IsReportedAsATimeout_WhenTheRunStopsOnItsToken()
    {
        // A cooperative script throws as soon as its token is cancelled, which can end the run
        // before the pipeline's own deadline fires.
        var service = CreateService(async ctx =>
        {
            await Task.Delay(Timeout.Infinite, ctx.CancellationToken);
            return ScrapeResult.Succeeded("unreachable");
        });

        var result = await service.ScrapeAsync(Request(timeout: ShortTimeout), Ct);

        AssertTimedOut(result);
    }

    private void AssertTimedOut(ScrapeResult result)
    {
        Assert.AreEqual(ScrapeOutcome.UnexpectedFailure, result.Outcome);
        Assert.AreEqual("Timed out after 0.2 s.", result.Message);
        Assert.IsGreaterThanOrEqualTo(ShortTimeout, result.Duration);
        CollectionAssert.AreEqual(ScreenshotBytes, result.Screenshot);
        _session.Received().CloseAsync();
    }

    // ---- Screenshots ----

    [TestMethod]
    public async Task Screenshot_IsNotTaken_WhenTheScrapeSucceeds()
    {
        var service = CreateService(_ => Task.FromResult(ScrapeResult.Succeeded("content")));

        var result = await service.ScrapeAsync(Request(), Ct);

        Assert.IsNull(result.Screenshot);
        await _page.DidNotReceive().ScreenshotAsync(Arg.Any<PageScreenshotOptions?>());
    }

    [TestMethod]
    public async Task Screenshot_IsTaken_WhenTheSiteAlwaysTakesOne()
    {
        var service = CreateService(_ => Task.FromResult(ScrapeResult.Succeeded("content")));

        var result = await service.ScrapeAsync(Request(alwaysTakeScreenshot: true), Ct);

        CollectionAssert.AreEqual(ScreenshotBytes, result.Screenshot);
    }

    [TestMethod]
    public async Task Screenshot_IsTaken_OnAKnownFailure()
    {
        var service = CreateService(_ => Task.FromResult(ScrapeResult.KnownFailure("Access denied")));

        var result = await service.ScrapeAsync(Request(), Ct);

        CollectionAssert.AreEqual(ScreenshotBytes, result.Screenshot);
    }

    [TestMethod]
    public async Task ScreenshotFailure_DoesNotChangeTheOutcome()
    {
        _page.ScreenshotAsync(Arg.Any<PageScreenshotOptions?>()).ThrowsAsync(new InvalidOperationException("page crashed"));
        var service = CreateService(_ => Task.FromResult(ScrapeResult.Succeeded("content")));

        var result = await service.ScrapeAsync(Request(alwaysTakeScreenshot: true), Ct);

        Assert.AreEqual(ScrapeOutcome.Succeeded, result.Outcome);
        Assert.IsNull(result.Screenshot);
    }

    [TestMethod]
    public async Task Session_IsClosed_AfterEveryScrape()
    {
        var service = CreateService(_ => Task.FromResult(ScrapeResult.Succeeded("content")));

        await service.ScrapeAsync(Request(), Ct);

        await _session.Received(1).DisposeAsync();
    }
}
