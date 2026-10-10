using SiteChecker.Scraper.Scripts;
using SiteChecker.Scripting;

namespace SiteChecker.Scraper;

/// <summary>
/// How a scrape ended. Values are pinned so they serialize the same way everywhere.
/// </summary>
public enum ScrapeOutcome
{
    /// <summary>The Scraper produced content.</summary>
    Succeeded = 1,

    /// <summary>The Scraper recognised the state it found, such as access denied or a blank page.</summary>
    KnownFailure = 2,

    /// <summary>Anything the Scraper didn't recognise: an error, a timeout or a broken page.</summary>
    UnexpectedFailure = 3,
}

/// <summary>
/// The outcome of running a Scraper through the pipeline.
/// </summary>
public sealed record ScrapeResult
{
    public required ScrapeOutcome Outcome { get; init; }

    /// <summary>
    /// The Site's content, when the scrape <see cref="ScrapeOutcome.Succeeded"/>.
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// What went wrong, when the scrape failed.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// What a Known Failure asks the Site Check Runner to do. An Unexpected Failure never carries any.
    /// </summary>
    public IReadOnlyList<RequestedAction> RequestedActions { get; init; } = [];

    /// <summary>
    /// The compile errors behind an Unexpected Failure, when the script didn't compile.
    /// </summary>
    public IReadOnlyList<ScriptDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>
    /// The exception behind an Unexpected Failure, if there was one.
    /// </summary>
    public Exception? Exception { get; init; }

    public byte[]? Screenshot { get; init; }

    /// <summary>
    /// How long the scrape took, from opening the browser to the last artifact.
    /// </summary>
    public TimeSpan Duration { get; init; }

    public static ScrapeResult Succeeded(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new() { Outcome = ScrapeOutcome.Succeeded, Content = content };
    }

    public static ScrapeResult KnownFailure(string message, IReadOnlyList<RequestedAction>? requestedActions = null)
        => new() { Outcome = ScrapeOutcome.KnownFailure, Message = message, RequestedActions = requestedActions ?? [] };

    public static ScrapeResult Unexpected(string message, Exception? exception = null)
        => new() { Outcome = ScrapeOutcome.UnexpectedFailure, Message = message, Exception = exception };

    public static ScrapeResult Unexpected(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Unexpected(exception.Message, exception);
    }
}
