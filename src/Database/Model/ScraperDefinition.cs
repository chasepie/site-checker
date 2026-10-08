using System.Text.Json.Serialization;

namespace SiteChecker.Database.Model;

/// <summary>
/// The kinds of Scraper. Values are pinned because they are persisted.
/// </summary>
public enum ScraperKind
{
    /// <summary>An uploaded C# file.</summary>
    Script = 1,
}

/// <summary>
/// A Site's Scraper: which kind it is, and that kind's payload. Exactly one payload is set, and it
/// matches <see cref="Kind"/>. It's flat rather than a class hierarchy because EF Core complex
/// types don't support inheritance (see <c>docs/adr/0003-scrapers-are-data.md</c>).
/// </summary>
public class ScraperDefinition
{
    public ScraperKind Kind { get; set; } = ScraperKind.Script;

    /// <summary>
    /// Set when <see cref="Kind"/> is <see cref="ScraperKind.Script"/>.
    /// </summary>
    public ScriptScraper? Script { get; set; }
}

/// <summary>
/// A Script Scraper's metadata. The source is stored separately, in <see cref="SiteScript"/>.
/// </summary>
public class ScriptScraper
{
    /// <summary>
    /// The name of the uploaded <c>.cs</c> file.
    /// </summary>
    public required string FileName { get; set; }

    /// <summary>
    /// Identifies the source, so the compiled script is reused while it's unchanged.
    /// </summary>
    public required string SourceHash { get; set; }

    public required DateTime UploadedAt { get; set; }
}

/// <summary>
/// A Script Scraper's source, one row per Site, deleted with its Site. It's kept off
/// <see cref="Site"/> so it stays out of Site lists, and it deliberately isn't an
/// <see cref="IEntityWithId"/>, so the save interceptor never broadcasts it.
/// </summary>
public class SiteScript
{
    public int SiteId { get; set; }

    public required string Source { get; set; }

    [JsonIgnore]
    public Site Site { get; set; } = null!;
}
