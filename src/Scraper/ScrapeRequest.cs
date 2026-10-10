using System.Text.Json.Serialization;

namespace SiteChecker.Scraper;

public enum BrowserType
{
    Browserless,
    BrowserlessVpn,
    Local,
}

/// <summary>
/// The Site a scrape is for.
/// </summary>
/// <param name="Id">The Site's ID, or <c>null</c> for a Test Run of a Site that hasn't been created.</param>
/// <param name="Name">The Site's name.</param>
/// <param name="Url">The URL the pipeline navigates to before running the Scraper.</param>
/// <param name="UseVpn">Whether the Site is set to use the VPN.</param>
public sealed record ScrapeSite(int? Id, string Name, Uri Url, bool UseVpn);

/// <summary>
/// The Scraper to run. Each kind of Scraper has its own subtype, run by the
/// <see cref="Executors.IScrapeExecutor"/> for that type, and its own <c>kind</c> in JSON.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ScriptSpec), "script")]
public abstract record ScraperSpec;

/// <summary>
/// A Script Scraper: an uploaded C# file.
/// </summary>
/// <param name="FileName">The uploaded file's name, used in compile errors and stack traces.</param>
/// <param name="Source">The script's source.</param>
/// <param name="SourceHash">The hash the app stored for the source. The executor caches compiles by
/// the source's own hash, which matches it, rather than trusting this one.</param>
public sealed record ScriptSpec(string FileName, string Source, string SourceHash) : ScraperSpec;

/// <summary>
/// One scrape: a Site Check's, or a Test Run's.
/// </summary>
public sealed record ScrapeRequest
{
    /// <summary>
    /// The Site Check being run, or <c>null</c> for a Test Run.
    /// </summary>
    public int? SiteCheckId { get; init; }

    public required ScrapeSite Site { get; init; }

    public required ScraperSpec Scraper { get; init; }

    public BrowserType BrowserType { get; init; } = BrowserType.Browserless;

    /// <summary>
    /// The Site's own timeout, or <c>null</c> for the default (<c>SCRAPE_TIMEOUT</c>).
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    public bool AlwaysTakeScreenshot { get; init; }

    /// <summary>
    /// A Test Run isn't a Site Check: its script isn't cached, and it gets no failure dumps.
    /// </summary>
    [JsonIgnore]
    public bool IsTestRun => SiteCheckId is null;
}
