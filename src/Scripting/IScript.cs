namespace SiteChecker.Scripting;

/// <summary>
/// A Script Scraper. A script is a single C# file containing exactly one non-abstract class that
/// implements this interface and has a public parameterless constructor. A new instance is created
/// for every run.
/// </summary>
public interface IScript
{
    /// <summary>
    /// Gets the Site's content from the page the pipeline loaded from the Site's URL.
    /// </summary>
    /// <param name="ctx">The loaded page, its navigation result, and the run's details.</param>
    /// <returns>
    /// The content (a <see cref="string"/> converts implicitly), or
    /// <see cref="ScriptOutcome.KnownFailure"/> for a state the script recognises. Anything the
    /// script throws is recorded as an Unexpected Failure.
    /// </returns>
    Task<ScriptOutcome> RunAsync(ScriptContext ctx);
}
