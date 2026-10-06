using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiteChecker.Backend.Extensions;
using SiteChecker.Backend.Services.VPN;
using SiteChecker.Database;
using SiteChecker.Database.Model;
using SiteChecker.Scraper;

namespace SiteChecker.Backend.Services.CheckQueue;

/// <summary>
/// Owns the Site Check lifecycle: decides which Sites are due, accepts requests for Site Checks,
/// and moves each Site Check from Queued to Checking to Done or Failed.
/// </summary>
/// <remarks>
/// The database is the queue (see <c>docs/adr/0001-database-is-the-site-check-queue.md</c>):
/// pending work is every Site Check in the <see cref="CheckStatus.Queued"/> status. The
/// in-memory wake signal only shortens the wait for new work and holds no state.
/// </remarks>
public sealed class SiteCheckRunner : IDisposable
{
    private const string EmptyCheckContent = "[Empty Check]";
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
    /// Records an Empty Check for a Site: a Done Site Check with placeholder content and no
    /// scrape, which resets the baseline the next Site Check is compared against.
    /// </summary>
    /// <param name="siteId">The ID of the Site.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>The Empty Check, or <c>null</c> if the Site does not exist.</returns>
    public async Task<SiteCheck?> RecordEmptyCheckAsync(int siteId, CancellationToken cancellationToken)
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
        siteCheck.Update(new SuccessScrapeResult { Content = EmptyCheckContent }, _timeProvider.GetUtcNow().UtcDateTime);
        dbContext.SiteChecks.Add(siteCheck);
        await dbContext.SaveChangesAsync(cancellationToken);

        return siteCheck;
    }

    /// <summary>
    /// Re-queues orphaned Site Checks, then claims the oldest Queued Site Check and runs it to
    /// Done or Failed. A failed Site Check is recorded on the Site Check, not thrown; this only
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
        // turn a Done check into a Failed one.
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

    public void Dispose() => _createLock.Dispose();

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
            .FirstAsync(sc => sc.Id == siteCheckId, cancellationToken);

        var browserType = _scraperService.GetBrowserType(siteCheck.Site.UseVpn);
        siteCheck.VpnLocationId = await GetVpnLocationAsync(browserType, cancellationToken);

        // Save before scraping so clients receive a real-time status update via SignalR while the
        // (potentially long-running) scrape is in progress. An EF Core SaveChanges interceptor
        // hooks into every save and automatically broadcasts entity changes to all connected clients.
        // The claim set Checking without the interceptor, and a re-run orphan may already hold this
        // location, so force a tracked change to guarantee the broadcast.
        dbContext.Entry(siteCheck).Property(sc => sc.Status).IsModified = true;
        await dbContext.SaveChangesAsync(cancellationToken);

        var request = new ScrapeRequest
        {
            Id = siteCheck.Id,
            ScraperId = siteCheck.Site.ScraperId,
            BrowserType = browserType,
            AlwaysTakeScreenshot = siteCheck.Site.AlwaysTakeScreenshot,
        };

        var result = await _scraperService.ScrapeContentAsync(request);
        siteCheck.Update(result, _timeProvider.GetUtcNow().UtcDateTime);

        if (result.Screenshot is not null)
        {
            var screenshot = new SiteCheckScreenshot(siteCheck, result.Screenshot);
            await dbContext.SiteCheckScreenshots.AddAsync(screenshot, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

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
    /// Resolves the location label used for the current check based on browser mode. If using a
    /// VPN, may change the VPN Location if the configured interval has elapsed.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if an unsupported browser type is encountered.</exception>
    private async Task<string> GetVpnLocationAsync(BrowserType browserType, CancellationToken cancellationToken)
    {
        if (browserType == BrowserType.Local)
        {
            return "Local browser";
        }

        if (browserType == BrowserType.Browserless)
        {
            return "No VPN";
        }

        if (browserType == BrowserType.BrowserlessVpn)
        {
            var location = _timeProvider.GetElapsedTime(_lastVpnChangeTimestamp) >= _vpnChangeInterval
                ? await ChangeVpnRegionAsync(cancellationToken)
                : await _piaService.GetCurrentLocationAsync(cancellationToken);
            return location.Name;
        }

        throw new InvalidOperationException($"Unsupported browser type: {browserType}");
    }

    private async Task<PiaLocation> ChangeVpnRegionAsync(CancellationToken cancellationToken)
    {
        _lastVpnChangeTimestamp = _timeProvider.GetTimestamp();

        var newLocation = await _piaService.ChangeLocationAsync(false, cancellationToken);
        _logger.LogInformation("VPN region changed to {Region}.", newLocation.Name);
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
