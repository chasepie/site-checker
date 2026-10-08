namespace SiteChecker.Backend.IntegrationTests;

using SiteChecker.Backend.Notifiers;
using SiteChecker.Database.Model;
using SiteChecker.Scraper;
using ScriptAction = SiteChecker.Scripting.RequestedAction;

/// <summary>
/// Retry, through the runner. Change VPN Location is in <see cref="VpnLocationTests"/>.
/// </summary>
[TestClass]
public sealed class RequestedActionTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    private static ScrapeResult KnownFailure(params ScriptAction[] actions) => ScrapeResult.KnownFailure("Blocked", actions);

    private static ScrapeResult Content(string content) => ScrapeResult.Succeeded(content);

    /// <summary>
    /// Each scrape returns the next result, in order.
    /// </summary>
    private static void ScrapeInOrder(RunnerHarness harness, params ScrapeResult[] results)
    {
        var next = new Queue<ScrapeResult>(results);
        harness.Scraper.OnScrape = _ => Task.FromResult(next.Dequeue());
    }

    [TestMethod]
    public async Task Retry_IsQueuedWithTheOutcome_BehindChecksAlreadyQueued()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var blocked = await harness.AddSiteAsync(null, Ct);
        var other = await harness.AddSiteAsync(null, Ct);
        await harness.Runner.RequestCheckAsync(blocked.Id, Ct);
        harness.Time.Advance(TimeSpan.FromMinutes(1));
        await harness.Runner.RequestCheckAsync(other.Id, Ct);
        harness.Time.Advance(TimeSpan.FromMinutes(1));
        ScrapeInOrder(harness, KnownFailure(ScriptAction.Retry), Content("other"), Content("retried"));

        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));

        var checks = await harness.GetChecksAsync(blocked.Id, Ct);
        Assert.HasCount(2, checks);
        Assert.AreEqual(CheckStatus.Failed, checks[0].Status);
        Assert.AreEqual(FailureKind.Known, checks[0].FailureKind);
        CollectionAssert.AreEqual(new[] { RequestedAction.Retry }, checks[0].RequestedActions);
        Assert.AreEqual(CheckStatus.Queued, checks[1].Status);

        // The retry joins the back of the queue.
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));
        Assert.AreEqual(other.Id, harness.Scraper.Requests[^1].Site.Id);
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));
        Assert.AreEqual(blocked.Id, harness.Scraper.Requests[^1].Site.Id);
        Assert.AreEqual("retried", (await harness.GetChecksAsync(blocked.Id, Ct))[1].Value);
    }

    [TestMethod]
    public async Task Retry_IsNotQueued_WhenTheOutcomeCannotBeSaved()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);
        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        ScrapeInOrder(harness, KnownFailure(ScriptAction.Retry));
        // The save that records the Known Failure and queues the retry.
        harness.SaveFaults.FailNextSaveWhen = db => db.ChangeTracker.Entries<SiteCheck>()
            .Any(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Added);

        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));

        var check = Assert.ContainsSingle(await harness.GetChecksAsync(site.Id, Ct));
        Assert.AreEqual(CheckStatus.Failed, check.Status);
        Assert.AreEqual(FailureKind.Unexpected, check.FailureKind);
        Assert.IsEmpty(check.RequestedActions);
    }

    [TestMethod]
    public async Task Retry_IsHonoredOncePerFailingRun()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);
        ScrapeInOrder(harness,
            KnownFailure(ScriptAction.Retry),   // queues a retry
            KnownFailure(ScriptAction.Retry),   // the retry: same run, so no second retry
            Content("back"),                    // ends the run
            KnownFailure(ScriptAction.Retry));  // a new run: retried again

        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));
        Assert.IsFalse(await harness.Runner.RunNextAsync(Ct));
        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));
        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));

        var checks = await harness.GetChecksAsync(site.Id, Ct);
        CollectionAssert.AreEqual(
            new[] { CheckStatus.Failed, CheckStatus.Failed, CheckStatus.Succeeded, CheckStatus.Failed, CheckStatus.Queued },
            checks.Select(c => c.Status).ToArray());
        CollectionAssert.AreEqual(new[] { RequestedAction.Retry }, checks[0].RequestedActions);
        Assert.IsEmpty(checks[1].RequestedActions);
        CollectionAssert.AreEqual(new[] { RequestedAction.Retry }, checks[3].RequestedActions);
    }

    [TestMethod]
    public async Task Retry_RunsEvenOutsideTheSitesScheduleWindow()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var outsideNow = new SiteSchedule { Enabled = true, Start = new TimeOnly(1, 0), End = new TimeOnly(2, 0), Interval = 15 };
        var site = await harness.AddSiteAsync(outsideNow, Ct);
        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        ScrapeInOrder(harness, KnownFailure(ScriptAction.Retry), Content("retried"));

        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));
        await harness.Runner.QueueDueChecksAsync(Ct);
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));

        var checks = await harness.GetChecksAsync(site.Id, Ct);
        Assert.HasCount(2, checks);
        Assert.AreEqual("retried", checks[1].Value);
    }

    [TestMethod]
    public async Task Retry_CountsTowardTheKnownFailureThreshold()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct, knownFailuresThreshold: 2);
        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        ScrapeInOrder(harness, KnownFailure(ScriptAction.Retry), KnownFailure(ScriptAction.Retry));

        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));
        Assert.IsEmpty(harness.Notifications.Sent);
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));

        var notification = Assert.ContainsSingle(harness.Notifications.Sent);
        Assert.AreEqual(NotificationKind.Failing, notification.Kind);
        Assert.AreEqual("2 Known Failures: Blocked", notification.Body);
    }

    [TestMethod]
    public async Task SuccessfulRetry_AfterAnUnreportedRun_SendsNoRecovery_ButNotifiesUpdated()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);
        ScrapeInOrder(harness, Content("a"), KnownFailure(ScriptAction.Retry), Content("b"));

        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));
        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));

        var notification = Assert.ContainsSingle(harness.Notifications.Sent);
        Assert.AreEqual(NotificationKind.Updated, notification.Kind);
        Assert.AreEqual("b", notification.Body);
    }

    [TestMethod]
    public async Task SuccessfulRetry_AfterAReportedRun_NotifiesRecovered()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct, knownFailuresThreshold: 1);
        ScrapeInOrder(harness, Content("a"), KnownFailure(ScriptAction.Retry), Content("a"));

        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));
        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));

        CollectionAssert.AreEqual(
            new[] { NotificationKind.Failing, NotificationKind.Recovered },
            harness.Notifications.Sent.Select(n => n.Kind).ToArray());
    }

    [TestMethod]
    public async Task UnexpectedFailure_CarriesOutNoRequestedActions()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);
        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        ScrapeInOrder(harness, ScrapeResult.Unexpected("boom") with { RequestedActions = [ScriptAction.Retry] });

        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));

        var check = Assert.ContainsSingle(await harness.GetChecksAsync(site.Id, Ct));
        Assert.IsEmpty(check.RequestedActions);
    }
}
