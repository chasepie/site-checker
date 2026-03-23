using System.Text.Json;
using System.Text.Json.Serialization;
using dotenv.net;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using SiteChecker.Backend.JsonConverters;
using SiteChecker.Backend.Services;
using SiteChecker.Backend.Services.CheckQueue;
using SiteChecker.Backend.Services.SignalR;
using SiteChecker.Backend.Services.VPN;
using SiteChecker.Database;
using SiteChecker.Database.Services;
using SiteChecker.Backend.Notifiers.Discord;
using SiteChecker.Backend.Notifiers.Pushover;
using SiteChecker.Scraper;
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
        BuildApplication(app);
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

        services
            .AddSiteCheckQueueService()
            .AddHostedService<SiteCheckQueueProcessor>()
            .AddHostedService<SiteCheckTimer>();

        services.AddHttpContextAccessor();
        services.AddPiaService();

        services.AddScoped<IEntityChangeService, EntityChangesService>();

        services.TryAddPushoverService(configuration);
        services.TryAddDiscordService(configuration);
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
            app.MapStaticAssets();
        }

        app.MapOpenApi();
        app.MapScalarApiReference();

        app.UseRouting();
        app.UseAuthorization();

        app.MapControllers();
        app.MapHealthChecks("/healthz");
        app.MapHub<DataHub>($"/{SignalRConstants.HubName}");

        app.MapFallbackToFile("/index.html");
    }

    private static async Task ConfigureDatabaseAsync(WebApplication app)
    {
        using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var dbContext = services.GetRequiredService<SiteCheckerDbContext>();
        await dbContext.Database.MigrateAsync();

        await new DataSeeder(dbContext).SeedDataAsync();
    }
}
