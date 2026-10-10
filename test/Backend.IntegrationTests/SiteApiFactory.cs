namespace SiteChecker.Backend.IntegrationTests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SiteChecker.Backend.Services;
using SiteChecker.Backend.Services.Security;
using SiteChecker.Database;
using SiteChecker.Scraper;

/// <summary>
/// The real app over an in-memory SQLite database (otherwise it would use the repo's
/// <c>site-checker/data</c> file), with demo data off, no background services, and a fake scraper.
/// It requires <see cref="AdminTokenValue"/>, which clients it creates send.
/// </summary>
/// <param name="environment">The host environment; Development unless a test needs another.</param>
/// <param name="settings">Configuration that replaces the factory's defaults, such as
/// <c>ADMIN_TOKEN</c>; a <c>null</c> value clears the setting.</param>
internal sealed class SiteApiFactory(
    string? environment = null,
    IReadOnlyDictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
{
    public const string AdminTokenValue = "test-admin-token";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly string _environment = environment ?? Environments.Development;
    private readonly Dictionary<string, string?> _settings = new(StringComparer.OrdinalIgnoreCase)
    {
        [DemoDataSeeder.SeedDemoDataKey] = "false",
        [AdminToken.AdminTokenKey] = AdminTokenValue,
    };

    public FakeScraperService Scraper { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            _settings[key] = value;
        }

        builder.UseEnvironment(_environment);
        foreach (var (key, value) in _settings)
        {
            // Empty rather than absent, so a value from the developer's .env can't fill it in.
            builder.UseSetting(key, value ?? string.Empty);
        }
        builder.ConfigureTestServices(services =>
        {
            services.ConfigureDbContext<SiteCheckerDbContext>(o => o.UseSqlite(_connection));
            services.AddSingleton<IScraperService>(Scraper);

            // The app's own background services: the check timer and queue, and the health ping.
            var appHostedServices = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                    && d.ImplementationType?.Assembly == typeof(Program).Assembly)
                .ToList();
            foreach (var descriptor in appHostedServices)
            {
                services.Remove(descriptor);
            }
        });
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", AdminTokenValue);
    }

    public SiteCheckerDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<SiteCheckerDbContext>().UseSqlite(_connection).Options);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
