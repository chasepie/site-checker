namespace SiteChecker.Backend.IntegrationTests;

using Microsoft.EntityFrameworkCore;
using SiteChecker.Database.Model;
using SiteChecker.Scraper;
using SiteChecker.Scraper.Exceptions;

[TestClass]
public sealed class SiteCheckRunnerTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    // ---- Due rule ----

    [TestMethod]
    public async Task QueueDueChecks_QueuesCheck_WhenSiteInsideWindowHasNoChecks()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(RunnerHarness.AllDaySchedule, Ct);

        await harness.Runner.QueueDueChecksAsync(Ct);

        var check = Assert.ContainsSingle(await harness.GetChecksAsync(site.Id, Ct));
        Assert.AreEqual(CheckStatus.Queued, check.Status);
        Assert.AreEqual(harness.Time.GetUtcNow().UtcDateTime, check.StartDate);
    }

    [TestMethod]
    public async Task QueueDueChecks_QueuesNothing_WhenOutsideWindowDisabledOrScheduleIncomplete()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var outsideWindow = await harness.AddSiteAsync(new SiteSchedule
        {
            Enabled = true,
            Start = new TimeOnly(1, 0),
            End = new TimeOnly(2, 0),
            Interval = 15,
        }, Ct);
        var disabled = RunnerHarness.AllDaySchedule;
        disabled.Enabled = false;
        var disabledSite = await harness.AddSiteAsync(disabled, Ct);
        var incomplete = RunnerHarness.AllDaySchedule;
        incomplete.Interval = null;
        var incompleteSite = await harness.AddSiteAsync(incomplete, Ct);

        await harness.Runner.QueueDueChecksAsync(Ct);

        Assert.IsEmpty(await harness.GetChecksAsync(outsideWindow.Id, Ct));
        Assert.IsEmpty(await harness.GetChecksAsync(disabledSite.Id, Ct));
        Assert.IsEmpty(await harness.GetChecksAsync(incompleteSite.Id, Ct));
    }

    [TestMethod]
    public async Task QueueDueChecks_WaitsForInterval_SinceLastCheckStarted()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(RunnerHarness.AllDaySchedule, Ct);
        await harness.Runner.RecordBaselineResetAsync(site.Id, Ct);

        harness.Time.Advance(TimeSpan.FromMinutes(14));
        await harness.Runner.QueueDueChecksAsync(Ct);
        Assert.HasCount(1, await harness.GetChecksAsync(site.Id, Ct));

        harness.Time.Advance(TimeSpan.FromMinutes(1));
        await harness.Runner.QueueDueChecksAsync(Ct);
        var checks = await harness.GetChecksAsync(site.Id, Ct);
        Assert.HasCount(2, checks);
        Assert.AreEqual(CheckStatus.Queued, checks[1].Status);
    }

    [TestMethod]
    public async Task QueueDueChecks_QueuesNothing_WhenSiteHasOpenCheck()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(RunnerHarness.AllDaySchedule, Ct);
        await harness.Runner.QueueDueChecksAsync(Ct);

        // A Baseline Reset is newer than the open check, so "latest check" alone would look finished.
        await harness.Runner.RecordBaselineResetAsync(site.Id, Ct);
        harness.Time.Advance(TimeSpan.FromHours(1));
        await harness.Runner.QueueDueChecksAsync(Ct);

        var checks = await harness.GetChecksAsync(site.Id, Ct);
        Assert.HasCount(2, checks);
        Assert.ContainsSingle(c => c.Status == CheckStatus.Queued, checks);
    }

    [TestMethod]
    public async Task QueueDueChecks_UsesLocalTime_ForWindowEdges()
    {
        var utcMinus5 = TimeZoneInfo.CreateCustomTimeZone("Test-05", TimeSpan.FromHours(-5), "Test-05", "Test-05");
        // 13:59 UTC is 08:59 local
        var start = new DateTimeOffset(2026, 10, 5, 13, 59, 0, TimeSpan.Zero);
        await using var harness = await RunnerHarness.CreateAsync(Ct, utcMinus5, start);
        var startsAtNine = await harness.AddSiteAsync(new SiteSchedule
        {
            Enabled = true,
            Start = new TimeOnly(9, 0),
            End = new TimeOnly(10, 0),
            Interval = 15,
        }, Ct);
        // A 1-minute interval, so only the window end can stop it being due again at 09:00.
        var endsAtNine = await harness.AddSiteAsync(new SiteSchedule
        {
            Enabled = true,
            Start = new TimeOnly(8, 0),
            End = new TimeOnly(9, 0),
            Interval = 1,
        }, Ct);

        await harness.Runner.QueueDueChecksAsync(Ct);
        Assert.IsEmpty(await harness.GetChecksAsync(startsAtNine.Id, Ct));
        Assert.HasCount(1, await harness.GetChecksAsync(endsAtNine.Id, Ct));
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));

        harness.Time.Advance(TimeSpan.FromMinutes(1)); // 09:00 local
        await harness.Runner.QueueDueChecksAsync(Ct);
        Assert.HasCount(1, await harness.GetChecksAsync(startsAtNine.Id, Ct));
        Assert.HasCount(1, await harness.GetChecksAsync(endsAtNine.Id, Ct));
    }

    // ---- Requests ----

    [TestMethod]
    public async Task RequestCheck_CreatesQueuedCheck_WhenSiteHasNoOpenCheck()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);

        var requested = await harness.Runner.RequestCheckAsync(site.Id, Ct);

        Assert.IsNotNull(requested);
        var check = Assert.ContainsSingle(await harness.GetChecksAsync(site.Id, Ct));
        Assert.AreEqual(requested.Id, check.Id);
        Assert.AreEqual(CheckStatus.Queued, check.Status);
    }

    [TestMethod]
    public async Task RequestCheck_ReturnsOpenCheck_WhenSiteAlreadyHasOne()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);
        var first = await harness.Runner.RequestCheckAsync(site.Id, Ct);

        var second = await harness.Runner.RequestCheckAsync(site.Id, Ct);

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreEqual(first.Id, second.Id);
        Assert.HasCount(1, await harness.GetChecksAsync(site.Id, Ct));
    }

    [TestMethod]
    public async Task RequestCheck_ReturnsNull_WhenSiteDoesNotExist()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);

        Assert.IsNull(await harness.Runner.RequestCheckAsync(404, Ct));
    }

    [TestMethod]
    public async Task RecordBaselineReset_CreatesSucceededCheckWithPlaceholderContent()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);

        await harness.Runner.RecordBaselineResetAsync(site.Id, Ct);

        var check = Assert.ContainsSingle(await harness.GetChecksAsync(site.Id, Ct));
        Assert.AreEqual(CheckStatus.Succeeded, check.Status);
        Assert.AreEqual("[Baseline Reset]", check.Value);
        Assert.IsNotNull(check.CompletedDate);
        Assert.IsEmpty(harness.Scraper.Requests);
    }

    // ---- Running ----

    [TestMethod]
    public async Task RunNext_RunsOldestQueuedCheck_ThroughCheckingToSucceeded()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var older = await harness.AddSiteAsync(null, Ct);
        var newer = await harness.AddSiteAsync(null, Ct);
        await harness.Runner.RequestCheckAsync(older.Id, Ct);
        harness.Time.Advance(TimeSpan.FromMinutes(1));
        await harness.Runner.RequestCheckAsync(newer.Id, Ct);

        CheckStatus? statusDuringScrape = null;
        string? locationDuringScrape = null;
        harness.Scraper.OnScrape = async request =>
        {
            await using var dbContext = harness.CreateDbContext();
            var current = await dbContext.SiteChecks.SingleAsync(sc => sc.Id == request.Id, Ct);
            statusDuringScrape = current.Status;
            locationDuringScrape = current.VpnLocationId;
            return new SuccessScrapeResult { Content = "new content", Screenshot = [1, 2, 3] };
        };

        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));

        Assert.AreEqual(CheckStatus.Checking, statusDuringScrape);
        Assert.AreEqual("No VPN", locationDuringScrape);
        var request = Assert.ContainsSingle(harness.Scraper.Requests);
        Assert.AreEqual(older.ScraperId, request.ScraperId);

        var succeeded = Assert.ContainsSingle(await harness.GetChecksAsync(older.Id, Ct));
        Assert.AreEqual(CheckStatus.Succeeded, succeeded.Status);
        Assert.AreEqual("new content", succeeded.Value);
        Assert.IsNotNull(succeeded.CompletedDate);
        await using (var dbContext = harness.CreateDbContext())
        {
            var screenshot = await dbContext.SiteCheckScreenshots.SingleAsync(s => s.SiteCheckId == succeeded.Id, Ct);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, screenshot.Data);
        }

        var stillQueued = Assert.ContainsSingle(await harness.GetChecksAsync(newer.Id, Ct));
        Assert.AreEqual(CheckStatus.Queued, stillQueued.Status);
    }

    [TestMethod]
    public async Task RunNext_RecordsFailed_WhenScrapeResultIsFailure()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);
        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        harness.Scraper.OnScrape = _ => Task.FromResult<IScrapeResult>(
            FailureScrapeResult.FromException(new AccessDeniedScraperException()));

        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));

        var check = Assert.ContainsSingle(await harness.GetChecksAsync(site.Id, Ct));
        Assert.AreEqual(CheckStatus.Failed, check.Status);
        Assert.AreEqual("Access Denied", check.Value);
        Assert.IsNotNull(check.CompletedDate);
    }

    [TestMethod]
    public async Task RunNext_RecordsFailed_WhenScraperThrows()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);
        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        harness.Scraper.OnScrape = _ => throw new InvalidOperationException("browser exploded");

        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));

        var check = Assert.ContainsSingle(await harness.GetChecksAsync(site.Id, Ct));
        Assert.AreEqual(CheckStatus.Failed, check.Status);
        Assert.AreEqual("browser exploded", check.Value);
    }

    [TestMethod]
    public async Task RunNext_RecordsFailed_WhenSavingResultFails()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);
        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        harness.Scraper.OnScrape = async request =>
        {
            // Screenshots are unique per Site Check, so the runner's own screenshot insert will fail.
            await using var dbContext = harness.CreateDbContext();
            dbContext.SiteCheckScreenshots.Add(new SiteCheckScreenshot { SiteCheckId = request.Id, Data = [9] });
            await dbContext.SaveChangesAsync(Ct);
            return new SuccessScrapeResult { Content = "never saved", Screenshot = [1] };
        };

        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));

        var check = Assert.ContainsSingle(await harness.GetChecksAsync(site.Id, Ct));
        Assert.AreEqual(CheckStatus.Failed, check.Status);
        Assert.AreNotEqual("never saved", check.Value);
    }

    [TestMethod]
    public async Task RunNext_ReturnsFalse_WhenNothingIsQueued()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);
        await harness.Runner.RecordBaselineResetAsync(site.Id, Ct);

        Assert.IsFalse(await harness.Runner.RunNextAsync(Ct));
        Assert.IsEmpty(harness.Scraper.Requests);
    }

    // ---- Recovery ----

    [TestMethod]
    public async Task RunNext_RerunsCheck_WhenRecordingItsFailureFailed()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);
        await harness.Runner.RequestCheckAsync(site.Id, Ct);
        harness.Scraper.OnScrape = _ => throw new InvalidOperationException("browser exploded");
        harness.SaveFaults.FailNextSaveWhen = dbContext => dbContext.ChangeTracker
            .Entries<SiteCheck>()
            .Any(e => e.State == EntityState.Modified && e.Entity.Status == CheckStatus.Failed);

        // The database rejects the Failed save, so the outcome can't be recorded.
        await Assert.ThrowsAsync<DbUpdateException>(() => harness.Runner.RunNextAsync(Ct));

        // Without a restart, the Site recovers: the stuck check is re-run.
        harness.Scraper.OnScrape = _ => Task.FromResult<IScrapeResult>(
            new SuccessScrapeResult { Content = "recovered" });
        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));

        var check = Assert.ContainsSingle(await harness.GetChecksAsync(site.Id, Ct));
        Assert.AreEqual(CheckStatus.Succeeded, check.Status);
        Assert.AreEqual("recovered", check.Value);
    }

    [TestMethod]
    public async Task RunNext_BroadcastsChecking_BeforeScraping_WhenRerunningInterruptedCheck()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);
        int interruptedId;
        // Left Checking by a previous run, already holding the location this run will resolve.
        await using (var dbContext = harness.CreateDbContext())
        {
            var interrupted = new SiteCheck(site, harness.Time.GetUtcNow().UtcDateTime)
            {
                Status = CheckStatus.Checking,
                VpnLocationId = "No VPN",
            };
            dbContext.SiteChecks.Add(interrupted);
            await dbContext.SaveChangesAsync(Ct);
            interruptedId = interrupted.Id;
        }

        IReadOnlyList<(int SiteCheckId, CheckStatus Status)>? broadcastBeforeScrape = null;
        harness.Scraper.OnScrape = _ =>
        {
            broadcastBeforeScrape = harness.Broadcasts.SiteCheckUpdates;
            return Task.FromResult<IScrapeResult>(new SuccessScrapeResult { Content = "content" });
        };

        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));

        Assert.IsNotNull(broadcastBeforeScrape);
        CollectionAssert.Contains(
            broadcastBeforeScrape.ToList(),
            (interruptedId, CheckStatus.Checking));
    }

    [TestMethod]
    public async Task RunNext_RerunsInterruptedChecks_AndLeavesCompleteChecksAlone()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(null, Ct);
        var startDate = harness.Time.GetUtcNow().UtcDateTime;
        // Rows as a previous run of the app could have left them.
        await using (var dbContext = harness.CreateDbContext())
        {
            dbContext.SiteChecks.AddRange(
                new SiteCheck(site, startDate) { Status = CheckStatus.Succeeded, Value = "done" },
                new SiteCheck(site, startDate) { Status = CheckStatus.Failed, Value = "failed" },
                new SiteCheck(site, startDate) { Status = CheckStatus.Checking });
            await dbContext.SaveChangesAsync(Ct);
        }

        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));

        var checks = await harness.GetChecksAsync(site.Id, Ct);
        CollectionAssert.AreEqual(
            new[] { CheckStatus.Succeeded, CheckStatus.Failed, CheckStatus.Succeeded },
            checks.Select(c => c.Status).ToList());
        Assert.AreEqual("done", checks[0].Value);
        Assert.AreEqual("failed", checks[1].Value);
        Assert.AreEqual("content", checks[2].Value);
        Assert.HasCount(1, harness.Scraper.Requests);
    }

    [TestMethod]
    public async Task DueCheckInterruptedByRestart_IsRecoveredAndRunToSucceeded()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var site = await harness.AddSiteAsync(RunnerHarness.AllDaySchedule, Ct);
        await harness.Runner.QueueDueChecksAsync(Ct);

        var scrapeStarted = new TaskCompletionSource();
        var scrapeResult = new TaskCompletionSource<IScrapeResult>();
        harness.Scraper.OnScrape = _ =>
        {
            scrapeStarted.SetResult();
            return scrapeResult.Task;
        };
        using var shutdown = new CancellationTokenSource();
        var interruptedRun = harness.Runner.RunNextAsync(shutdown.Token);
        await scrapeStarted.Task.WaitAsync(Ct);
        await shutdown.CancelAsync();
        scrapeResult.SetCanceled(shutdown.Token);
        await Assert.ThrowsAsync<OperationCanceledException>(() => interruptedRun);

        await harness.RestartAsync();
        harness.Time.Advance(TimeSpan.FromMinutes(1));
        harness.Scraper.OnScrape = _ => Task.FromResult<IScrapeResult>(
            new SuccessScrapeResult { Content = "after restart" });
        await harness.Runner.QueueDueChecksAsync(Ct);

        Assert.IsTrue(await harness.Runner.RunNextAsync(Ct));
        var check = Assert.ContainsSingle(await harness.GetChecksAsync(site.Id, Ct));
        Assert.AreEqual(CheckStatus.Succeeded, check.Status);
        Assert.AreEqual("after restart", check.Value);
    }
}
