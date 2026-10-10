using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace SiteChecker.Scraper;

/// <summary>
/// The secret the app sends to the Scrape Worker as <c>Authorization: Bearer {secret}</c>
/// (<c>SCRAPE_WORKER_SECRET</c>). The worker runs whatever script a request carries, so without it,
/// anything that can reach the worker (such as a page loaded in Browserless) could run code there
/// without the admin token. Both sides require it outside Development.
/// </summary>
public static class ScrapeWorkerSecret
{
    public const string Key = "SCRAPE_WORKER_SECRET";

    /// <summary>
    /// The configured secret, trimmed, or <c>null</c> when it isn't set.
    /// </summary>
    public static string? Read(IConfiguration configuration)
        => configuration[Key]?.Trim() is { Length: > 0 } secret ? secret : null;

    /// <summary>
    /// Whether <paramref name="candidate"/> is <paramref name="secret"/>, compared in constant time.
    /// Hashing both sides first keeps the comparison from revealing the secret's length.
    /// </summary>
    public static bool Matches(string secret, string? candidate)
        => candidate is not null
            && CryptographicOperations.FixedTimeEquals(Hash(secret), Hash(candidate));

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
