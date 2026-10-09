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
