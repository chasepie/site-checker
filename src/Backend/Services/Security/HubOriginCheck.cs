using Microsoft.AspNetCore.HostFiltering;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace SiteChecker.Backend.Services.Security;

/// <summary>
/// Refuses a browser's connection to a SignalR hub from a page on another host. WebSockets aren't
/// covered by CORS, and the session cookie is <c>SameSite=Strict</c>, which still lets a page on a
/// "same site" host (such as another Tailscale machine under <c>ts.net</c>) open the hub with it
/// and read live data. Browsers always send <c>Origin</c> on these requests; a request without one
/// isn't from a browser page, so it's left to the login.
/// </summary>
public static class HubOriginCheck
{
    extension(IApplicationBuilder app)
    {
        /// <summary>
        /// Answers 403 to requests under <paramref name="hubPath"/> whose <c>Origin</c> host isn't
        /// one of the allowed hosts. Does nothing while every host is allowed (Development).
        /// </summary>
        public IApplicationBuilder UseHubOriginCheck(PathString hubPath)
        {
            return app.UseWhen(
                context => context.Request.Path.StartsWithSegments(hubPath),
                branch => branch.Use(async (context, next) =>
                {
                    var allowedHosts = context.RequestServices.GetRequiredService<IOptions<HostFilteringOptions>>().Value.AllowedHosts;
                    var origin = context.Request.Headers.Origin.ToString();
                    if (origin.Length > 0 && !IsAllowedOrigin(origin, allowedHosts))
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
