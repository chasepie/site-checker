namespace SiteChecker.Domain.Sites;

/// <summary>
/// Pushover message priority levels
/// </summary>
public enum PushoverPriority
{
    /// <summary>
    /// Lowest priority - generates no notification/alert
    /// </summary>
    Lowest = -2,

    /// <summary>
    /// Low priority - always sends as a quiet notification
    /// </summary>
    Low = -1,

    /// <summary>
    /// Normal priority (default)
    /// </summary>
    Normal = 0,

    /// <summary>
    /// High priority - bypasses the user's quiet hours
    /// </summary>
    High = 1,

    /// <summary>
    /// Emergency priority - requires confirmation from the user
    /// </summary>
    Emergency = 2
}
