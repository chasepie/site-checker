using System.Text.Json;
using System.Text.Json.Serialization;
using dotenv.net;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using SiteChecker.Application.UseCases;
using SiteChecker.Backend.JsonConverters;
using SiteChecker.Backend.Services;
using SiteChecker.Backend.Services.CheckQueue;
using SiteChecker.Backend.Services.DomainEvents;
using SiteChecker.Backend.Services.SignalR;
using SiteChecker.Backend.Services.VPN;
using SiteChecker.Database;
using SiteChecker.Database.Repositories;
using SiteChecker.Domain.Entities;
using SiteChecker.Domain.Events;
using SiteChecker.Domain.Ports;
using SiteChecker.Backend.Notifiers.Discord;
using SiteChecker.Backend.Notifiers.Pushover;
using SiteChecker.Scraper;
using SiteChecker.Backend.Extensions;
using SiteChecker.Utilities;
using SiteChecker.PlaywrightServer;

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
            .AddDbContext<SiteCheckerDbContext>((sp, options) =>
            {
                var dbDir = GetDbDataDirectory();
                if (!dbDir.Exists)
                {
                    dbDir.Create();
                }

                var dbPath = Path.Join(dbDir.FullName, "SiteChecker.db");
                options.UseSqlite($"Data Source={dbPath}");

                if (environment.IsDevelopment())
                {
                    options.EnableDetailedErrors();
                    options.EnableSensitiveDataLogging();
                }
            });

        services.AddScraperServices();

        services.AddScoped<ISiteRepository, SiteRepository>();
        services.AddScoped<ISiteCheckRepository, SiteCheckRepository>();

        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        // SignalR event handlers — broadcast entity changes to connected clients
        services.AddScoped<SignalREventHandler<Site>>();
        services.AddScoped<IDomainEventHandler<EntityCreatedEvent<Site>>>(sp =>
            sp.GetRequiredService<SignalREventHandler<Site>>());
        services.AddScoped<IDomainEventHandler<EntityUpdatedEvent<Site>>>(sp =>
            sp.GetRequiredService<SignalREventHandler<Site>>());
        services.AddScoped<IDomainEventHandler<EntityDeletedEvent<Site>>>(sp =>
            sp.GetRequiredService<SignalREventHandler<Site>>());

        services.AddScoped<SignalREventHandler<SiteCheck>>();
        services.AddScoped<IDomainEventHandler<EntityCreatedEvent<SiteCheck>>>(sp =>
            sp.GetRequiredService<SignalREventHandler<SiteCheck>>());
        services.AddScoped<IDomainEventHandler<EntityUpdatedEvent<SiteCheck>>>(sp =>
            sp.GetRequiredService<SignalREventHandler<SiteCheck>>());
        services.AddScoped<IDomainEventHandler<EntityDeletedEvent<SiteCheck>>>(sp =>
            sp.GetRequiredService<SignalREventHandler<SiteCheck>>());

        // Queue handler — transitions new SiteChecks to Queued and enqueues them
        services.AddScoped<IDomainEventHandler<EntityCreatedEvent<SiteCheck>>, QueueEventHandler>();

        // Notification handler — fires notifications when a SiteCheck reaches a terminal state
        services.AddScoped<IDomainEventHandler<EntityUpdatedEvent<SiteCheck>>, NotificationEventHandler>();

        // Browser server
        services.AddScoped<IBrowserServer, PlaywrightBrowserServer>();

        services.AddScoped<ManageSitesUseCase>();
        services.AddScoped<CreateSiteCheckUseCase>();
        services.AddScoped<PerformSiteCheckUseCase>();
        services.AddScoped<ScheduleSiteChecksUseCase>();
        services.AddScoped<NotifyCheckCompletedUseCase>();
        services.AddScoped<BrowserServerUseCase>();

        services
            .AddSiteCheckQueueService()
            .AddHostedService<SiteCheckQueueProcessor>()
            .AddHostedService<SiteCheckTimer>();

        services.AddHttpContextAccessor();
        services.AddPiaService();

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

        var siteRepository = services.GetRequiredService<ISiteRepository>();
        await new DataSeeder(siteRepository).SeedDataAsync();
    }

    private static DirectoryInfo GetDbDataDirectory()
    {
        string dataDir;

        if (!EnvironmentUtils.IsDockerContainer()
            && RepoUtils.TryGetRepoDirectory(out var repoRoot))
        {
            dataDir = Path.Join(repoRoot, "site-checker/data");
        }
        else
        {
            dataDir = Path.Join(AppContext.BaseDirectory, "data");
        }

        return new DirectoryInfo(dataDir);
    }
}
