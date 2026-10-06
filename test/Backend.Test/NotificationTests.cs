namespace SiteChecker.Backend.Test;

using SiteChecker.Backend.Notifiers;
using SiteChecker.Database.Model;
using SiteChecker.Scraper;
using SiteChecker.Scraper.Exceptions;

/// <summary>
/// Notifications are tested through the runner: a check finishes, and the recording channel
/// shows what was sent.
/// </summary>
[TestClass]
public sealed class NotificationTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    private static SuccessScrapeResult Content(string content, byte[]? screenshot = null)
        => new() { Content = content, Screenshot = screenshot };

    private static FailureScrapeResult Known()
        => FailureScrapeResult.FromException(new AccessDeniedScraperException());

    private static FailureScrapeResult Unexpected(string message)
        => FailureScrapeResult.FromException(new UnexpectedScraperException(message));

    /// <summary>
    /// Runs one Site Check per result, in order.
    /// </summary>
    private async Task RunChecksAsync(RunnerHarness harness, Site site, params IScrapeResult[] results)
    {
        foreach (var result in results)
        {
            await harness.Runner.RequestCheckAsync(site.Id, Ct);
            harness.Scraper.OnScrape = _ => Task.FromResult(result);
            Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));
        }
    }

    private static List<NotificationKind> Kinds(RunnerHarness harness)
        => harness.Notifications.Sent.Select(n => n.Kind).ToList();

    // ---- Content changes ----

    [TestMethod]
    public async Task Updated_WhenContentDiffersFromPreviousDone()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);

        await RunChecksAsync(harness, site, Content("in stock: no"), Content("in stock: yes", [1, 2, 3]));

        var notification = Assert.ContainsSingle(harness.Notifications.Sent);
        Assert.AreEqual(NotificationKind.Updated, notification.Kind);
        Assert.AreEqual("Site 1 Updated", notification.Title);
        Assert.AreEqual("in stock: yes", notification.Body);
        Assert.AreEqual(site.Url, notification.SiteUrl);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, notification.Screenshot);
    }

    [TestMethod]
    public async Task Nothing_WhenContentIsUnchanged()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);

        await RunChecksAsync(harness, site, Content("same"), Content("same"));

        Assert.IsEmpty(harness.Notifications.Sent);
    }

    [TestMethod]
    public async Task Nothing_ForSitesFirstDoneCheck()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);

        await RunChecksAsync(harness, site, Content("baseline"));

        Assert.IsEmpty(harness.Notifications.Sent);
    }

    // ---- Failing Runs ----

    [TestMethod]
    public async Task Failing_OnFirstUnexpectedFailureOfRunOnly()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);

        await RunChecksAsync(harness, site, Content("a"), Unexpected("boom"), Unexpected("still broken"));

        var notification = Assert.ContainsSingle(harness.Notifications.Sent);
        Assert.AreEqual(NotificationKind.Failing, notification.Kind);
        Assert.AreEqual("Site 1 Check Failed", notification.Title);
        Assert.AreEqual("boom", notification.Body);
    }

    [TestMethod]
    public async Task Failing_WhenScraperThrows()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);
        await RunChecksAsync(harness, site, Content("a"));

        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        harness.Scraper.OnScrape = _ => throw new InvalidOperationException("browser exploded");
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));

        var notification = Assert.ContainsSingle(harness.Notifications.Sent);
        Assert.AreEqual(NotificationKind.Failing, notification.Kind);
        Assert.AreEqual("browser exploded", notification.Body);
    }

    [TestMethod]
    public async Task KnownFailures_ReportedOnce_WhenTheyReachTheThreshold()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct, knownFailuresThreshold: 3);

        await RunChecksAsync(harness, site, Content("a"), Known(), Known());
        Assert.IsEmpty(harness.Notifications.Sent);

        await RunChecksAsync(harness, site, Known());
        var notification = Assert.ContainsSingle(harness.Notifications.Sent);
        Assert.AreEqual(NotificationKind.Failing, notification.Kind);
        Assert.AreEqual("Site 1 Check Failed", notification.Title);
        Assert.AreEqual("3 Known Failures: Access Denied", notification.Body);

        await RunChecksAsync(harness, site, Known(), Known());
        Assert.HasCount(1, harness.Notifications.Sent);
    }

    [TestMethod]
    public async Task FailingRun_IsReportedAtMostOnce_WhenFailureKindsMix()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct, knownFailuresThreshold: 3);

        await RunChecksAsync(harness, site, Content("a"), Known(), Unexpected("boom"), Known(), Known(), Known());

        var notification = Assert.ContainsSingle(harness.Notifications.Sent);
        Assert.AreEqual(NotificationKind.Failing, notification.Kind);
        Assert.AreEqual("boom", notification.Body);
    }

    // ---- Recovery ----

    [TestMethod]
    public async Task Recovered_WhenReportedRunEnds_WithUnchangedContent()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);

        await RunChecksAsync(harness, site, Content("a"), Unexpected("boom"), Content("a"));

        CollectionAssert.AreEqual(
            new[] { NotificationKind.Failing, NotificationKind.Recovered },
            Kinds(harness));
        var recovered = harness.Notifications.Sent[1];
        Assert.AreEqual("Site 1 Recovered", recovered.Title);
        Assert.AreEqual("a", recovered.Body);
    }

    [TestMethod]
    public async Task RecoveredAndUpdated_WhenReportedRunEnds_WithChangedContent()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);

        await RunChecksAsync(harness, site, Content("a"), Unexpected("boom"), Content("b"));

        CollectionAssert.AreEqual(
            new[] { NotificationKind.Failing, NotificationKind.RecoveredAndUpdated },
            Kinds(harness));
        var recovered = harness.Notifications.Sent[1];
        Assert.AreEqual("Site 1 Recovered and Updated", recovered.Title);
        Assert.AreEqual("b", recovered.Body);
    }

    [TestMethod]
    public async Task Recovered_WhenSitesFirstChecksFailed()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);

        await RunChecksAsync(harness, site, Unexpected("boom"), Content("a"));

        CollectionAssert.AreEqual(
            new[] { NotificationKind.Failing, NotificationKind.Recovered },
            Kinds(harness));
    }

    [TestMethod]
    public async Task NoRecovery_WhenRunWasNeverReported()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct, knownFailuresThreshold: 5);

        await RunChecksAsync(harness, site, Content("a"), Known(), Known(), Content("a"));
        Assert.IsEmpty(harness.Notifications.Sent);

        // A content change after an unreported run is just an update.
        await RunChecksAsync(harness, site, Known(), Content("b"));
        Assert.AreEqual(NotificationKind.Updated, Assert.ContainsSingle(harness.Notifications.Sent).Kind);
    }

    [TestMethod]
    public async Task EmptyCheck_EndsReportedRunSilently_SoTheNextFailureIsReportedAgain()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);
        await RunChecksAsync(harness, site, Content("a"), Unexpected("boom"));

        await harness.Runner.RecordEmptyCheckAsync(site.Id, Ct);
        Assert.HasCount(1, harness.Notifications.Sent);

        await RunChecksAsync(harness, site, Unexpected("boom again"));
        CollectionAssert.AreEqual(
            new[] { NotificationKind.Failing, NotificationKind.Failing },
            Kinds(harness));
    }

    // ---- Delivery ----

    [TestMethod]
    public async Task FailingChannel_DoesNotBlockOtherChannels_OrAffectTheCheck()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);
        harness.OtherChannel.Fail = true;

        await RunChecksAsync(harness, site, Content("a"), Content("b"));

        Assert.AreEqual(NotificationKind.Updated, Assert.ContainsSingle(harness.Notifications.Sent).Kind);
        var checks = await harness.GetChecksAsync(site.Id, Ct);
        Assert.AreEqual(CheckStatus.Done, checks[^1].Status);
    }

    // ---- Recorded outcome ----

    [TestMethod]
    public async Task FailureKind_IsRecordedOnFailedChecksOnly()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);
        await RunChecksAsync(harness, site, Content("a"), Known(), Unexpected("boom"));
        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        harness.Scraper.OnScrape = _ => throw new InvalidOperationException("browser exploded");
        await harness.Runner.RunNextAsync(Ct);

        var kinds = (await harness.GetChecksAsync(site.Id, Ct)).Select(c => c.FailureKind).ToList();

        CollectionAssert.AreEqual(
            new FailureKind?[] { null, FailureKind.Known, FailureKind.Unexpected, FailureKind.Unexpected },
            kinds);
    }
}
