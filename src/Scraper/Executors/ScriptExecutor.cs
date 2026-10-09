using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using SiteChecker.Scraper.Scripts;
using SiteChecker.Scripting;

namespace SiteChecker.Scraper.Executors;

/// <summary>
/// Runs one kind of Scraper on the page the pipeline loaded.
/// </summary>
public interface IScrapeExecutor
{
    /// <summary>
    /// The <see cref="ScraperSpec"/> subtype this executor runs.
    /// </summary>
    Type SpecType { get; }

    /// <summary>
    /// Returns <see cref="ScrapeOutcome.Succeeded"/> or a <see cref="ScrapeOutcome.KnownFailure"/>.
    /// Anything it throws is an Unexpected Failure; the pipeline converts it.
    /// </summary>
    Task<ScrapeResult> ExecuteAsync(ExecutorContext context);
}

/// <summary>
/// What an executor gets: the loaded page, how navigating to it went, the request, the log the
/// Scraper's own logging should be recorded in, and a token that's cancelled at the Site's timeout.
/// </summary>
public sealed record ExecutorContext(
    IPage Page,
    NavigationResult Navigation,
    ScrapeRequest Request,
    ScraperLog Log,
    CancellationToken CancellationToken);

/// <summary>
/// Runs Script Scrapers. A Site Check reuses the Site's cached compile; a Test Run compiles its
/// own copy into a separate load context and unloads it when the run ends.
/// </summary>
public sealed class ScriptExecutor(
    ScriptCache cache,
    IScriptCompiler compiler,
    ILoggerFactory loggerFactory) : IScrapeExecutor
{
    private readonly ScriptCache _cache = cache;
    private readonly IScriptCompiler _compiler = compiler;
    private readonly ILogger _scriptLogger = loggerFactory.CreateLogger("SiteChecker.Script");

    public Type SpecType => typeof(ScriptSpec);

    public async Task<ScrapeResult> ExecuteAsync(ExecutorContext context)
    {
        var request = context.Request;
        var spec = (ScriptSpec)request.Scraper;

        ScriptCompileResult compiled;
        bool ownsScript;
        if (!request.IsTestRun && request.Site.Id is { } siteId)
        {
            compiled = _cache.GetOrCompile(siteId, spec.SourceHash, spec.Source, spec.FileName);
            ownsScript = false;
        }
        else
        {
            compiled = _compiler.Compile(spec.Source, spec.FileName);
            ownsScript = true;
        }

        if (!compiled.Succeeded)
        {
            var first = compiled.Errors[0];
            var more = compiled.Errors.Count > 1 ? $" (and {compiled.Errors.Count - 1} more)" : string.Empty;
            return ScrapeResult.Unexpected($"The script doesn't compile: {first.FileName}({first.Line},{first.Column}): {first.Message}{more}")
                with { Diagnostics = compiled.Errors };
        }

        try
        {
            using var scope = _scriptLogger.BeginScope(new Dictionary<string, object?>
            {
                ["SiteId"] = request.Site.Id,
                ["SiteName"] = request.Site.Name,
                ["SiteCheckId"] = request.SiteCheckId,
            });

            var outcome = await compiled.Script.CreateInstance().RunAsync(new ScriptContext
            {
                Page = context.Page,
                Navigation = context.Navigation,
                CancellationToken = context.CancellationToken,
                Site = new ScriptSite(request.Site.Name, request.Site.Url, request.BrowserType == BrowserType.BrowserlessVpn),
                Logger = context.Log.Wrap(_scriptLogger),
            });

            if (outcome is null)
            {
                throw new InvalidOperationException("The script returned no outcome.");
            }

            return outcome.IsKnownFailure
                ? ScrapeResult.KnownFailure(outcome.KnownFailureMessage!, outcome.RequestedActions)
                : ScrapeResult.Succeeded(outcome.Content!);
        }
        finally
        {
            if (ownsScript)
            {
                compiled.Script.Dispose();
            }
        }
    }
}
