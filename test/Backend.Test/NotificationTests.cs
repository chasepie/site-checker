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

    [TestMethod]
    public async Task LoweringThresholdMidRun_ReportsOnTheNextKnownFailure()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct, knownFailuresThreshold: 5);
        await RunChecksAsync(harness, site, Content("a"), Known(), Known(), Known());

        await harness.SetKnownFailuresThresholdAsync(site.Id, 2, Ct);
        await RunChecksAsync(harness, site, Known());

        var notification = Assert.ContainsSingle(harness.Notifications.Sent);
        Assert.AreEqual("4 Known Failures: Access Denied", notification.Body);
    }

    [TestMethod]
    public async Task RaisingThresholdMidRun_DoesNotReportTheRunAgain()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct, knownFailuresThreshold: 2);
        await RunChecksAsync(harness, site, Content("a"), Known(), Known());
        Assert.HasCount(1, harness.Notifications.Sent);

        await harness.SetKnownFailuresThresholdAsync(site.Id, 5, Ct);
        await RunChecksAsync(harness, site, Known(), Known(), Known());

        Assert.HasCount(1, harness.Notifications.Sent);
    }

    [TestMethod]
    public async Task EmptyCheckRecordedWhileACheckIsOpen_EndsTheRunBeforeThatCheckFinishes()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);
        await RunChecksAsync(harness, site, Content("a"), Unexpected("boom"));

        // The open check is created first but finishes after the Empty Check.
        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        harness.Time.Advance(TimeSpan.FromMinutes(1));
        await harness.Runner.RecordEmptyCheckAsync(site.Id, Ct);
        harness.Time.Advance(TimeSpan.FromMinutes(1));
        harness.Scraper.OnScrape = _ => Task.FromResult<IScrapeResult>(Content("a"));
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));

        // No Recovery: the Empty Check ended the run and reset the baseline, so "a" is an update.
        CollectionAssert.AreEqual(
            new[] { NotificationKind.Failing, NotificationKind.Updated },
            Kinds(harness));
    }

    [TestMethod]
    public async Task EachKind_UsesTheAgreedChannelSettings()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);

        await RunChecksAsync(harness, site,
            Content("a"), Content("b"),            // Updated
            Unexpected("boom"), Content("b"),      // Failing, Recovered
            Unexpected("boom"), Content("c"));     // Failing, Recovered and Updated

        CollectionAssert.AreEqual(
            new[]
            {
                (NotificationKind.Updated, NotificationSettings.Success),
                (NotificationKind.Failing, NotificationSettings.Failure),
                (NotificationKind.Recovered, NotificationSettings.Failure),
                (NotificationKind.Failing, NotificationSettings.Failure),
                (NotificationKind.RecoveredAndUpdated, NotificationSettings.FailureThenSuccess),
            },
            harness.Notifications.Sent.Select(n => (n.Kind, n.Settings)).ToList());
    }

    // ---- Delivery ----

    [TestMethod]
    public async Task FailedDelivery_LeavesTheRunUnreported_SoTheNextFailureRetriesTheAlert()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);
        await RunChecksAsync(harness, site, Content("a"));

        harness.Notifications.Fail = true;
        harness.OtherChannel.Fail = true;
        await RunChecksAsync(harness, site, Unexpected("boom"));
        harness.Notifications.Fail = false;
        harness.OtherChannel.Fail = false;
        await RunChecksAsync(harness, site, Unexpected("still broken"), Content("a"));

        CollectionAssert.AreEqual(
            new[] { NotificationKind.Failing, NotificationKind.Recovered },
            Kinds(harness));
        Assert.AreEqual("still broken", harness.Notifications.Sent[0].Body);
    }

    [TestMethod]
    public async Task ShutdownAfterDelivery_StillRecordsTheRunAsReported()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);
        await RunChecksAsync(harness, site, Content("baseline"));

        // Shutdown lands just after the alert is delivered.
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        harness.Notifications.AfterSend = shutdown.Cancel;
        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        harness.Scraper.OnScrape = _ => Task.FromResult<IScrapeResult>(Unexpected("boom"));
        try
        {
            await harness.Runner.RunNextAsync(shutdown.Token);
        }
        catch (OperationCanceledException)
        {
            // Shutdown may surface once the delivery has been recorded.
        }
        harness.Notifications.AfterSend = null;

        var failed = (await harness.GetChecksAsync(site.Id, Ct))[^1];
        Assert.IsNotNull(failed.ReportedAt);

        // After a restart the run is still known to be reported: no repeat, and a Recovery.
        await harness.RestartAsync();
        await RunChecksAsync(harness, site, Unexpected("still broken"), Content("baseline"));
        CollectionAssert.AreEqual(
            new[] { NotificationKind.Failing, NotificationKind.Recovered },
            Kinds(harness));
    }

    [TestMethod]
    public async Task ChannelCanceledByShutdown_DoesNotLoseAnotherChannelsDelivery()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);
        await RunChecksAsync(harness, site, Content("baseline"));

        // The other channel is mid-send when shutdown cancels it; this channel has already delivered.
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        harness.OtherChannel.BeforeSend = () =>
        {
            shutdown.Cancel();
            shutdown.Token.ThrowIfCancellationRequested();
        };
        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        harness.Scraper.OnScrape = _ => Task.FromResult<IScrapeResult>(Unexpected("boom"));
        try
        {
            await harness.Runner.RunNextAsync(shutdown.Token);
        }
        catch (OperationCanceledException)
        {
            // Shutdown may surface once the delivery has been recorded.
        }

        Assert.AreEqual(NotificationKind.Failing, Assert.ContainsSingle(harness.Notifications.Sent).Kind);
        var failed = (await harness.GetChecksAsync(site.Id, Ct))[^1];
        Assert.IsNotNull(failed.ReportedAt);
    }

    [TestMethod]
    public async Task NoRecovery_WhenTheRunsAlertNeverReachedAChannel()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);
        await RunChecksAsync(harness, site, Content("a"));

        harness.Notifications.Fail = true;
        harness.OtherChannel.Fail = true;
        await RunChecksAsync(harness, site, Unexpected("boom"));
        harness.Notifications.Fail = false;
        harness.OtherChannel.Fail = false;
        await RunChecksAsync(harness, site, Content("a"));

        Assert.IsEmpty(harness.Notifications.Sent);
    }


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
