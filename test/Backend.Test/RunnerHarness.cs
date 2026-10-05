namespace SiteChecker.Backend.Test;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SiteChecker.Backend.Services.CheckQueue;
using SiteChecker.Backend.Services.VPN;
using SiteChecker.Database;
using SiteChecker.Database.Model;
using SiteChecker.Scraper;

/// <summary>
/// Hosts a <see cref="SiteCheckRunner"/> over a migrated in-memory SQLite database, with a fake
/// clock and a fake scraper. The database outlives <see cref="RestartAsync"/>, so a test can
/// simulate the app restarting.
/// </summary>
internal sealed class RunnerHarness : IAsyncDisposable
{
    public static readonly DateTimeOffset DefaultStart = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A Schedule that is due all day, every 15 minutes.
    /// </summary>
    public static SiteSchedule AllDaySchedule => new()
    {
        Enabled = true,
        Start = new TimeOnly(0, 0),
        End = new TimeOnly(23, 59),
        Interval = 15,
    };

    private readonly SqliteConnection _connection;
    private ServiceProvider _services;
    private int _siteCount;

    public FakeTimeProvider Time { get; }
    public FakeScraperService Scraper { get; } = new();
    public SiteCheckRunner Runner { get; private set; }

    private RunnerHarness(SqliteConnection connection, FakeTimeProvider time)
    {
        _connection = connection;
        Time = time;
        _services = BuildServices();
        Runner = _services.GetRequiredService<SiteCheckRunner>();
    }

    public static async Task<RunnerHarness> CreateAsync(
        CancellationToken cancellationToken,
        TimeZoneInfo? localTimeZone = null,
        DateTimeOffset? start = null)
    {
        // The in-memory database lives as long as this connection stays open.
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(cancellationToken);

        var time = new FakeTimeProvider(start ?? DefaultStart);
        if (localTimeZone != null)
        {
            time.SetLocalTimeZone(localTimeZone);
        }

        var harness = new RunnerHarness(connection, time);
        await using var dbContext = harness.CreateDbContext();
        await dbContext.Database.MigrateAsync(cancellationToken);
        return harness;
    }

    private ServiceProvider BuildServices()
    {
        return new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton<TimeProvider>(Time)
            .AddSingleton<IScraperService>(Scraper)
            .AddSingleton<PiaService>()
            .AddDbContext<SiteCheckerDbContext>(o => o.UseSqlite(_connection))
            .AddSingleton<SiteCheckRunner>()
            .BuildServiceProvider(validateScopes: true);
    }

    /// <summary>
    /// Throws away the runner and everything in memory, keeping only the database.
    /// </summary>
    public async Task RestartAsync()
    {
        await _services.DisposeAsync();
        _services = BuildServices();
        Runner = _services.GetRequiredService<SiteCheckRunner>();
    }

    public SiteCheckerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SiteCheckerDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new SiteCheckerDbContext(options);
    }

    public async Task<Site> AddSiteAsync(SiteSchedule? schedule, CancellationToken cancellationToken)
    {
        _siteCount++;
        var site = new Site
        {
            Name = $"Site {_siteCount}",
            Url = new Uri($"https://example.com/{_siteCount}"),
            ScraperId = $"SCRAPER_{_siteCount}",
            Schedule = schedule ?? new SiteSchedule(),
        };

        await using var dbContext = CreateDbContext();
        dbContext.Sites.Add(site);
        await dbContext.SaveChangesAsync(cancellationToken);
        return site;
    }

    public async Task<List<SiteCheck>> GetChecksAsync(int siteId, CancellationToken cancellationToken)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.SiteChecks
            .AsNoTracking()
            .Where(sc => sc.SiteId == siteId)
            .OrderBy(sc => sc.Id)
            .ToListAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

/// <summary>
/// Stands in for Playwright. Never routes through the VPN, so the VPN code is never reached.
/// </summary>
internal sealed class FakeScraperService : IScraperService
{
    public Func<ScrapeRequest, Task<IScrapeResult>> OnScrape { get; set; } =
        _ => Task.FromResult<IScrapeResult>(new SuccessScrapeResult { Content = "content" });

    public List<ScrapeRequest> Requests { get; } = [];

    public Task<IScrapeResult> ScrapeContentAsync(ScrapeRequest request)
    {
        Requests.Add(request);
        return OnScrape(request);
    }

    public BrowserType GetBrowserType(bool useVpn) => BrowserType.Browserless;
}
