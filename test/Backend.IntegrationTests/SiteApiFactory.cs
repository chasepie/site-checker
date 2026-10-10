namespace SiteChecker.Backend.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.DataProtection;
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
/// Its password is <see cref="Password"/>: <see cref="CreateLoggedInClientAsync"/> logs in with it,
/// and <c>CreateClient()</c> gives a client that isn't logged in. Cookie keys go to a temporary
/// directory, deleted with the factory, rather than the repo's data directory.
/// </summary>
/// <param name="environment">The host environment; Development unless a test needs another.</param>
/// <param name="settings">Configuration that replaces the factory's defaults, such as
/// <c>ADMIN_PASSWORD</c>; a <c>null</c> value clears the setting.</param>
internal sealed class SiteApiFactory(
    string? environment = null,
    IReadOnlyDictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
{
    public const string Password = "test-password";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly string _keysDirectory = Path.Combine(Path.GetTempPath(), $"site-checker-keys-{Guid.NewGuid():N}");
    private readonly string _environment = environment ?? Environments.Development;
    private readonly Dictionary<string, string?> _settings = new(StringComparer.OrdinalIgnoreCase)
    {
        [DemoDataSeeder.SeedDemoDataKey] = "false",
        [AdminPassword.AdminPasswordKey] = Password,
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
            // Registered after the app's, so it replaces the data directory as the key store.
            services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(_keysDirectory));
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

    /// <summary>
    /// A client logged in with <see cref="Password"/>, sending the antiforgery token the login
    /// issued, as the frontend does. Its cookies keep the session.
    /// </summary>
    public async Task<HttpClient> CreateLoggedInClientAsync(CancellationToken cancellationToken)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { password = Password }, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode, "Logging in failed.");
        client.DefaultRequestHeaders.Add(SecurityExtensions.AntiforgeryHeaderName, ReadAntiforgeryToken(response));
        return client;
    }

    /// <summary>
    /// The antiforgery request token a response issued in its <c>XSRF-TOKEN</c> cookie.
    /// </summary>
    public static string ReadAntiforgeryToken(HttpResponseMessage response)
    {
        var prefix = SecurityExtensions.AntiforgeryCookieName + "=";
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(prefix, StringComparison.Ordinal));
        return Uri.UnescapeDataString(cookie[prefix.Length..cookie.IndexOf(';', StringComparison.Ordinal)]);
    }

    public SiteCheckerDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<SiteCheckerDbContext>().UseSqlite(_connection).Options);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
            if (Directory.Exists(_keysDirectory))
            {
                Directory.Delete(_keysDirectory, recursive: true);
            }
        }
    }
}
