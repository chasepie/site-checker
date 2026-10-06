using SiteChecker.Database.Model;

namespace SiteChecker.Backend.Notifiers;

/// <summary>
/// A destination notifications are sent to (a Notification Channel in <c>CONTEXT.md</c>).
/// </summary>
public interface INotificationChannel
{
    /// <summary>
    /// Sends the notification if the Site has this channel enabled for its kind; otherwise does
    /// nothing.
    /// </summary>
    /// <exception cref="Exception">Any failure to deliver is thrown, never swallowed.</exception>
    Task SendAsync(Notification notification, Site site, CancellationToken cancellationToken);
}
