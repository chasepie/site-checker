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
        /// <c>same-origin</c>, or whose <c>Origin</c> host isn't one of the allowed hosts. The
        /// host check does nothing while every host is allowed (Development).
        /// </summary>
        public IApplicationBuilder UseHubOriginCheck(PathString hubPath)
        {
            return app.UseWhen(
                context => context.Request.Path.StartsWithSegments(hubPath),
                branch => branch.Use(async (context, next) =>
                {
                    var allowedHosts = context.RequestServices.GetRequiredService<IOptions<HostFilteringOptions>>().Value.AllowedHosts;
                    var fetchSite = context.Request.Headers["Sec-Fetch-Site"].ToString();
                    var origin = context.Request.Headers.Origin.ToString();
                    if ((fetchSite.Length > 0 && fetchSite != "same-origin")
                        || (origin.Length > 0 && !IsAllowedOrigin(origin, allowedHosts)))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return;
                    }
                    await next(context);
                }));
        }
    }

    private static bool IsAllowedOrigin(string origin, IList<string> allowedHosts)
    {
        if (allowedHosts.Count == 0 || allowedHosts.Contains("*"))
        {
            return true;
        }
        // Matched the way HostFilteringMiddleware matches Host headers (port ignored, IPv6 in
        // brackets, *.example.com wildcards), so the hub accepts exactly the hosts the app does.
        return Uri.TryCreate(origin, UriKind.Absolute, out var originUri)
            && HostString.MatchesAny(originUri.Authority, [.. allowedHosts.Select(host => new StringSegment(host))]);
    }
}
