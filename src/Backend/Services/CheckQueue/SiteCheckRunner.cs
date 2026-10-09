using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiteChecker.Backend.Extensions;
using SiteChecker.Backend.Services.VPN;
using SiteChecker.Database;
using SiteChecker.Database.Extensions;
using SiteChecker.Database.Model;
using SiteChecker.Scraper;
using ScriptAction = SiteChecker.Scripting.RequestedAction;

namespace SiteChecker.Backend.Services.CheckQueue;

/// <summary>
/// Owns the Site Check lifecycle: decides which Sites are due, accepts requests for Site Checks,
/// and moves each Site Check from Queued to Checking to Succeeded or Failed.
/// </summary>
/// <remarks>
/// The database is the queue (see <c>docs/adr/0001-database-is-the-site-check-queue.md</c>):
/// pending work is every Site Check in the <see cref="CheckStatus.Queued"/> status. The
/// in-memory wake signal only shortens the wait for new work and holds no state.
/// <para>
/// One scrape lock covers everything that uses the shared browser and VPN containers: resolving
/// and rotating the VPN Location, scraping, Test Runs, and manual VPN Location changes (which
/// restart the containers). They never overlap.
/// </para>
/// </remarks>
public sealed class SiteCheckRunner : IDisposable
{
    private const string BaselineResetContent = "[Baseline Reset]";
    private const int DefaultVpnChangeIntervalMinutes = 15;

    /// <summary>
    /// Longest time <see cref="WaitForWorkAsync"/> waits before returning without a wake signal.
    /// </summary>
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IScraperService _scraperService;
    private readonly PiaService _piaService;
    private readonly NotifierService _notifier;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SiteCheckRunner> _logger;
    private readonly TimeSpan _vpnChangeInterval;

    /// <summary>
    /// Serializes creation of open Site Checks so a Site never gets two at once.
    /// </summary>
    private readonly SemaphoreSlim _createLock = new(1, 1);

    /// <summary>
    /// Held around everything that uses the shared browser and VPN containers.
    /// </summary>
    private readonly SemaphoreSlim _scrapeLock = new(1, 1);

    /// <summary>
    /// VPN Locations a Known Failure asked to move off (Change VPN Location), applied before the
    /// next VPN-routed check. In memory only: one lost to a restart just waits for the interval.
    /// </summary>
    private readonly HashSet<string> _failedVpnLocationIds = [];
    private readonly Lock _failedVpnLocationIdsLock = new();

    /// <summary>
    /// Capacity of one, so any number of wake-ups before the next wait collapse into one.
    /// </summary>
    private readonly Channel<bool> _wakeSignal = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    /// <summary>
    /// Timestamp (from <see cref="_timeProvider"/>) of the last VPN Location change.
    /// </summary>
    private long _lastVpnChangeTimestamp;

    public SiteCheckRunner(
        IServiceScopeFactory scopeFactory,
        IScraperService scraperService,
        PiaService piaService,
        NotifierService notifier,
        TimeProvider timeProvider,
        IConfiguration configuration,
        ILogger<SiteCheckRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _scraperService = scraperService;
        _piaService = piaService;
        _notifier = notifier;
        _timeProvider = timeProvider;
        _logger = logger;

        if (!int.TryParse(configuration["VPN_CHANGE_INTERVAL"], out var intervalMinutes))
        {
            intervalMinutes = DefaultVpnChangeIntervalMinutes;
        }
        _vpnChangeInterval = TimeSpan.FromMinutes(intervalMinutes);
        _lastVpnChangeTimestamp = timeProvider.GetTimestamp();
    }

    /// <summary>
    /// Creates a Queued Site Check for every Site that is due: inside its Schedule window, with no
    /// open Site Check, and whose last Site Check started at least one interval ago.
    /// </summary>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task QueueDueChecksAsync(CancellationToken cancellationToken)
    {
        await _createLock.WaitAsync(cancellationToken);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SiteCheckerDbContext>();

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            // Schedule start/end are in local time
            var nowLocalTime = TimeOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);

            var sites = await dbContext.Sites
                .Where(s =>
                    s.Schedule.Enabled
                    && s.Schedule.Interval.HasValue
                    && s.Schedule.Start.HasValue
                    && s.Schedule.End.HasValue)
                .ToListAsync(cancellationToken);

            var queuedAny = false;
            foreach (var site in sites)
            {
                if (!nowLocalTime.IsBetween(site.Schedule.Start!.Value, site.Schedule.End!.Value)
                    || await HasOpenCheckAsync(dbContext, site.Id, cancellationToken))
                {
                    continue;
                }

                // Start dates are stored in UTC
                var lastStartDate = await dbContext.SiteChecks
                    .Where(sc => sc.SiteId == site.Id)
                    .OrderByDescending(sc => sc.StartDate)
                    .Select(sc => (DateTime?)sc.StartDate)
                    .FirstOrDefaultAsync(cancellationToken);
                var interval = TimeSpan.FromMinutes(site.Schedule.Interval!.Value);
                if (lastStartDate > nowUtc - interval)
                {
                    continue;
                }

                _logger.LogInformation("Queueing check for {SiteName}.", site.Name);
                dbContext.SiteChecks.Add(new SiteCheck(site, nowUtc));
                queuedAny = true;
            }

            if (queuedAny)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                Wake();
            }
        }
        finally
        {
            _createLock.Release();
        }
    }

    /// <summary>
    /// Requests a Site Check for a Site. If the Site already has an open Site Check, that Site
    /// Check is returned and nothing is created.
    /// </summary>
    /// <param name="siteId">The ID of the Site to check.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>The open Site Check for the Site, or <c>null</c> if the Site does not exist.</returns>
    public async Task<SiteCheck?> RequestCheckAsync(int siteId, CancellationToken cancellationToken)
    {
        await _createLock.WaitAsync(cancellationToken);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SiteCheckerDbContext>();

            var site = await dbContext.Sites
                .FirstOrDefaultAsync(s => s.Id == siteId, cancellationToken);
            if (site == null)
            {
                return null;
            }

            var openCheck = await dbContext.SiteChecks
                .Where(sc => sc.SiteId == siteId
                    && (sc.Status == CheckStatus.Queued || sc.Status == CheckStatus.Checking))
                .OrderBy(sc => sc.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (openCheck != null)
            {
                return openCheck;
            }

            var siteCheck = new SiteCheck(site, _timeProvider.GetUtcNow().UtcDateTime);
            dbContext.SiteChecks.Add(siteCheck);
            await dbContext.SaveChangesAsync(cancellationToken);
            Wake();

            return siteCheck;
        }
        finally
        {
            _createLock.Release();
        }
    }

    /// <summary>
    /// Records a Baseline Reset for a Site: a Succeeded Site Check with placeholder content and
    /// no scrape, which resets the Baseline the next Site Check is compared against.
    /// </summary>
    /// <param name="siteId">The ID of the Site.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>The Baseline Reset, or <c>null</c> if the Site does not exist.</returns>
    public async Task<SiteCheck?> RecordBaselineResetAsync(int siteId, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SiteCheckerDbContext>();

        var site = await dbContext.Sites
            .FirstOrDefaultAsync(s => s.Id == siteId, cancellationToken);
        if (site == null)
        {
            return null;
        }

        var siteCheck = new SiteCheck(site, _timeProvider.GetUtcNow().UtcDateTime);
        siteCheck.Update(ScrapeResult.Succeeded(BaselineResetContent), _timeProvider.GetUtcNow().UtcDateTime);
        dbContext.SiteChecks.Add(siteCheck);
        await dbContext.SaveChangesAsync(cancellationToken);

        return siteCheck;
    }

    /// <summary>
    /// Re-queues orphaned Site Checks, then claims the oldest Queued Site Check and runs it to
    /// Succeeded or Failed. A failed Site Check is recorded on the Site Check, not thrown; this only
    /// throws when the database itself fails, in which case the caller should wait before
    /// calling again.
    /// </summary>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns><c>true</c> if a Site Check was run; <c>false</c> if none was Queued.</returns>
    public async Task<bool> RunNextAsync(CancellationToken cancellationToken)
    {
        await RequeueOrphanedChecksAsync(cancellationToken);

        var siteCheckId = await ClaimNextAsync(cancellationToken);
        if (siteCheckId == null)
        {
            return false;
        }

        try
        {
            await PerformCheckAsync(siteCheckId.Value, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // If shutdown cancels the check instead, it stays Checking and is re-queued as an
            // orphan the next time the runner looks for work.
            _logger.LogError(ex, "Error occurred running Site Check {SiteCheckId}.", siteCheckId);
            await MarkFailedAsync(siteCheckId.Value, ex, cancellationToken);
        }

        // Outside the try: the outcome is already saved, and a notification problem must never
        // turn a Succeeded check into a Failed one.
        await _notifier.NotifyAsync(siteCheckId.Value, cancellationToken);

        return true;
    }

    /// <summary>
    /// Waits until new work may be available: a Site Check was queued, or a minute has passed.
    /// </summary>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task WaitForWorkAsync(CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(IdleTimeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await _wakeSignal.Reader.WaitToReadAsync(linked.Token);
            _wakeSignal.Reader.TryRead(out _);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timed out; the caller checks the queue anyway.
        }
    }

    /// <summary>
    /// Changes the VPN Location now, waiting for any running scrape first, since it restarts the
    /// containers that scrape uses.
    /// </summary>
    /// <param name="excludeCurrent">Whether to also exclude the current location from rotation.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>The new VPN Location.</returns>
    public async Task<PiaLocation> ChangeVpnLocationAsync(bool excludeCurrent, CancellationToken cancellationToken)
    {
        await _scrapeLock.WaitAsync(cancellationToken);
        try
        {
            return await ChangeVpnLocationCoreAsync(excludeCurrent, cancellationToken);
        }
        finally
        {
            _scrapeLock.Release();
        }
    }

    /// <summary>
    /// Runs a Test Run: a scrape that isn't a Site Check. It waits for any running check, uses
    /// the current VPN Location without rotating, and records nothing, notifies nothing and
    /// carries out none of its Requested Actions.
    /// </summary>
    /// <param name="request">The scrape, with no Site Check ID. Its browser is chosen here.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>The scrape's result.</returns>
    public async Task<ScrapeResult> RunTestAsync(ScrapeRequest request, CancellationToken cancellationToken)
    {
        if (!request.IsTestRun)
        {
            throw new ArgumentException("A Test Run has no Site Check.", nameof(request));
        }

        await _scrapeLock.WaitAsync(cancellationToken);
        try
        {
            var browserType = _scraperService.GetBrowserType(request.Site.UseVpn);
            return await _scraperService.ScrapeAsync(request with { BrowserType = browserType }, cancellationToken);
        }
        finally
        {
            _scrapeLock.Release();
        }
    }

    public void Dispose()
    {
        _createLock.Dispose();
        _scrapeLock.Dispose();
    }

    private void Wake() => _wakeSignal.Writer.TryWrite(true);

    private static Task<bool> HasOpenCheckAsync(
        SiteCheckerDbContext dbContext,
        int siteId,
        CancellationToken cancellationToken)
    {
        return dbContext.SiteChecks.AnyAsync(
            sc => sc.SiteId == siteId
                && (sc.Status == CheckStatus.Queued || sc.Status == CheckStatus.Checking),
            cancellationToken);
    }

    /// <summary>
    /// Re-queues every Site Check left Checking. The runner runs one Site Check at a time and
    /// only calls this between checks, so any Checking row is an orphan: interrupted by a restart,
    /// or a check whose outcome couldn't be saved. Running checks in parallel would require
    /// tracking which checks are in flight, so only true orphans are reset.
    /// </summary>
    private async Task RequeueOrphanedChecksAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SiteCheckerDbContext>();

        var orphaned = await dbContext.SiteChecks
            .Where(sc => sc.Status == CheckStatus.Checking)
            .ToListAsync(cancellationToken);
        if (orphaned.Count == 0)
        {
            return;
        }

        foreach (var siteCheck in orphaned)
        {
            siteCheck.Status = CheckStatus.Queued;
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Re-queued {Count} orphaned site check(s).", orphaned.Count);
    }

    /// <summary>
    /// Moves the oldest Queued Site Check to Checking.
    /// </summary>
    /// <returns>The ID of the claimed Site Check, or <c>null</c> if none was Queued.</returns>
    private async Task<int?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SiteCheckerDbContext>();

        while (true)
        {
            var nextId = await dbContext.SiteChecks
                .Where(sc => sc.Status == CheckStatus.Queued)
                .OrderBy(sc => sc.StartDate)
                .ThenBy(sc => sc.Id)
                .Select(sc => (int?)sc.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (nextId == null)
            {
                return null;
            }

            // Conditional so two workers could never claim the same check. This bypasses the
            // change interceptor; PerformCheckAsync's pre-scrape save broadcasts Checking.
            var claimed = await dbContext.SiteChecks
                .Where(sc => sc.Id == nextId && sc.Status == CheckStatus.Queued)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(sc => sc.Status, CheckStatus.Checking),
                    cancellationToken);
            if (claimed == 1)
            {
                return nextId;
            }
        }
    }

    /// <summary>
    /// Scrapes a claimed Site Check and records the result.
    /// </summary>
    private async Task PerformCheckAsync(int siteCheckId, CancellationToken cancellationToken)
    {
        // A new scope per check ensures each check gets a fresh DbContext with an isolated
        // change tracker, preventing stale entity state from one check affecting the next.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SiteCheckerDbContext>();

        var siteCheck = await dbContext.SiteChecks
            .Include(sc => sc.Site)
            .ThenInclude(s => s.SiteScript)
            .FirstAsync(sc => sc.Id == siteCheckId, cancellationToken);
        var site = siteCheck.Site;

        // Before the scrape lock, so a Site with no usable Scraper fails without rotating the VPN.
        var scraper = ToScraperSpec(site);

        BrowserType browserType;
        ScrapeResult result;
        await _scrapeLock.WaitAsync(cancellationToken);
        try
        {
            browserType = _scraperService.GetBrowserType(site.UseVpn);
            siteCheck.VpnLocationId = (await ResolveVpnLocationAsync(browserType, cancellationToken))?.Id;

            // Save before scraping so clients receive a real-time status update via SignalR while the
            // (potentially long-running) scrape is in progress. An EF Core SaveChanges interceptor
            // hooks into every save and automatically broadcasts entity changes to all connected clients.
            // The claim set Checking without the interceptor, and a re-run orphan may already hold this
            // location, so force a tracked change to guarantee the broadcast.
            dbContext.Entry(siteCheck).Property(sc => sc.Status).IsModified = true;
            await dbContext.SaveChangesAsync(cancellationToken);

            var request = new ScrapeRequest
            {
                SiteCheckId = siteCheck.Id,
                Site = new ScrapeSite(site.Id, site.Name, site.Url, site.UseVpn),
                Scraper = scraper,
                BrowserType = browserType,
                Timeout = site.TimeoutSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
                AlwaysTakeScreenshot = site.AlwaysTakeScreenshot,
            };

            result = await _scraperService.ScrapeAsync(request, cancellationToken);
        }
        finally
        {
            _scrapeLock.Release();
        }

        await RecordOutcomeAsync(dbContext, siteCheck, result, browserType, cancellationToken);
    }

    /// <summary>
    /// Saves the scrape's outcome and screenshot, and carries out the Requested Actions of a Known
    /// Failure: a Retry is queued in the same save, so a crash can't lose it, and a failed VPN
    /// Location is excluded before the next VPN-routed check. Only the actions carried out are
    /// recorded on the Site Check.
    /// </summary>
    private async Task RecordOutcomeAsync(
        SiteCheckerDbContext dbContext,
        SiteCheck siteCheck,
        ScrapeResult result,
        BrowserType browserType,
        CancellationToken cancellationToken)
    {
        var completedDate = _timeProvider.GetUtcNow().UtcDateTime;
        siteCheck.Update(result, completedDate);

        if (result.Screenshot is not null)
        {
            var screenshot = new SiteCheckScreenshot(siteCheck, result.Screenshot);
            await dbContext.SiteCheckScreenshots.AddAsync(screenshot, cancellationToken);
        }

        var requested = result.Outcome == ScrapeOutcome.KnownFailure ? result.RequestedActions : [];

        // Ignored for a Site that doesn't use the VPN: rotating would restart the shared container
        // for no benefit.
        var failedVpnLocationId = requested.Contains(ScriptAction.ChangeVpnLocation)
            && browserType == BrowserType.BrowserlessVpn
            && siteCheck.VpnLocationId is { } locationId
            && locationId != PiaLocation.NoVPN.Id
                ? locationId
                : null;

        var carriedOut = new List<RequestedAction>();
        if (failedVpnLocationId != null)
        {
            carriedOut.Add(RequestedAction.ChangeVpnLocation);
        }

        var retryRequested = requested.Contains(ScriptAction.Retry);
        var queuedRetry = false;
        if (retryRequested)
        {
            // Like any open Site Check, the retry is created under the create lock.
            await _createLock.WaitAsync(cancellationToken);
        }
        try
        {
            if (retryRequested && !await RetriedInFailingRunAsync(dbContext, siteCheck, completedDate, cancellationToken))
            {
                carriedOut.Add(RequestedAction.Retry);
                dbContext.SiteChecks.Add(new SiteCheck(siteCheck.Site, completedDate));
                queuedRetry = true;
            }

            siteCheck.RequestedActions = carriedOut;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            if (retryRequested)
            {
                _createLock.Release();
            }
        }

        if (failedVpnLocationId != null)
        {
            lock (_failedVpnLocationIdsLock)
            {
                _failedVpnLocationIds.Add(failedVpnLocationId);
            }
        }

        if (queuedRetry)
        {
            Wake();
        }
    }

    /// <summary>
    /// Whether a check earlier in the Site's current Failing Run already queued a Retry. A Retry
    /// is honored once per Failing Run, so a Site that keeps hitting the same Known Failure can't
    /// retry in a loop.
    /// </summary>
    private static async Task<bool> RetriedInFailingRunAsync(
        SiteCheckerDbContext dbContext,
        SiteCheck siteCheck,
        DateTime completedDate,
        CancellationToken cancellationToken)
    {
        var (_, failingRun) = await dbContext.SiteChecks.FailingRunBeforeAsync(
            siteCheck.SiteId, completedDate, siteCheck.Id, cancellationToken);
        return await failingRun.AnyAsync(sc => sc.RequestedActions.Contains(RequestedAction.Retry), cancellationToken);
    }

    /// <exception cref="InvalidOperationException">The Site's Scraper is missing its payload or source.</exception>
    private static ScriptSpec ToScraperSpec(Site site) => site.Scraper.Kind switch
    {
        ScraperKind.Script when site.Scraper.Script is { } script && site.SiteScript is { } source
            => new ScriptSpec(script.FileName, source.Source, script.SourceHash),
        ScraperKind.Script
            => throw new InvalidOperationException($"Site {site.Id} has a Script Scraper but no script."),
        _ => throw new InvalidOperationException($"Site {site.Id} has an unsupported Scraper kind: {site.Scraper.Kind}."),
    };

    /// <summary>
    /// Records a Site Check as Failed using a fresh DbContext, so that whatever broke the check
    /// (including a failed save) doesn't also break recording the failure. If this save fails
    /// too, the exception propagates and the check stays Checking until it is re-queued as an
    /// orphan.
    /// </summary>
    private async Task MarkFailedAsync(int siteCheckId, Exception exception, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SiteCheckerDbContext>();

        var siteCheck = await dbContext.SiteChecks
            .FirstOrDefaultAsync(sc => sc.Id == siteCheckId, cancellationToken);
        if (siteCheck == null)
        {
            _logger.LogWarning("Site check with ID {SiteCheckId} no longer exists; not marking it failed.", siteCheckId);
            return;
        }

        siteCheck.Update(exception, _timeProvider.GetUtcNow().UtcDateTime);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Resolves the VPN Location the check runs on, or <c>null</c> if it doesn't use the VPN. Called
    /// under the scrape lock. First excludes every location a Known Failure asked to move off, and
    /// rotates if the current location is one of them, or if the configured interval has elapsed.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if an unsupported browser type is encountered.</exception>
    private async Task<PiaLocation?> ResolveVpnLocationAsync(BrowserType browserType, CancellationToken cancellationToken)
    {
        if (browserType is BrowserType.Local or BrowserType.Browserless)
        {
            return null;
        }

        if (browserType != BrowserType.BrowserlessVpn)
        {
            throw new InvalidOperationException($"Unsupported browser type: {browserType}");
        }

        string[] failedLocationIds;
        lock (_failedVpnLocationIdsLock)
        {
            failedLocationIds = [.. _failedVpnLocationIds];
            _failedVpnLocationIds.Clear();
        }
        foreach (var locationId in failedLocationIds)
        {
            _piaService.ExcludeLocation(locationId);
        }

        // Bound to the location that failed, not "whatever is current": if a manual change
        // already moved off it, the request is satisfied and there's no second rotation.
        var current = await _piaService.GetCurrentLocationAsync(cancellationToken);
        if (failedLocationIds.Contains(current.Id)
            || _timeProvider.GetElapsedTime(_lastVpnChangeTimestamp) >= _vpnChangeInterval)
        {
            return await ChangeVpnLocationCoreAsync(excludeCurrent: false, cancellationToken);
        }
        return current;
    }

    /// <summary>
    /// Changes the VPN Location. Called under the scrape lock.
    /// </summary>
    private async Task<PiaLocation> ChangeVpnLocationCoreAsync(bool excludeCurrent, CancellationToken cancellationToken)
    {
        // Before the change, so a failing change waits for the next interval instead of being
        // retried before every check.
        _lastVpnChangeTimestamp = _timeProvider.GetTimestamp();

        var newLocation = await _piaService.ChangeLocationAsync(excludeCurrent, cancellationToken);
        _logger.LogInformation("VPN Location changed to {Location}.", newLocation.Name);
        return newLocation;
    }
}

public static class SiteCheckRunnerExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSiteCheckRunner()
        {
            services.TryAddSingleton(TimeProvider.System);
            return services
                .AddSingleton<SiteCheckRunner>()
                .AddHostedService<SiteCheckQueueProcessor>()
                .AddHostedService<SiteCheckTimer>();
        }
    }
}
