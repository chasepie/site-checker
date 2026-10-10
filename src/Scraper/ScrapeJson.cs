using System.Text.Json;
using System.Text.Json.Serialization;

namespace SiteChecker.Scraper;

/// <summary>
/// How a <see cref="ScrapeRequest"/> and its <see cref="ScrapeResult"/> are written as JSON, the
/// same way on both sides of a process boundary.
/// </summary>
public static class ScrapeJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() },
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

/// <summary>
/// The Scrape Worker's routes, relative to its base address, so the worker and the app's client
/// can't drift apart.
/// </summary>
public static class ScrapeWorkerPaths
{
    /// <summary><c>POST</c> a <see cref="ScrapeRequest"/>; returns its <see cref="ScrapeResult"/>.</summary>
    public const string Scrape = "scrape";

    /// <summary><c>DELETE {Scripts}/{siteId}</c> unloads a Site's compiled script.</summary>
    public const string Scripts = "scripts";

    /// <summary><c>GET</c>; unhealthy while a run abandoned at its timeout is still going.</summary>
    public const string Health = "healthz";
}
