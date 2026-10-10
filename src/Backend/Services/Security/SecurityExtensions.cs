using System.Globalization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    /// How many days a login lasts without being used (default 30). Each request extends it.
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

    public const string LoginRateLimitPolicy = "login";

    public const int LoginAttemptsPerMinute = 5;

    private const int DefaultSessionDays = 30;

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
        /// no password is required), antiforgery on every controller write, the login rate limit,
        /// and data protection keys kept in the data directory, so a restart doesn't log you out.
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
                    // The app is often reached over plain HTTP on the LAN.
                    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                    options.ExpireTimeSpan = TimeSpan.FromDays(ReadSessionDays(configuration));
                    options.SlidingExpiration = true;

                    // API endpoints, including the SignalR hub, answer 401/403 (ASP.NET Core 10 and
                    // later). Pages that aren't the SPA, such as Scalar, redirect to its login page,
                    // which reads returnUrl.
                    options.LoginPath = "/login";
                    options.ReturnUrlParameter = "returnUrl";
                });

            services.AddSingleton<IAuthorizationHandler, LoggedInHandler>();
            services
                .AddAuthorizationBuilder()
                .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                    .AddRequirements(new LoggedInRequirement())
                    .Build());

            services.AddAntiforgery(options => options.HeaderName = AntiforgeryHeaderName);
            services.Configure<MvcOptions>(options => options.Filters.Add<ValidateAntiforgeryFilter>());

            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                // One user, so one shared window: no per-client partition to get wrong behind a proxy.
                options.AddFixedWindowLimiter(LoginRateLimitPolicy, window =>
                {
                    window.PermitLimit = LoginAttemptsPerMinute;
                    window.Window = TimeSpan.FromMinutes(1);
                    window.QueueLimit = 0;
                });
            });

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
    }

    /// <exception cref="InvalidOperationException"><c>SESSION_DAYS</c> isn't a positive whole number.</exception>
    private static int ReadSessionDays(IConfiguration configuration)
    {
        var value = configuration[SessionDaysKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultSessionDays;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var days) || days <= 0)
        {
            throw new InvalidOperationException($"{SessionDaysKey} must be a positive whole number of days, but is '{value}'.");
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
