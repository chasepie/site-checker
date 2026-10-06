using SiteChecker.Database.Model;

namespace SiteChecker.Backend.Notifiers;

/// <summary>
/// A destination notifications are sent to (a Notification Channel in <c>CONTEXT.md</c>).
/// </summary>
public interface INotificationChannel
{
    /// <summary>
    /// Sends the notification if the Site has this channel enabled for the notification's
    /// <see cref="Notification.Settings"/>; otherwise does nothing.
    /// </summary>
    /// <returns><c>true</c> if the notification was delivered; <c>false</c> if the Site has this channel off.</returns>
    /// <exception cref="Exception">Any failure to deliver is thrown, never swallowed.</exception>
    Task<bool> SendAsync(Notification notification, Site site, CancellationToken cancellationToken);
}
