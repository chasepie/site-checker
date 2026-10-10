using System.Text.Json;
using System.Text.Json.Serialization;
using dotenv.net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using SiteChecker.Backend.JsonConverters;
using SiteChecker.Backend.Services;
using SiteChecker.Backend.Services.CheckQueue;
using SiteChecker.Backend.Services.Scraping;
using SiteChecker.Backend.Services.Security;
using SiteChecker.Backend.Services.SignalR;
using SiteChecker.Backend.Services.Sites;
using SiteChecker.Backend.Services.TestRuns;
using SiteChecker.Backend.Services.VPN;
using SiteChecker.Database;
using SiteChecker.Database.Services;
using SiteChecker.Backend.Notifiers.Discord;
using SiteChecker.Backend.Notifiers.Pushover;
using SiteChecker.Scraper;
using SiteChecker.Scraper.Scripts;
using SiteChecker.Utilities;
using SiteChecker.Backend.Extensions;

namespace SiteChecker.Backend;

public class Program
{
    public static async Task Main(string[] args)
    {
        DotEnv.Fluent()
            .WithEnvFiles()
            .WithProbeForEnv()
            .WithOverwriteExistingVars()
            .WithoutExceptions()
            .Load();

        var builder = WebApplication.CreateBuilder(args);
        builder.ConfigureLogging(nameof(SiteChecker));
        BuildServices(builder.Services, builder.Configuration, builder.Environment);

        var app = builder.Build();
        ValidateSecuritySettings(app);
        BuildApplication(app);
        ValidateScraperServices(app);
        await ConfigureDatabaseAsync(app);

        await app.RunAsync();
    }

    private static void BuildServices(
        IServiceCollection services,
        ConfigurationManager configuration,
        IWebHostEnvironment environment)
    {
        static void SetJsonOptions(JsonSerializerOptions options)
        {
            options.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            options.Converters.Add(new DateTimeConverter());
            options.Converters.Add(new JsonStringEnumConverter());
        }

        // Add services to the container.
        services
            .AddControllers()
            .AddJsonOptions(options =>
            {
                SetJsonOptions(options.JsonSerializerOptions);
            });

        services.AddSingleton<HubConnections>();
        services
            .AddSignalR(options =>
            {
                options.EnableDetailedErrors = environment.IsDevelopment();
                options.MaximumReceiveMessageSize = 1024 * 1024 * 5; // 5 MB
            })
            .AddJsonProtocol(options =>
            {
                SetJsonOptions(options.PayloadSerializerOptions);
            });

        services
            .AddDbContext<SiteCheckerDbContext>(o =>
            {
                if (environment.IsDevelopment())
                {
                    o.EnableDetailedErrors();
                    o.EnableSensitiveDataLogging();
                }
            });

        services.AddScraperServices();
        services.TryAddRemoteScraperService(configuration);
        services.AddScoped<DemoDataSeeder>();
        services.AddSingleton<SiteValidator>();
        services.AddSingleton<TestRunService>();

        services.AddSiteCheckRunner();

        services.AddLogin();
        services.AddAllowedHosts();

        services.AddHttpContextAccessor();
        services.AddPiaService();

        services.AddScoped<IEntityChangeService, EntityChangesService>();

        services.TryAddPushoverChannel(configuration);
        services.TryAddDiscordChannel(configuration);
        services.AddNotifierService();

        services.AddHealthChecks();
        services.TryAddHealthCheckService(configuration);

        services.AddOpenApi();
    }

    private static void BuildApplication(WebApplication app)
    {
        if (app.Environment.IsProduction())
        {
            app.UseDefaultFiles();
            // The SPA itself is public, so the login page can load; its data isn't.
            app.MapStaticAssets().AllowAnonymous();
        }

        app.MapOpenApi();
        app.MapScalarApiReference();

        app.UseRouting();
        app.UseHubOriginCheck($"/{SignalRConstants.HubName}");
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        // Every endpoint requires the login (the fallback policy) unless it's marked anonymous.
        app.MapControllers();
        app.MapHealthChecks("/healthz").AllowAnonymous();
        // SignalR keeps the user it connected with, so close the connection when the session ends.
        app.MapHub<DataHub>($"/{SignalRConstants.HubName}", options => options.CloseOnAuthenticationExpiration = true);

        app.MapFallbackToFile("/index.html").AllowAnonymous();
    }

    /// <summary>
    /// Fails startup when the password or the allowed hosts are missing outside Development,
    /// rather than on the first request.
    /// </summary>
    private static void ValidateSecuritySettings(WebApplication app)
    {
        app.Services.GetRequiredService<AdminPassword>();
        // Reads SESSION_DAYS.
        _ = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        _ = app.Services.GetRequiredService<IOptions<HostFilteringOptions>>().Value;
    }

    /// <summary>
    /// Fails startup on invalid scrape timeouts, and builds the script compiler's references once,
    /// up front, rather than on the first check. Outside Development, a Scrape Worker needs its
    /// secret. Warns when scripts would run inside the app's container because no Scrape Worker is
    /// configured.
    /// </summary>
    private static void ValidateScraperServices(WebApplication app)
    {
        app.Services.GetRequiredService<ScrapeTimeouts>();
        app.Services.GetRequiredService<IScriptCompiler>();

        if (!string.IsNullOrWhiteSpace(app.Configuration[RemoteScraperService.ScrapeWorkerUrlKey])
            && ScrapeWorkerSecret.Read(app.Configuration) is null)
        {
            if (!app.Environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"{ScrapeWorkerSecret.Key} must be set when {RemoteScraperService.ScrapeWorkerUrlKey} is, to the same value as the Scrape Worker's.");
            }

            app.Logger.LogWarning("{Key} isn't set, so the Scrape Worker must be running without one too.", ScrapeWorkerSecret.Key);
        }

        if (app.Services.GetRequiredService<IScraperService>() is ScraperService && EnvironmentUtils.IsDockerContainer())
        {
            app.Logger.LogWarning(
                "{Key} isn't set, so scripts run inside the app's container, with its secrets and database. Run the Scrape Worker (see docker-compose.yml).",
                RemoteScraperService.ScrapeWorkerUrlKey);
        }
    }

    private static async Task ConfigureDatabaseAsync(WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var dbContext = services.GetRequiredService<SiteCheckerDbContext>();
        await dbContext.Database.MigrateAsync();

        await services.GetRequiredService<DemoDataSeeder>().SeedAsync(CancellationToken.None);
    }
}
