using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiteChecker.Backend.Notifiers;
using SiteChecker.Database;
using SiteChecker.Database.Model;

namespace SiteChecker.Backend.Services;

/// <summary>
/// Decides whether a completed Site Check warrants a notification, builds it, and sends it to
/// every Notification Channel. The single notification dispatch path; called by the Site Check
/// Runner (see <c>docs/adr/0002-runner-triggers-notifications.md</c>).
/// </summary>
/// <remarks>
/// A Failing Run should be reported once it contains an unexpected failure or its Known
/// Failures reach the Site's Known Failure Threshold. It counts as reported only once a Failing
/// notification actually reached a channel, recorded as <see cref="SiteCheck.ReportedAt"/>; until
/// then, each failure in the run tries again. History is ordered by when checks finished.
/// </remarks>
public sealed class NotifierService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<NotifierService> logger)
{
    private const string NoContent = "[No content]";

    /// <summary>
    /// How long recording a delivered alert may take once shutdown has begun.
    /// </summary>
    private static readonly TimeSpan RecordDeliveryTimeout = TimeSpan.FromSeconds(10);

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<NotifierService> _logger = logger;

    /// <summary>
    /// Sends whatever notification the completed Site Check warrants, if any. Never throws for a
    /// notification problem: failures are logged, and one failing channel doesn't stop the others.
    /// Cancellation may stop it before anything is sent, but once a channel has delivered a
    /// Failing notification, that delivery is recorded even if cancellation follows.
    /// </summary>
    /// <param name="siteCheckId">The ID of a Site Check whose outcome has been saved.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task NotifyAsync(int siteCheckId, CancellationToken cancellationToken)
    {
        try
        {
            // Channels are resolved per call, so typed HttpClients aren't held for the app's lifetime.
            await using var scope = _scopeFactory.CreateAsyncScope();
            var channels = scope.ServiceProvider.GetServices<INotificationChannel>().ToList();
            if (channels.Count == 0)
            {
                return;
            }

            var dbContext = scope.ServiceProvider.GetRequiredService<SiteCheckerDbContext>();

            var siteCheck = await dbContext.SiteChecks
                .AsNoTracking()
                .Include(sc => sc.Site)
                .FirstOrDefaultAsync(sc => sc.Id == siteCheckId, cancellationToken);
            if (siteCheck == null || !siteCheck.IsComplete)
            {
                return;
            }

            var notification = await DecideAsync(dbContext, siteCheck, cancellationToken);
            if (notification == null)
            {
                return;
            }

            // Loaded only once a notification is due: most checks send nothing, and screenshots
            // can be megabytes.
            notification = notification with
            {
                Screenshot = await dbContext.SiteCheckScreenshots
                    .AsNoTracking()
                    .Where(s => s.SiteCheckId == siteCheck.Id)
                    .Select(s => s.Data)
                    .FirstOrDefaultAsync(cancellationToken),
            };

            // SendAsync never throws, so every channel's result arrives even if one is canceled.
            var delivered = await Task.WhenAll(channels.Select(channel =>
                SendAsync(channel, notification, siteCheck.Site, cancellationToken)));

            if (notification.Kind == NotificationKind.Failing && delivered.Any(sent => sent))
            {
                // Not the shutdown token: an alert that was delivered must be recorded, or after a
                // restart the run would alert again and never send its Recovery.
                using var recordDelivery = new CancellationTokenSource(RecordDeliveryTimeout, _timeProvider);
                var reported = await dbContext.SiteChecks.FirstAsync(sc => sc.Id == siteCheck.Id, recordDelivery.Token);
                reported.ReportedAt = _timeProvider.GetUtcNow().UtcDateTime;
                await dbContext.SaveChangesAsync(recordDelivery.Token);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Could not send notifications for site check {SiteCheckId}.", siteCheckId);
        }
    }

    /// <returns>Whether the channel delivered the notification. Never throws: a failed or canceled
    /// send counts as not delivered.</returns>
    private async Task<bool> SendAsync(
        INotificationChannel channel,
        Notification notification,
        Site site,
        CancellationToken cancellationToken)
    {
        try
        {
            return await channel.SendAsync(notification, site, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("{Channel} send of a {Kind} notification for site {SiteId} was canceled.",
                channel.GetType().Name, notification.Kind, site.Id);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Channel} failed to send a {Kind} notification for site {SiteId}.",
                channel.GetType().Name, notification.Kind, site.Id);
            return false;
        }
    }

    /// <returns>The notification to send, or <c>null</c> if the Site Check warrants none.</returns>
    private static async Task<Notification?> DecideAsync(
        SiteCheckerDbContext dbContext,
        SiteCheck siteCheck,
        CancellationToken cancellationToken)
    {
        var site = siteCheck.Site;
        var doneDate = siteCheck.DoneDate!.Value;
        var siteChecks = dbContext.SiteChecks.Where(sc => sc.SiteId == site.Id);

        // History is ordered by when checks finished (DoneDate, then Id), not by when they were
        // created: an Empty Check recorded while a check is open finishes first.
        var finishedBefore = siteChecks.Where(sc =>
            sc.DoneDate < doneDate || (sc.DoneDate == doneDate && sc.Id < siteCheck.Id));

        var previousDone = await finishedBefore
            .Where(sc => sc.Status == CheckStatus.Done)
            .OrderByDescending(sc => sc.DoneDate)
            .ThenByDescending(sc => sc.Id)
            .Select(sc => new { sc.Id, sc.DoneDate, sc.Value })
            .FirstOrDefaultAsync(cancellationToken);

        // The Failing Run before this check: every Failed check that finished since the previous Done.
        var failedRunBefore = finishedBefore.Where(sc => sc.Status == CheckStatus.Failed);
        if (previousDone != null)
        {
            failedRunBefore = failedRunBefore.Where(sc =>
                sc.DoneDate > previousDone.DoneDate
                || (sc.DoneDate == previousDone.DoneDate && sc.Id > previousDone.Id));
        }

        // Counted in the database: a long Failing Run can hold thousands of checks.
        var runBefore = new RunSummary(
            Known: await failedRunBefore.CountAsync(sc => sc.FailureKind == FailureKind.Known, cancellationToken),
            Unexpected: await failedRunBefore.CountAsync(sc => sc.FailureKind != FailureKind.Known, cancellationToken),
            Reported: await failedRunBefore.AnyAsync(sc => sc.ReportedAt != null, cancellationToken));

        if (siteCheck.Status == CheckStatus.Failed)
        {
            var run = runBefore.With(siteCheck.FailureKind);
            if (runBefore.Reported || !run.ShouldBeReported(site.KnownFailuresThreshold))
            {
                return null;
            }

            var body = IsUnexpected(siteCheck.FailureKind) || run.Known < site.KnownFailuresThreshold
                ? Message(siteCheck)
                : $"{run.Known} Known Failures: {Message(siteCheck)}";
            return Build(NotificationKind.Failing, $"{site.Name} Check Failed", body, siteCheck);
        }

        // Done. A Site's first Done check is only a baseline, so it never counts as changed.
        var contentChanged = previousDone != null
            && !string.Equals(previousDone.Value, siteCheck.Value, StringComparison.Ordinal);

        if (runBefore.Reported)
        {
            return contentChanged
                ? Build(NotificationKind.RecoveredAndUpdated, $"{site.Name} Recovered and Updated", Message(siteCheck), siteCheck)
                : Build(NotificationKind.Recovered, $"{site.Name} Recovered", Message(siteCheck), siteCheck);
        }

        return contentChanged
            ? Build(NotificationKind.Updated, $"{site.Name} Updated", Message(siteCheck), siteCheck)
            : null;
    }

    private static bool IsUnexpected(FailureKind? kind) => kind != FailureKind.Known;

    private static string Message(SiteCheck siteCheck) => siteCheck.Value ?? NoContent;

    /// <remarks>The screenshot is attached by <see cref="NotifyAsync"/>.</remarks>
    private static Notification Build(NotificationKind kind, string title, string body, SiteCheck siteCheck)
        => new(kind, SettingsFor(kind), title, body, siteCheck.Site.Url, Screenshot: null);

    /// <summary>
    /// Recoveries use the failure settings so the all-clear reaches you wherever the alert did. A
    /// Recovery that also changed content falls back to the success settings, so the content change
    /// isn't lost when a channel has failures off.
    /// </summary>
    private static NotificationSettings SettingsFor(NotificationKind kind) => kind switch
    {
        NotificationKind.Updated => NotificationSettings.Success,
        NotificationKind.Failing or NotificationKind.Recovered => NotificationSettings.Failure,
        NotificationKind.RecoveredAndUpdated => NotificationSettings.FailureThenSuccess,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// A Failing Run so far: how many Known and unexpected failures it holds, and whether a
    /// Failing notification for it has reached a channel. Checks recorded before Known Failures
    /// were tracked count as unexpected.
    /// </summary>
    private readonly record struct RunSummary(int Known, int Unexpected, bool Reported)
    {
        public RunSummary With(FailureKind? kind)
            => IsUnexpected(kind) ? this with { Unexpected = Unexpected + 1 } : this with { Known = Known + 1 };

        /// <summary>
        /// A Failing Run should be reported once it has an unexpected failure, or its Known
        /// Failures reach the threshold. A run with no failures never should.
        /// </summary>
        public bool ShouldBeReported(int knownFailuresThreshold)
            => Unexpected > 0 || (Known > 0 && Known >= knownFailuresThreshold);
    }
}

public static class NotifierServiceExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddNotifierService()
        {
            services.TryAddSingleton(TimeProvider.System);
            return services.AddSingleton<NotifierService>();
        }
    }
}
