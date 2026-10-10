using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using SiteChecker.Utilities;

namespace SiteChecker.Backend.Services.Security;

public static class SecurityExtensions
{
    /// <summary>
    /// The host names the app answers to, separated by semicolons. <c>localhost</c> is always
    /// added, for the container healthcheck.
    /// </summary>
    public const string AllowedHostsKey = "ALLOWED_HOSTS";

    /// <summary>
    /// The reverse proxies in front of the app, as IP addresses or CIDR ranges separated by
    /// semicolons. Their <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> headers are believed;
    /// anyone else's are ignored.
    /// </summary>
    public const string TrustedProxiesKey = "TRUSTED_PROXIES";

    /// <summary>
    /// How many days a login lasts (default 30). A request in the second half of that renews it for
    /// the full period (sliding expiration), so an unused login ends between half and all of it
    /// after the last visit.
    /// </summary>
    public const string SessionDaysKey = "SESSION_DAYS";

    public const string SessionCookieName = "SiteChecker.Session";

    /// <summary>
    /// The header Angular's HttpClient sends the antiforgery token in, read from
    /// <see cref="AntiforgeryCookieName"/>.
    /// </summary>
    public const string AntiforgeryHeaderName = "X-XSRF-TOKEN";

    /// <summary>
    /// The cookie the antiforgery request token is issued in, readable by the page's scripts.
    /// </summary>
    public const string AntiforgeryCookieName = "XSRF-TOKEN";

    private const int DefaultSessionDays = 30;

    /// <summary>
    /// 100 years. Much more and a login's expiry passes the year 9999, which can't be represented,
    /// so every login would fail.
    /// </summary>
    private const int MaxSessionDays = 36_500;

    /// <summary>
    /// The entries <c>HostFilteringMiddleware</c> treats as "allow any host". Any one of them in the
    /// list turns filtering off, whatever else is listed.
    /// </summary>
    private static readonly string[] WildcardHosts = ["*", "0.0.0.0", "[::]"];

    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the login: <see cref="AdminPassword"/>, cookie sessions lasting
        /// <c>SESSION_DAYS</c>, a fallback policy that puts every endpoint behind the login (unless
        /// no password is required), antiforgery on every controller write, the
        /// <see cref="LoginThrottle"/>, and data protection keys kept in the data directory, so a
        /// restart doesn't log you out.
        /// </summary>
        public IServiceCollection AddLogin()
        {
            services.AddSingleton<AdminPassword>();

            services
                .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie();
            services
                .AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
                .Configure<IConfiguration>((options, configuration) =>
                {
                    options.Cookie.Name = SessionCookieName;
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SameSite = SameSiteMode.Strict;
                    // The app is often reached over plain HTTP on the LAN. Behind a proxy that serves
                    // HTTPS, this needs the proxy in TRUSTED_PROXIES to see the request as HTTPS.
                    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                    options.ExpireTimeSpan = TimeSpan.FromDays(ReadSessionDays(configuration));
                    options.SlidingExpiration = true;

                    // API endpoints, including the SignalR hub, answer 401/403 (ASP.NET Core 10 and
                    // later). Pages that aren't the SPA, such as Scalar, redirect to its login page,
                    // which reads returnUrl.
                    options.LoginPath = "/login";
                    options.ReturnUrlParameter = "returnUrl";

                    // The keys outlive a password change, so without this a session made with the
                    // old password would keep working, renewed on every visit.
                    options.Events.OnValidatePrincipal = async context =>
                    {
                        var adminPassword = context.HttpContext.RequestServices.GetRequiredService<AdminPassword>();
                        if (adminPassword.IsRequired
                            && !adminPassword.IsCurrentSessionStamp(context.Principal?.FindFirst(AdminPassword.SessionStampClaim)?.Value))
                        {
                            context.RejectPrincipal();
                            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        }
                    };
                });

            services.AddSingleton<IAuthorizationHandler, LoggedInHandler>();
            services
                .AddAuthorizationBuilder()
                .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                    .AddRequirements(new LoggedInRequirement())
                    .Build());

            services.AddAntiforgery(options => options.HeaderName = AntiforgeryHeaderName);
            services.Configure<MvcOptions>(options => options.Filters.Add<ValidateAntiforgeryFilter>());

            services.AddSingleton<LoginThrottle>();

            services
                .AddDataProtection()
                .SetApplicationName(nameof(SiteChecker))
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Join(AppDirectories.Data, "keys")));

            return services;
        }

        /// <summary>
        /// Restricts the Host headers the app accepts to <c>ALLOWED_HOSTS</c>, so a web page can't
        /// reach the API by rebinding its own domain to the app's address. Outside Development,
        /// <c>ALLOWED_HOSTS</c> is required and can't include a wildcard (<c>*</c>, <c>0.0.0.0</c> or
        /// <c>[::]</c>).
        /// </summary>
        public IServiceCollection AddAllowedHosts()
        {
            services
                .AddOptions<HostFilteringOptions>()
                .Configure<IConfiguration, IHostEnvironment>((options, configuration, environment) =>
                {
                    var hosts = (configuration[AllowedHostsKey] ?? string.Empty)
                        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (hosts.Length == 0 || hosts.Any(host => WildcardHosts.Contains(host)))
                    {
                        if (!environment.IsDevelopment())
                        {
                            throw new InvalidOperationException(
                                $"{AllowedHostsKey} must list the host names the app is reached by, separated by semicolons (for example 'sitechecker.lan;sitechecker.tailnet.ts.net'), and can't include '*', '0.0.0.0' or '[::]', which allow any host.");
                        }

                        // Development keeps appsettings' AllowedHosts, which the host applies when
                        // this leaves the list empty.
                        return;
                    }

                    options.AllowedHosts = [.. hosts.Append("localhost").Distinct(StringComparer.OrdinalIgnoreCase)];
                });
            return services;
        }

        /// <summary>
        /// Believes the client address and scheme that the proxies in <c>TRUSTED_PROXIES</c> forward,
        /// so the session cookie is <c>Secure</c> behind a proxy that serves HTTPS, and login
        /// attempts are counted per client rather than per proxy. Without it, forwarded headers are
        /// ignored. The Host header is never taken from them, so <c>ALLOWED_HOSTS</c> still sees the
        /// one the proxy sent.
        /// </summary>
        public IServiceCollection AddTrustedProxies()
        {
            services
                .AddOptions<ForwardedHeadersOptions>()
                .Configure<IConfiguration>((options, configuration) =>
                {
                    // Only the configured proxies, not ASP.NET Core's default of loopback.
                    options.KnownProxies.Clear();
                    options.KnownIPNetworks.Clear();
                    foreach (var entry in (configuration[TrustedProxiesKey] ?? string.Empty)
                        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        // A range first: IPAddress parses "fd00::/64" too, as the one address.
                        if (entry.Contains('/') && System.Net.IPNetwork.TryParse(entry, out var network))
                        {
                            options.KnownIPNetworks.Add(network);
                        }
                        else if (!entry.Contains('/') && IPAddress.TryParse(entry, out var address))
                        {
                            options.KnownProxies.Add(address);
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                $"{TrustedProxiesKey} must list IP addresses or CIDR ranges (such as '172.18.0.0/16'), separated by semicolons, but has '{entry}'.");
                        }
                    }

                    // With nothing listed, an empty list would mean "believe anyone", so read nothing.
                    options.ForwardedHeaders = options.KnownProxies.Count + options.KnownIPNetworks.Count > 0
                        ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
                        : ForwardedHeaders.None;
                    // Walk back through every trusted proxy in a chain, stopping at the first address
                    // that isn't one, rather than only the last hop.
                    options.ForwardLimit = null;
                });
            return services;
        }
    }

    /// <exception cref="InvalidOperationException"><c>SESSION_DAYS</c> isn't a whole number of days
    /// from 1 to <see cref="MaxSessionDays"/>.</exception>
    private static int ReadSessionDays(IConfiguration configuration)
    {
        var value = configuration[SessionDaysKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultSessionDays;
        }

        // Trimmed, like the password, so stray whitespace in .env doesn't stop startup.
        if (!int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var days)
            || days <= 0
            || days > MaxSessionDays)
        {
            throw new InvalidOperationException(
                $"{SessionDaysKey} must be a whole number of days from 1 to {MaxSessionDays}, but is '{value}'.");
        }
        return days;
    }
}

/// <summary>
/// The fallback policy's requirement: logged in, or no password required (Development).
/// </summary>
public sealed class LoggedInRequirement : IAuthorizationRequirement;

public sealed class LoggedInHandler(AdminPassword adminPassword) : AuthorizationHandler<LoggedInRequirement>
{
    private readonly AdminPassword _adminPassword = adminPassword;

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, LoggedInRequirement requirement)
    {
        if (!_adminPassword.IsRequired || context.User.Identity?.IsAuthenticated == true)
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}
