namespace SiteChecker.Backend.IntegrationTests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using NSubstitute;
using SiteChecker.Backend.Services.Scraping;
using SiteChecker.Scraper;
using SiteChecker.Scraper.Browsers;
using SiteChecker.Scraper.Scripts;
using SiteChecker.Scripting;
using BrowserType = SiteChecker.Scraper.BrowserType;

/// <summary>
/// <see cref="RemoteScraperService"/> against the real Scrape Worker, which compiles and runs
/// scripts for real in a substituted browser.
/// </summary>
[TestClass]
public sealed class RemoteScraperServiceTests
{
    private const string HelloScript = """
        public sealed class Hello : IScript
        {
            public Task<ScriptOutcome> RunAsync(ScriptContext ctx)
            {
                ctx.Logger.LogInformation("Hello from {Site}", ctx.Site.Name);
                return Task.FromResult<ScriptOutcome>("hello");
            }
        }
        """;

    private const string BlockedScript = """
        public sealed class Blocked : IScript
        {
            public Task<ScriptOutcome> RunAsync(ScriptContext ctx)
                => Task.FromResult(ScriptOutcome.KnownFailure("Blocked", RequestedAction.ChangeVpnLocation));
        }
        """;

    private const string ThrowingScript = """
        public sealed class Throwing : IScript
        {
            public Task<ScriptOutcome> RunAsync(ScriptContext ctx)
                => throw new InvalidOperationException("selector not found");
        }
        """;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    /// <summary>
    /// The worker with a substituted browser.
    /// </summary>
    private sealed class WorkerFactory : WebApplicationFactory<SiteChecker.ScrapeWorker.Program>, IHttpClientFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var page = Substitute.For<IPage>();
            page.GotoAsync(Arg.Any<string>(), Arg.Any<PageGotoOptions?>()).Returns((IResponse?)null);
            page.ScreenshotAsync(Arg.Any<PageScreenshotOptions?>()).Returns([9, 9]);
            page.ContentAsync().Returns("<html>page</html>");
            var session = Substitute.For<IBrowserSession>();
            session.Page.Returns(page);
            var browsers = Substitute.For<IBrowserProvider>();
            browsers.OpenAsync(Arg.Any<BrowserType>(), Arg.Any<CancellationToken>()).Returns(session);

            builder.ConfigureTestServices(services => services.AddSingleton(browsers));
        }

        HttpClient IHttpClientFactory.CreateClient(string name) => CreateClient();
    }

    private static RemoteScraperService CreateService(IHttpClientFactory worker)
    {
        var config = new ConfigurationBuilder().Build();
        return new RemoteScraperService(
            worker,
            new BrowserSelector(config),
            new ScrapeTimeouts(config, NullLogger<ScrapeTimeouts>.Instance),
            RemoteScraperOptions.Default,
            TimeProvider.System,
            NullLoggerFactory.Instance);
    }

    private static ScrapeRequest Request(string source, int? siteCheckId = 7) => new()
    {
        SiteCheckId = siteCheckId,
        Site = new ScrapeSite(siteCheckId is null ? null : 3, "Site", new Uri("https://example.com/"), UseVpn: false),
        Scraper = new ScriptSpec("Script.cs", source, ScriptSource.Hash(source)),
    };

    [TestMethod]
    public async Task Succeeded_ComesBackWithItsContentAndLogs()
    {
        await using var worker = new WorkerFactory();

        var result = await CreateService(worker).ScrapeAsync(Request(HelloScript), Ct);

        Assert.AreEqual(ScrapeOutcome.Succeeded, result.Outcome, result.Message);
        Assert.AreEqual("hello", result.Content);
        Assert.AreEqual("Hello from Site", Assert.ContainsSingle(result.Logs).Message);
        Assert.IsGreaterThan(TimeSpan.Zero, result.Duration);
    }

    [TestMethod]
    public async Task KnownFailure_ComesBackWithItsRequestedActionsAndScreenshot()
    {
        await using var worker = new WorkerFactory();

        var result = await CreateService(worker).ScrapeAsync(Request(BlockedScript), Ct);

        Assert.AreEqual(ScrapeOutcome.KnownFailure, result.Outcome);
        Assert.AreEqual(RequestedAction.ChangeVpnLocation, Assert.ContainsSingle(result.RequestedActions));
        CollectionAssert.AreEqual(new byte[] { 9, 9 }, result.Screenshot);
    }

    [TestMethod]
    public async Task UnexpectedFailure_ComesBackWithTheExceptionAndPageHtml()
    {
        await using var worker = new WorkerFactory();

        var result = await CreateService(worker).ScrapeAsync(Request(ThrowingScript), Ct);

        Assert.AreEqual(ScrapeOutcome.UnexpectedFailure, result.Outcome);
        Assert.AreEqual("selector not found", result.Message);
        Assert.AreEqual(typeof(InvalidOperationException).FullName, result.ExceptionType);
        Assert.Contains("Throwing.RunAsync", result.ExceptionDetail!);
        Assert.AreEqual("<html>page</html>", result.PageHtml);
    }

    [TestMethod]
    public async Task TestRun_ComesBackWithoutPageHtml()
    {
        await using var worker = new WorkerFactory();

        var result = await CreateService(worker).ScrapeAsync(Request(ThrowingScript, siteCheckId: null), Ct);

        Assert.AreEqual(ScrapeOutcome.UnexpectedFailure, result.Outcome);
        Assert.IsNull(result.PageHtml);
    }
}
