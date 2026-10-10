namespace SiteChecker.Backend.IntegrationTests;

using SiteChecker.Database.Model;
using SiteChecker.Scraper;
using ScriptAction = SiteChecker.Scripting.RequestedAction;

/// <summary>
/// VPN Location rotation, Change VPN Location, and the scrape lock, through the runner with the
/// real <c>PiaService</c> over fake containers.
/// </summary>
[TestClass]
public sealed class VpnLocationTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    private static ScrapeResult Blocked() => ScrapeResult.KnownFailure("Blocked", [ScriptAction.ChangeVpnLocation, ScriptAction.Retry]);

    private static async Task<(RunnerHarness Harness, Site Site)> CreateVpnSiteAsync(CancellationToken ct)
    {
        var harness = await RunnerHarness.CreateAsync(ct);
        harness.Scraper.BrowserType = BrowserType.BrowserlessVpn;
        var site = await harness.AddSiteAsync(null, ct);
        return (harness, site);
    }

    /// <summary>
    /// Every location the containers were restarted on, in order.
    /// </summary>
    private static string Restarts(RunnerHarness harness) => string.Join(", ", harness.Vpn.Restarts);

    private static void ScrapeInOrder(RunnerHarness harness, params ScrapeResult[] results)
    {
        var next = new Queue<ScrapeResult>(results);
        harness.Scraper.OnScrape = _ => Task.FromResult(next.Dequeue());
    }

    [TestMethod]
    public async Task ChangeVpnLocation_ExcludesTheFailedLocation_AndTheRetryRunsElsewhere()
    {
        var (harness, site) = await CreateVpnSiteAsync(Ct);
        await using var _ = harness;
        ScrapeInOrder(harness, Blocked(), ScrapeResult.Succeeded("content"));

        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));

        var failed = (await harness.GetChecksAsync(site.Id, Ct))[0];
        Assert.AreEqual(CheckStatus.Failed, failed.Status);
        Assert.AreEqual("us_a", failed.VpnLocationId);
        CollectionAssert.AreEqual(new[] { RequestedAction.ChangeVpnLocation, RequestedAction.Retry }, failed.RequestedActions);
        Assert.IsEmpty(harness.Vpn.Restarts);

        // The retry rotates first, instead of waiting for the interval.
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));
        Assert.AreEqual("us_b", Restarts(harness));
        Assert.AreEqual("us_b", (await harness.GetChecksAsync(site.Id, Ct))[1].VpnLocationId);

        // A stays excluded: going around the list skips it.
        harness.Vpn.CurrentLocationId = "us_f";
        Assert.AreEqual("us_b", (await harness.Runner.ChangeVpnLocationAsync(excludeCurrent: false, Ct)).Id);
    }

    [TestMethod]
    public async Task ManualChange_BeforeTheNextVpnCheck_SatisfiesTheRequest_WithoutASecondRotation()
    {
        var (harness, site) = await CreateVpnSiteAsync(Ct);
        await using var _ = harness;
        ScrapeInOrder(harness, Blocked(), ScrapeResult.Succeeded("content"));
        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));

        // A manual change (without exclusion) moves from A to B before the retry runs.
        Assert.AreEqual("us_b", (await harness.Runner.ChangeVpnLocationAsync(excludeCurrent: false, Ct)).Id);
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));

        Assert.AreEqual("us_b", Restarts(harness));
        Assert.AreEqual("us_b", (await harness.GetChecksAsync(site.Id, Ct))[1].VpnLocationId);

        // A is excluded and B isn't: from F, the rotation skips A and lands on B.
        harness.Vpn.CurrentLocationId = "us_f";
        Assert.AreEqual("us_b", (await harness.Runner.ChangeVpnLocationAsync(excludeCurrent: false, Ct)).Id);
    }

    [TestMethod]
    public async Task ChangeVpnLocation_IsIgnored_ForASiteThatDoesNotUseTheVpn()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);
        ScrapeInOrder(harness, ScrapeResult.KnownFailure("Blocked", [ScriptAction.ChangeVpnLocation]), ScrapeResult.Succeeded("content"));
        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));

        var check = Assert.ContainsSingle(await harness.GetChecksAsync(site.Id, Ct));
        Assert.IsNull(check.VpnLocationId);
        Assert.IsEmpty(check.RequestedActions);

        // A later VPN-routed check doesn't rotate.
        harness.Scraper.BrowserType = BrowserType.BrowserlessVpn;
        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));
        Assert.IsEmpty(harness.Vpn.Restarts);
    }

    [TestMethod]
    public async Task Rotation_MovesToTheNextEligibleLocationAfterTheCurrentOne()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        harness.Vpn.CurrentLocationId = "us_c";

        var next = await harness.Runner.ChangeVpnLocationAsync(excludeCurrent: true, Ct);

        Assert.AreEqual("us_d", next.Id);
    }

    [TestMethod]
    public async Task Exclusions_Reset_OnceFewerThanFiveLocationsRemainEligible()
    {
        var (harness, site) = await CreateVpnSiteAsync(Ct);
        await using var _ = harness;
        // Fails on A, then its retry fails on B: A and B are excluded, leaving four eligible.
        ScrapeInOrder(harness, Blocked(), Blocked(), ScrapeResult.Succeeded("content"));
        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));
        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));
        Assert.AreEqual("us_b, us_c", Restarts(harness));

        // The reset made A eligible again.
        harness.Vpn.CurrentLocationId = "us_f";
        Assert.AreEqual("us_a", (await harness.Runner.ChangeVpnLocationAsync(excludeCurrent: false, Ct)).Id);
    }

    [TestMethod]
    public async Task VpnLocation_RotatesOnceTheIntervalHasElapsed()
    {
        var (harness, site) = await CreateVpnSiteAsync(Ct);
        await using var _ = harness;

        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));
        Assert.IsEmpty(harness.Vpn.Restarts);

        harness.Time.Advance(TimeSpan.FromMinutes(15));
        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));
        Assert.AreEqual("us_b", Restarts(harness));
    }

    // ---- Scrape lock ----

    [TestMethod]
    public async Task ManualVpnChange_WaitsForTheRunningCheck()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);
        var (scrapeStarted, finishScrape) = BlockFirstScrape(harness);
        await harness.Runner.RequestCheckAsync(site.Id, Ct);

        var run = harness.Runner.RunNextAsync(Ct);
        await scrapeStarted.Task.WaitAsync(Ct);
        var change = harness.Runner.ChangeVpnLocationAsync(excludeCurrent: false, Ct);
        await Task.Delay(100, Ct);

        Assert.IsFalse(change.IsCompleted);
        Assert.IsEmpty(harness.Vpn.Restarts);
        finishScrape.SetResult(ScrapeResult.Succeeded("content"));
        Assert.IsTrue(await run);
        Assert.AreEqual("us_b", (await change).Id);
    }

    [TestMethod]
    public async Task TestRun_WaitsForTheRunningCheck_AndRecordsAndCarriesOutNothing()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        harness.Scraper.BrowserType = BrowserType.BrowserlessVpn;
        var site = await harness.AddSiteAsync(null, Ct);
        var (scrapeStarted, finishScrape) = BlockFirstScrape(harness, then: Blocked());
        await harness.Runner.RequestCheckAsync(site.Id, Ct);

        var run = harness.Runner.RunNextAsync(Ct);
        await scrapeStarted.Task.WaitAsync(Ct);
        var testRun = harness.Runner.RunTestAsync(new ScrapeRequest
        {
            Site = new ScrapeSite(null, "Draft", new Uri("https://example.com/draft"), UseVpn: true),
            Scraper = new ScriptSpec("Draft.cs", "// draft", "hash"),
        }, Ct);
        await Task.Delay(100, Ct);

        Assert.IsFalse(testRun.IsCompleted);
        finishScrape.SetResult(ScrapeResult.Succeeded("content"));
        Assert.IsTrue(await run);
        var result = await testRun;

        Assert.AreEqual(ScrapeOutcome.KnownFailure, result.Outcome);
        Assert.IsNull(harness.Scraper.Requests[^1].SiteCheckId);
        Assert.AreEqual(BrowserType.BrowserlessVpn, harness.Scraper.Requests[^1].BrowserType);
        Assert.ContainsSingle(await harness.GetChecksAsync(site.Id, Ct));
        Assert.IsFalse(await harness.Runner.RunNextAsync(Ct), "The Test Run's Retry was carried out.");
        Assert.IsEmpty(harness.Notifications.Sent);

        // Nor its Change VPN Location: the next VPN-routed check doesn't rotate.
        harness.Scraper.OnScrape = _ => Task.FromResult(ScrapeResult.Succeeded("content"));
        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));
        Assert.IsEmpty(harness.Vpn.Restarts);
    }

    /// <summary>
    /// The first scrape waits until the test finishes it; later scrapes return <paramref name="then"/>.
    /// </summary>
    private static (TaskCompletionSource Started, TaskCompletionSource<ScrapeResult> Finish) BlockFirstScrape(
        RunnerHarness harness,
        ScrapeResult? then = null)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<ScrapeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var scrapes = 0;
        harness.Scraper.OnScrape = _ =>
        {
            if (Interlocked.Increment(ref scrapes) == 1)
            {
                started.SetResult();
                return finish.Task;
            }
            return Task.FromResult(then ?? ScrapeResult.Succeeded("content"));
        };
        return (started, finish);
    }
}
