using System.Security.Cryptography;
using System.Text;

namespace SiteChecker.Backend.Services.Security;

/// <summary>
/// The token that gates uploading and running scripts (<c>ADMIN_TOKEN</c>). It's required outside
/// Development; in Development, leaving it unset turns the gate off.
/// </summary>
public sealed class AdminToken
{
    public const string AdminTokenKey = "ADMIN_TOKEN";

    /// <summary>
    /// The authorization policy for actions that upload or run a script.
    /// </summary>
    public const string PolicyName = "Admin";

    public const string SchemeName = "AdminToken";

    private readonly byte[]? _tokenHash;

    /// <exception cref="InvalidOperationException">The token isn't set outside Development.</exception>
    public AdminToken(IConfiguration configuration, IHostEnvironment environment, ILogger<AdminToken> logger)
    {
        var token = configuration[AdminTokenKey];
        if (string.IsNullOrWhiteSpace(token))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"{AdminTokenKey} must be set. It's the token the UI asks for before saving a Site or starting a Test Run, because both run code on the host.");
            }

            logger.LogWarning("{Key} isn't set, so anyone who can reach the app can upload and run scripts.", AdminTokenKey);
            return;
        }

        _tokenHash = Hash(token);
    }

    /// <summary>
    /// Whether a token is required. Only Development runs without one.
    /// </summary>
    public bool IsRequired => _tokenHash is not null;

    /// <summary>
    /// Whether <paramref name="candidate"/> is the token, compared in constant time. Hashing both
    /// sides first keeps the comparison from revealing the token's length.
    /// </summary>
    public bool Matches(string candidate)
        => _tokenHash is not null && CryptographicOperations.FixedTimeEquals(_tokenHash, Hash(candidate));

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
