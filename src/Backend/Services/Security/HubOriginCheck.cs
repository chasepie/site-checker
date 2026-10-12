using Microsoft.AspNetCore.HostFiltering;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace SiteChecker.Backend.Services.Security;

/// <summary>
/// Refuses a browser's connection to a SignalR hub from a page with another origin. WebSockets
/// aren't covered by CORS, and the session cookie is <c>SameSite=Strict</c>, which still lets a
/// "same site" page open the hub with it and read live data: another Tailscale machine under
/// <c>ts.net</c>, or another app on this host at another port, since cookies ignore ports.
/// <para>
/// Browsers send <c>Sec-Fetch-Site</c>, which they work out from the URLs they see, so it holds
/// behind a proxy that rewrites the Host header. Where it's missing, the <c>Origin</c> host must
/// be an allowed host, which can't tell ports apart. A request with neither isn't from a browser
/// page, so it's left to the login.
/// </para>
/// </summary>
public static class HubOriginCheck
{
    extension(IApplicationBuilder app)
    {
        /// <summary>
        /// Answers 403 to requests under <paramref name="hubPath"/> that a browser marks as not
        /// <c>same-origin</c>, or, without that mark, whose <c>Origin</c> host isn't one of the
        /// allowed hosts. The host check does nothing while every host is allowed (Development).
        /// </summary>
        public IApplicationBuilder UseHubOriginCheck(PathString hubPath)
        {
            var hostFiltering = app.ApplicationServices.GetRequiredService<IOptions<HostFilteringOptions>>();
            // Built on first use rather than per request; startup has already checked the setting.
            var allowedHosts = new Lazy<StringSegment[]?>(() => AllowedHostPatterns(hostFiltering.Value.AllowedHosts));
            return app.UseWhen(
                context => context.Request.Path.StartsWithSegments(hubPath),
                branch => branch.Use(async (context, next) =>
                {
                    var fetchSite = context.Request.Headers["Sec-Fetch-Site"].ToString();
                    var origin = context.Request.Headers.Origin.ToString();
                    // The Origin check is only the fallback: behind a proxy that rewrites the Host
                    // header, the page's origin isn't an allowed host, but the browser still says
                    // same-origin.
                    var allowed = fetchSite.Length > 0
                        ? fetchSite == "same-origin"
                        : origin.Length == 0 || IsAllowedOrigin(origin, allowedHosts.Value);
                    if (!allowed)
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return;
                    }
                    await next(context);
                }));
        }
    }

    /// <summary>
    /// The allowed hosts as <see cref="HostString.MatchesAny"/> patterns, or <c>null</c> when every
    /// host is allowed. Converted to punycode, as <c>HostFilteringMiddleware</c> converts them, since
    /// browsers send an internationalized name that way.
    /// </summary>
    private static StringSegment[]? AllowedHostPatterns(IList<string> allowedHosts)
        => allowedHosts.Count == 0 || allowedHosts.Contains("*")
            ? null
            : [.. allowedHosts.Select(host => new StringSegment(new HostString(host).ToUriComponent()))];

    private static bool IsAllowedOrigin(string origin, StringSegment[]? allowedHosts)
        // Matched the way HostFilteringMiddleware matches Host headers (port ignored, IPv6 in
        // brackets, *.example.com wildcards), so the hub accepts exactly the hosts the app does.
        => allowedHosts is null
            || (Uri.TryCreate(origin, UriKind.Absolute, out var originUri)
                && HostString.MatchesAny(originUri.Authority, allowedHosts));
}
