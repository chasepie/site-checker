using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SiteChecker.Scraper;

/// <summary>
/// How long a scrape may run. Each Site may set its own timeout, which falls back to
/// <c>SCRAPE_TIMEOUT</c> (seconds, default 120). Browserless ends a session at its own
/// <c>TIMEOUT</c>, which the app reads from <c>BROWSERLESS_TIMEOUT</c> (milliseconds, default
/// 180000), so every timeout has to leave room under it for the failure screenshot.
/// </summary>
public sealed class ScrapeTimeouts
{
    public const string ScrapeTimeoutKey = "SCRAPE_TIMEOUT";
    public const string BrowserlessTimeoutKey = "BROWSERLESS_TIMEOUT";

    /// <summary>
    /// The time allowed, after the timeout, for the screenshot and the failure dumps.
    /// </summary>
    public static readonly TimeSpan ArtifactBudget = TimeSpan.FromSeconds(10);

    private const int DefaultScrapeTimeoutSeconds = 120;
    private const int DefaultBrowserlessTimeoutMilliseconds = 180_000;

    private readonly ILogger<ScrapeTimeouts> _logger;

    /// <exception cref="InvalidOperationException">The configured timeouts are invalid or don't fit.</exception>
    public ScrapeTimeouts(IConfiguration configuration, ILogger<ScrapeTimeouts> logger)
    {
        _logger = logger;

        BrowserlessTimeout = TimeSpan.FromMilliseconds(
            ReadPositiveInt(configuration, BrowserlessTimeoutKey, DefaultBrowserlessTimeoutMilliseconds));
        Max = BrowserlessTimeout - ArtifactBudget;
        if (Max < TimeSpan.FromSeconds(1))
        {
            throw new InvalidOperationException(
                $"{BrowserlessTimeoutKey} ({BrowserlessTimeout.TotalMilliseconds:0} ms) must be more than {ArtifactBudget.TotalSeconds:0} s, to leave room for a scrape and its screenshot.");
        }

        Default = TimeSpan.FromSeconds(ReadPositiveInt(configuration, ScrapeTimeoutKey, DefaultScrapeTimeoutSeconds));
        if (Default > Max)
        {
            throw new InvalidOperationException(
                $"{ScrapeTimeoutKey} ({Default.TotalSeconds:0} s) must leave {ArtifactBudget.TotalSeconds:0} s under {BrowserlessTimeoutKey} ({BrowserlessTimeout.TotalMilliseconds:0} ms) for the failure screenshot, so it can be at most {MaxSeconds} s.");
        }
    }

    /// <summary>
    /// The timeout for Sites that don't set their own.
    /// </summary>
    public TimeSpan Default { get; }

    /// <summary>
    /// The longest timeout that leaves room under <see cref="BrowserlessTimeout"/> for the screenshot.
    /// </summary>
    public TimeSpan Max { get; }

    public int MaxSeconds => (int)Max.TotalSeconds;

    public TimeSpan BrowserlessTimeout { get; }

    /// <summary>
    /// The timeout a scrape runs under: the Site's own, or the default. A stored timeout that no
    /// longer fits (because <c>BROWSERLESS_TIMEOUT</c> was lowered) is clamped to <see cref="Max"/>.
    /// </summary>
    public TimeSpan Resolve(TimeSpan? siteTimeout)
    {
        if (siteTimeout is not { } timeout)
        {
            return Default;
        }

        if (timeout > Max)
        {
            _logger.LogWarning(
                "A Site timeout of {Timeout} s doesn't fit under {Key}; using {Max} s instead.",
                timeout.TotalSeconds, BrowserlessTimeoutKey, MaxSeconds);
            return Max;
        }
        return timeout;
    }

    private static int ReadPositiveInt(IConfiguration configuration, string key, int defaultValue)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            throw new InvalidOperationException($"{key} must be a positive whole number, but is '{value}'.");
        }
        return parsed;
    }
}
