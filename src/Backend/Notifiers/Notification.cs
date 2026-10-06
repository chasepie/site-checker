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
/// Which of a Site's per-outcome channel settings a notification uses.
/// </summary>
public enum NotificationSettings
{
    /// <summary>The Site's success settings.</summary>
    Success,

    /// <summary>The Site's failure settings.</summary>
    Failure,

    /// <summary>
    /// The failure settings, or the success settings if the channel is off for failures, so a
    /// content change isn't lost.
    /// </summary>
    FailureThenSuccess,
}

/// <summary>
/// A channel-neutral notification. Each <see cref="INotificationChannel"/> decides whether the
/// Site wants it on that channel and how to format it.
/// </summary>
public sealed record Notification(
    NotificationKind Kind,
    NotificationSettings Settings,
    string Title,
    string Body,
    Uri SiteUrl,
    byte[]? Screenshot)
{
    /// <summary>
    /// <see cref="SiteUrl"/> as a link for a channel: escaped when absolute (unlike
    /// <see cref="Uri.ToString"/>), and as entered when relative (where
    /// <see cref="Uri.AbsoluteUri"/> would throw).
    /// </summary>
    public string SiteLink => SiteUrl.IsAbsoluteUri ? SiteUrl.AbsoluteUri : SiteUrl.OriginalString;
}
