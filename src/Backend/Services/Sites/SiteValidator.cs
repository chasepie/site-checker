using SiteChecker.Backend.Models;
using SiteChecker.Database.Model;
using SiteChecker.Scraper;
using SiteChecker.Scraper.Scripts;

namespace SiteChecker.Backend.Services.Sites;

/// <summary>
/// Checks a Site's settings and Scraper before they're saved or test-run: the URL is an absolute
/// http(s) URL the pipeline can navigate to, the Scraper's payload matches its kind, its script
/// compiles with the runtime compiler, and its timeout fits under Browserless's.
/// </summary>
public sealed class SiteValidator(IScriptCompiler compiler, ScrapeTimeouts timeouts)
{
    private readonly IScriptCompiler _compiler = compiler;
    private readonly ScrapeTimeouts _timeouts = timeouts;

    /// <param name="request">The Site to create or update.</param>
    /// <param name="existing">The Site being updated, with its script loaded, or <c>null</c> for a new Site.</param>
    public SiteValidationResult ValidateSite(SiteRequest request, Site? existing)
    {
        var result = new SiteValidationResult();
        ValidateUrl(request.Url, result);
        ValidateTimeout(request.TimeoutSeconds, result);
        // Only a Site that already has a script can keep it.
        ValidateScraper(request.Scraper, scriptRequired: existing?.SiteScript is null, result);
        return result;
    }

    public SiteValidationResult ValidateTestRun(TestRunRequest request)
    {
        var result = new SiteValidationResult();
        ValidateUrl(request.Url, result);
        ValidateTimeout(request.TimeoutSeconds, result);
        ValidateScraper(request.Scraper, scriptRequired: true, result);
        return result;
    }

    /// <summary>
    /// JSON binds a relative URL such as <c>example.com</c> without complaint, but the pipeline can't
    /// navigate to it, so every check would fail.
    /// </summary>
    private static void ValidateUrl(Uri url, SiteValidationResult result)
    {
        if (!url.IsAbsoluteUri || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            result.Errors.Add("The URL must be an absolute URL starting with http:// or https://.");
        }
    }

    private void ValidateTimeout(int? timeoutSeconds, SiteValidationResult result)
    {
        if (timeoutSeconds is { } seconds && (seconds < 1 || seconds > _timeouts.MaxSeconds))
        {
            result.Errors.Add(
                $"The timeout must be between 1 and {_timeouts.MaxSeconds} seconds, to leave room for the screenshot before Browserless ends the session.");
        }
    }

    private void ValidateScraper(ScraperRequest scraper, bool scriptRequired, SiteValidationResult result)
    {
        if (scraper.Kind != ScraperKind.Script)
        {
            result.Errors.Add($"Unsupported Scraper kind '{scraper.Kind}'.");
            return;
        }

        if (scraper.Script is not { } script)
        {
            if (scriptRequired)
            {
                result.Errors.Add("A Script Scraper needs a script file.");
            }
            return;
        }

        if (!script.FileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add("The script must be a .cs file.");
        }

        if (string.IsNullOrWhiteSpace(script.Source))
        {
            result.Errors.Add("The script file is empty.");
            return;
        }

        result.Diagnostics.AddRange(_compiler.Validate(script.Source, script.FileName));
    }
}
