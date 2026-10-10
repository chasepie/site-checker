namespace SiteChecker.Backend.IntegrationTests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SiteChecker.Backend.Services;
using SiteChecker.Database;
using SiteChecker.Scraper;

/// <summary>
/// The real app over an in-memory SQLite database (otherwise it would use the repo's
/// <c>site-checker/data</c> file), with demo data off, no background services, and a fake scraper.
/// </summary>
internal sealed class SiteApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public FakeScraperService Scraper { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        builder.UseEnvironment(Environments.Development);
        builder.UseSetting(DemoDataSeeder.SeedDemoDataKey, "false");
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
