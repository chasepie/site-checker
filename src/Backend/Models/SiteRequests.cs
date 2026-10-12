using System.Text.Json.Serialization;
using SiteChecker.Database.Model;
using SiteChecker.Scraper;
using SiteChecker.Scraper.Scripts;
using ScriptAction = SiteChecker.Scripting.RequestedAction;

namespace SiteChecker.Backend.Models;

/// <summary>
/// A Site's settings and Scraper, as the create and update endpoints take them.
/// </summary>
public sealed class SiteRequest : SiteUpdate
{
    public required ScraperRequest Scraper { get; set; }
}

/// <summary>
/// A Scraper as the client sends it: its kind, and that kind's payload.
/// </summary>
public sealed class ScraperRequest
{
    public ScraperKind Kind { get; set; } = ScraperKind.Script;

    /// <summary>
    /// The uploaded script. On an update, <c>null</c> keeps the Site's current script.
    /// </summary>
    public ScriptUpload? Script { get; set; }
}

/// <summary>
/// A <c>.cs</c> file the browser read as text.
/// </summary>
public sealed class ScriptUpload
{
    public required string FileName { get; set; }

    public required string Source { get; set; }
}

/// <summary>
/// Why a Site or a Test Run was rejected: problems with its settings, and the script's compile
/// errors.
/// </summary>
public sealed class SiteValidationResult
{
    public List<string> Errors { get; set; } = [];

    public List<ScriptDiagnostic> Diagnostics { get; set; } = [];

    [JsonIgnore]
    public bool IsValid => Errors.Count == 0 && Diagnostics.Count == 0;
}

/// <summary>
/// A Test Run of an unsaved Scraper and the Site settings it depends on. The client chooses the
/// Test Run's ID and gives its SignalR connection, which alone receives the result.
/// </summary>
public sealed class TestRunRequest
{
    public required string TestRunId { get; set; }

    public required string ConnectionId { get; set; }

    /// <summary>
    /// The Site's name, if it has one yet; used only in logs.
    /// </summary>
    public string? Name { get; set; }

    public required Uri Url { get; set; }

    public bool UseVpn { get; set; }

    public bool AlwaysTakeScreenshot { get; set; }

    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// The Scraper to test, with its source.
    /// </summary>
    public required ScraperRequest Scraper { get; set; }
}

/// <summary>
/// The outcome of a Test Run, sent to the connection that started it.
/// </summary>
public sealed class TestRunResult
{
    public required string TestRunId { get; set; }

    public required ScrapeOutcome Outcome { get; set; }

    public string? Content { get; set; }

    public string? Message { get; set; }

    /// <summary>
    /// What a Known Failure asked for. A Test Run never carries them out.
    /// </summary>
    public List<RequestedAction> RequestedActions { get; set; } = [];

    public List<ScriptDiagnostic> Diagnostics { get; set; } = [];

    public byte[]? Screenshot { get; set; }

    public int DurationMilliseconds { get; set; }

    public static TestRunResult From(string testRunId, ScrapeResult result) => new()
    {
        TestRunId = testRunId,
        Outcome = result.Outcome,
        Content = result.Content,
        Message = result.Message,
        // A value the contract doesn't define is ignored, as a Site Check ignores it, rather than
        // turning the Test Run's real outcome into an error.
        RequestedActions = [.. result.RequestedActions.Select(ToRecorded).OfType<RequestedAction>()],
        Diagnostics = [.. result.Diagnostics],
        Screenshot = result.Screenshot,
        DurationMilliseconds = (int)result.Duration.TotalMilliseconds,
    };

    private static RequestedAction? ToRecorded(ScriptAction action) => action switch
    {
        ScriptAction.ChangeVpnLocation => RequestedAction.ChangeVpnLocation,
        ScriptAction.Retry => RequestedAction.Retry,
        _ => null,
    };
}
