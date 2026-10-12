namespace SiteChecker.Scraper.IntegrationTests;

using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using NSubstitute;
using SiteChecker.Scraper;
using SiteChecker.Scraper.Executors;
using SiteChecker.Scraper.Scripts;
using SiteChecker.Scripting;
using BrowserType = SiteChecker.Scraper.BrowserType;

[TestClass]
public sealed class ScriptExecutorTests
{
    private const string RoutingScript = """
        public sealed class Routing : IScript
        {
            public Task<ScriptOutcome> RunAsync(ScriptContext ctx)
                => Task.FromResult<ScriptOutcome>(ctx.Site.UsesVpn ? "through the VPN" : "direct");
        }
        """;

    private const string BlockedScript = """
        public sealed class Blocked : IScript
        {
            public Task<ScriptOutcome> RunAsync(ScriptContext ctx)
                => Task.FromResult(ScriptOutcome.KnownFailure("Blocked", RequestedAction.ChangeVpnLocation, RequestedAction.Retry));
        }
        """;

    private readonly CountingCompiler _compiler = new();

    private ScriptExecutor CreateExecutor(ScriptCache cache) => new(cache, _compiler, NullLoggerFactory.Instance);

    private const string LoggingScript = """
        public sealed class Chatty : IScript
        {
            public Task<ScriptOutcome> RunAsync(ScriptContext ctx)
            {
                ctx.Logger.LogInformation("Checking {Site}", ctx.Site.Name);
                ctx.Logger.LogWarning(new InvalidOperationException("odd page"), "Something looks off");
                throw new InvalidOperationException("gave up");
            }
        }
        """;

    private static ExecutorContext Context(
        string source,
        int? siteCheckId,
        string fileName = "Script.cs",
        BrowserType browserType = BrowserType.Browserless,
        ScraperLog? log = null) => new(
        Substitute.For<IPage>(),
        NavigationResult.FromResponse(null),
        new ScrapeRequest
        {
            SiteCheckId = siteCheckId,
            Site = new ScrapeSite(1, "Site", new Uri("https://example.com/"), UseVpn: browserType == BrowserType.BrowserlessVpn),
            Scraper = new ScriptSpec(fileName, source, ScriptSource.Hash(source)),
            BrowserType = browserType,
        },
        log ?? new ScraperLog(),
        CancellationToken.None);

    [TestMethod]
    public async Task SiteChecks_ReuseTheSitesCachedScript()
    {
        using var cache = new ScriptCache(_compiler);
        var executor = CreateExecutor(cache);

        await executor.ExecuteAsync(Context(RoutingScript, siteCheckId: 1));
        await executor.ExecuteAsync(Context(RoutingScript, siteCheckId: 2));

        Assert.AreEqual(1, _compiler.Compiles);
    }

    [TestMethod]
    public async Task TestRun_CompilesItsOwnCopy_AndUnloadsItWhenTheRunEnds()
    {
        using var cache = new ScriptCache(_compiler);
        var executor = CreateExecutor(cache);
        var fileName = $"TestRun-{Guid.NewGuid():N}.cs";

        var result = await executor.ExecuteAsync(Context(RoutingScript, siteCheckId: null, fileName));

        Assert.AreEqual(ScrapeOutcome.Succeeded, result.Outcome);
        Assert.IsTrue(Unloaded(fileName), "The Test Run's load context is still alive.");

        // Not cached: the next Site Check compiles again.
        await executor.ExecuteAsync(Context(RoutingScript, siteCheckId: 1));
        Assert.AreEqual(2, _compiler.Compiles);
    }

    [TestMethod]
    [DataRow(BrowserType.Browserless, "direct")]
    [DataRow(BrowserType.BrowserlessVpn, "through the VPN")]
    public async Task Succeeded_ReturnsTheContent_AndTellsTheScriptWhetherItRunsThroughTheVpn(BrowserType browserType, string expected)
    {
        using var cache = new ScriptCache(_compiler);

        var result = await CreateExecutor(cache).ExecuteAsync(Context(RoutingScript, siteCheckId: 1, browserType: browserType));

        Assert.AreEqual(ScrapeOutcome.Succeeded, result.Outcome);
        Assert.AreEqual(expected, result.Content);
    }

    [TestMethod]
    public async Task KnownFailure_KeepsItsMessageAndRequestedActions()
    {
        using var cache = new ScriptCache(_compiler);

        var result = await CreateExecutor(cache).ExecuteAsync(Context(BlockedScript, siteCheckId: 1));

        Assert.AreEqual(ScrapeOutcome.KnownFailure, result.Outcome);
        Assert.AreEqual("Blocked", result.Message);
        CollectionAssert.AreEqual(
            new[] { RequestedAction.ChangeVpnLocation, RequestedAction.Retry },
            result.RequestedActions.ToArray());
    }

    [TestMethod]
    public async Task ScriptThatDoesNotCompile_IsAnUnexpectedFailure_WithItsDiagnostics()
    {
        using var cache = new ScriptCache(_compiler);

        var result = await CreateExecutor(cache).ExecuteAsync(Context("public sealed class Broken : IScript { }", siteCheckId: 1, "Broken.cs"));

        Assert.AreEqual(ScrapeOutcome.UnexpectedFailure, result.Outcome);
        Assert.StartsWith("The script doesn't compile: Broken.cs(1,", result.Message!);
        Assert.IsNotEmpty(result.Diagnostics);
    }

    private static bool Unloaded(string fileName)
    {
        var name = $"SiteChecker.Script: {fileName}";
        for (var i = 0; i < 20 && AssemblyLoadContext.All.Any(c => c.Name == name); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        return !AssemblyLoadContext.All.Any(c => c.Name == name);
    }

    [TestMethod]
    public async Task ScriptLogs_AreRecorded_EvenWhenTheScriptThrows()
    {
        using var cache = new ScriptCache(_compiler);
        var log = new ScraperLog();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateExecutor(cache).ExecuteAsync(Context(LoggingScript, siteCheckId: 1, log: log)));

        Assert.HasCount(2, log.Entries);
        Assert.AreEqual("Checking Site", log.Entries[0].Message);
        Assert.AreEqual(LogLevel.Warning, log.Entries[1].Level);
        Assert.Contains("odd page", log.Entries[1].Exception!);
    }
}
