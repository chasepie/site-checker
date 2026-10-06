using Microsoft.EntityFrameworkCore;
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
/// Whether a Failing Run has been reported is derived from the Site's check history on every
/// call, never stored: a run is reported once it contains an unexpected failure or its Known
/// Failures reach the Site's Known Failure Threshold.
/// </remarks>
public sealed class NotifierService(
    IServiceScopeFactory scopeFactory,
    ILogger<NotifierService> logger)
{
    private const string NoContent = "[No content]";

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<NotifierService> _logger = logger;

    /// <summary>
    /// Sends whatever notification the completed Site Check warrants, if any. Never throws for a
    /// notification problem: failures are logged, and one failing channel doesn't stop the others.
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

            await Task.WhenAll(channels.Select(channel =>
                SendAsync(channel, notification, siteCheck.Site, cancellationToken)));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Could not send notifications for site check {SiteCheckId}.", siteCheckId);
        }
    }

    private async Task SendAsync(
        INotificationChannel channel,
        Notification notification,
        Site site,
        CancellationToken cancellationToken)
    {
        try
        {
            await channel.SendAsync(notification, site, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "{Channel} failed to send a {Kind} notification for site {SiteId}.",
                channel.GetType().Name, notification.Kind, site.Id);
        }
    }

    /// <returns>The notification to send, or <c>null</c> if the Site Check warrants none.</returns>
    private static async Task<Notification?> DecideAsync(
        SiteCheckerDbContext dbContext,
        SiteCheck siteCheck,
        CancellationToken cancellationToken)
    {
        var site = siteCheck.Site;

        // The Failing Run before this check: every Failed check since the Site's last Done.
        var previousDone = await dbContext.SiteChecks
            .AsNoTracking()
            .Where(sc => sc.SiteId == site.Id
                && sc.Id < siteCheck.Id
                && sc.Status == CheckStatus.Done)
            .OrderByDescending(sc => sc.Id)
            .Select(sc => new { sc.Id, sc.Value })
            .FirstOrDefaultAsync(cancellationToken);
        var previousDoneId = previousDone?.Id ?? 0;

        // Counted in the database: a long Failing Run can hold thousands of checks.
        var failedRunBefore = dbContext.SiteChecks
            .Where(sc => sc.SiteId == site.Id
                && sc.Id > previousDoneId
                && sc.Id < siteCheck.Id
                && sc.Status == CheckStatus.Failed);
        var runBefore = new RunSummary(
            Known: await failedRunBefore.CountAsync(sc => sc.FailureKind == FailureKind.Known, cancellationToken),
            Unexpected: await failedRunBefore.CountAsync(sc => sc.FailureKind != FailureKind.Known, cancellationToken));
        var runBeforeWasReported = runBefore.IsReported(site.KnownFailuresThreshold);

        if (siteCheck.Status == CheckStatus.Failed)
        {
            var run = runBefore.With(siteCheck.FailureKind);
            if (runBeforeWasReported || !run.IsReported(site.KnownFailuresThreshold))
            {
                return null;
            }

            var body = IsUnexpected(siteCheck.FailureKind)
                ? Message(siteCheck)
                : $"{run.Known} Known Failures: {Message(siteCheck)}";
            return Build(NotificationKind.Failing, $"{site.Name} Check Failed", body, siteCheck);
        }

        // Done. A Site's first Done check is only a baseline, so it never counts as changed.
        var contentChanged = previousDone != null
            && !string.Equals(previousDone.Value, siteCheck.Value, StringComparison.Ordinal);

        if (runBeforeWasReported)
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
        => new(kind, title, body, siteCheck.Site.Url, Screenshot: null);

    /// <summary>
    /// How many Known and unexpected failures a Failing Run holds. Checks recorded before Known
    /// Failures were tracked count as unexpected.
    /// </summary>
    private readonly record struct RunSummary(int Known, int Unexpected)
    {
        public RunSummary With(FailureKind? kind)
            => IsUnexpected(kind) ? this with { Unexpected = Unexpected + 1 } : this with { Known = Known + 1 };

        /// <summary>
        /// A Failing Run is reported by its first unexpected failure, or when its Known Failures
        /// reach the threshold. A run with no failures is never reported.
        /// </summary>
        public bool IsReported(int knownFailuresThreshold)
            => Unexpected > 0 || (Known > 0 && Known >= knownFailuresThreshold);
    }
}

public static class NotifierServiceExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddNotifierService()
        {
            return services.AddSingleton<NotifierService>();
        }
    }
}
