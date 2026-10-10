using System.Security.Cryptography;
using System.Text;

namespace SiteChecker.Backend.Services.Security;

/// <summary>
/// The password that logs in to the app (<c>ADMIN_PASSWORD</c>). It's required outside Development;
/// in Development, leaving it unset turns login off.
/// </summary>
/// <remarks>
/// Constructing it throws <see cref="InvalidOperationException"/> when the password isn't set
/// outside Development, so startup fails.
/// </remarks>
public sealed class AdminPassword(IConfiguration configuration, IHostEnvironment environment, ILogger<AdminPassword> logger)
{
    public const string AdminPasswordKey = "ADMIN_PASSWORD";

    /// <summary>
    /// The session claim holding <see cref="SessionStamp"/>.
    /// </summary>
    public const string SessionStampClaim = "session-stamp";

    private readonly byte[]? _passwordHash = HashConfiguredPassword(configuration, environment, logger);

    /// <summary>
    /// Whether logging in is required. Only Development runs without a password.
    /// </summary>
    public bool IsRequired => _passwordHash is not null;

    /// <summary>
    /// Whether <paramref name="candidate"/> is the password, compared in constant time. Hashing both
    /// sides first keeps the comparison from revealing the password's length.
    /// </summary>
    public bool Matches(string candidate)
        => _passwordHash is not null && CryptographicOperations.FixedTimeEquals(_passwordHash, Hash(candidate));

    /// <summary>
    /// A value derived from the password, put in every session when it's created, so changing the
    /// password ends every session made with the old one (<see cref="IsCurrentSessionStamp"/>). The
    /// session cookie is encrypted, so the stamp isn't readable outside the app. <c>null</c> when no
    /// password is required.
    /// </summary>
    public string? SessionStamp => _passwordHash is null
        ? null
        : Convert.ToBase64String(SHA256.HashData([.. "SiteChecker session stamp\n"u8, .. _passwordHash]));

    /// <summary>
    /// Whether a session's stamp was made with the current password.
    /// </summary>
    public bool IsCurrentSessionStamp(string? stamp)
        => SessionStamp is { } current
            && stamp is not null
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(current), Encoding.UTF8.GetBytes(stamp));

    /// <summary>
    /// The configured password's hash, or <c>null</c> when Development leaves it unset. Trimmed, like
    /// the login page trims what it's given, so stray whitespace in <c>.env</c> can't lock you out.
    /// </summary>
    private static byte[]? HashConfiguredPassword(IConfiguration configuration, IHostEnvironment environment, ILogger logger)
    {
        var password = configuration[AdminPasswordKey]?.Trim();
        if (!string.IsNullOrEmpty(password))
        {
            return Hash(password);
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"{AdminPasswordKey} must be set. It's the password for logging in to the app.");
        }

        logger.LogWarning("{Key} isn't set, so login is off and anyone who can reach the app can use it.", AdminPasswordKey);
        return null;
    }

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
