using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace SiteChecker.Scripting;

/// <summary>
/// What a script gets for one run.
/// </summary>
public sealed class ScriptContext
{
    /// <summary>
    /// The page, already navigated to the Site's URL. A script may navigate elsewhere.
    /// </summary>
    public required IPage Page { get; init; }

    /// <summary>
    /// How navigating to the Site's URL went. Navigation errors don't fail the run; the script
    /// decides what they mean, or calls <see cref="NavigationResult.EnsureSucceeded"/>.
    /// </summary>
    public required NavigationResult Navigation { get; init; }

    /// <summary>
    /// Cancelled when the Site's timeout is reached. The run ends at the timeout whether or not
    /// the script observes it; checking it lets the script stop cleanly.
    /// </summary>
    public required CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// The Site being scraped.
    /// </summary>
    public required ScriptSite Site { get; init; }

    /// <summary>
    /// A logger scoped to the Site and Site Check.
    /// </summary>
    public required ILogger Logger { get; init; }
}

/// <summary>
/// The read-only details of the Site a script is running for.
/// </summary>
/// <param name="Name">The Site's name.</param>
/// <param name="Url">The URL the pipeline navigated to.</param>
/// <param name="UsesVpn">Whether the page is loaded through the VPN.</param>
public sealed record ScriptSite(string Name, Uri Url, bool UsesVpn);
