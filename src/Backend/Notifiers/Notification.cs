namespace SiteChecker.Backend.Notifiers;

/// <summary>
/// What a notification is about. Channels use it to pick which of a Site's per-outcome
/// settings apply.
/// </summary>
public enum NotificationKind
{
    /// <summary>A Done Site Check whose content differs from the previous Done Site Check.</summary>
    Updated,

    /// <summary>A Failing Run being reported: its first unexpected failure, or its Known Failures reaching the threshold.</summary>
    Failing,

    /// <summary>A Recovery from a reported Failing Run, with unchanged content.</summary>
    Recovered,

    /// <summary>A Recovery from a reported Failing Run whose content also changed.</summary>
    RecoveredAndUpdated,
}

/// <summary>
/// A channel-neutral notification. Each <see cref="INotificationChannel"/> decides whether the
/// Site wants it on that channel and how to format it.
/// </summary>
public sealed record Notification(
    NotificationKind Kind,
    string Title,
    string Body,
    Uri SiteUrl,
    byte[]? Screenshot);
