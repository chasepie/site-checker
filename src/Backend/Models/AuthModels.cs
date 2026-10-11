namespace SiteChecker.Backend.Models;

/// <summary>
/// A login attempt.
/// </summary>
public sealed class LoginRequest
{
    public required string Password { get; set; }
}

/// <summary>
/// Whether this browser is logged in, and whether it needs to be.
/// </summary>
public sealed class SessionInfo
{
    /// <summary>
    /// <c>false</c> only in Development without <c>ADMIN_PASSWORD</c>, where login is off.
    /// </summary>
    public required bool LoginRequired { get; set; }

    /// <summary>
    /// Whether the app can be used: logged in, or login is off.
    /// </summary>
    public required bool LoggedIn { get; set; }
}
