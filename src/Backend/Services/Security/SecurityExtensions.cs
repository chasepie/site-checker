using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HostFiltering;

namespace SiteChecker.Backend.Services.Security;

public static class SecurityExtensions
{
    /// <summary>
    /// The host names the app answers to, separated by semicolons. <c>localhost</c> is always
    /// added, for the container healthcheck.
    /// </summary>
    public const string AllowedHostsKey = "ALLOWED_HOSTS";

    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the admin token, its authentication scheme and the <see cref="AdminToken.PolicyName"/>
        /// policy.
        /// </summary>
        public IServiceCollection AddAdminToken()
        {
            services.AddSingleton<AdminToken>();
            services
                .AddAuthentication(AdminToken.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, AdminTokenAuthenticationHandler>(AdminToken.SchemeName, configureOptions: null);
            services
                .AddAuthorizationBuilder()
                .AddPolicy(AdminToken.PolicyName, policy => policy
                    .AddAuthenticationSchemes(AdminToken.SchemeName)
                    .RequireAuthenticatedUser());
            return services;
        }

        /// <summary>
        /// Restricts the Host headers the app accepts to <c>ALLOWED_HOSTS</c>, so a web page can't
        /// reach the API by rebinding its own domain to the app's address. Outside Development,
        /// <c>ALLOWED_HOSTS</c> is required and can't be <c>*</c>.
        /// </summary>
        public IServiceCollection AddAllowedHosts()
        {
            services
                .AddOptions<HostFilteringOptions>()
                .Configure<IConfiguration, IHostEnvironment>((options, configuration, environment) =>
                {
                    var hosts = (configuration[AllowedHostsKey] ?? string.Empty)
                        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (hosts.Length == 0 || hosts.Contains("*"))
                    {
                        if (!environment.IsDevelopment())
                        {
                            throw new InvalidOperationException(
                                $"{AllowedHostsKey} must list the host names the app is reached by, separated by semicolons (for example 'sitechecker.lan;sitechecker.tailnet.ts.net'), and can't be '*'.");
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
}
