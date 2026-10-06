namespace SiteChecker.Backend.Test;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SiteChecker.Backend.Notifiers;
using SiteChecker.Backend.Services;
using SiteChecker.Backend.Services.CheckQueue;
using SiteChecker.Backend.Services.VPN;
using SiteChecker.Database;
using SiteChecker.Database.Model;
using SiteChecker.Database.Services;
using SiteChecker.Scraper;

/// <summary>
/// Hosts a <see cref="SiteCheckRunner"/> over a migrated in-memory SQLite database, with a fake
/// clock and a fake scraper. The database outlives <see cref="RestartAsync"/>, so a test can
/// simulate the app restarting. Only the runner's saves are recorded in <see cref="Broadcasts"/>
/// and can be failed with <see cref="SaveFaults"/>; saves through <see cref="CreateDbContext"/>
/// (test setup and assertions) are not.
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
    public RecordingEntityChangeService Broadcasts { get; } = new();
    public SaveFaultInterceptor SaveFaults { get; } = new();

    /// <summary>
    /// Receives every notification the notifier sends.
    /// </summary>
    public RecordingNotificationChannel Notifications { get; } = new();

    /// <summary>
    /// A second channel, registered first, that a test can make fail.
    /// </summary>
    public RecordingNotificationChannel OtherChannel { get; } = new();
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
            .AddSingleton<IEntityChangeService>(Broadcasts)
            .AddSingleton<INotificationChannel>(OtherChannel)
            .AddSingleton<INotificationChannel>(Notifications)
            .AddNotifierService()
            .AddDbContext<SiteCheckerDbContext>(o => o
                .UseSqlite(_connection)
                .AddInterceptors(SaveFaults))
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

    public async Task<Site> AddSiteAsync(
        SiteSchedule? schedule,
        CancellationToken cancellationToken,
        int knownFailuresThreshold = 5)
    {
        _siteCount++;
        var site = new Site
        {
            Name = $"Site {_siteCount}",
            Url = new Uri($"https://example.com/{_siteCount}"),
            ScraperId = $"SCRAPER_{_siteCount}",
            Schedule = schedule ?? new SiteSchedule(),
            KnownFailuresThreshold = knownFailuresThreshold,
        };

        await using var dbContext = CreateDbContext();
        dbContext.Sites.Add(site);
        await dbContext.SaveChangesAsync(cancellationToken);
        return site;
    }

    public async Task SetKnownFailuresThresholdAsync(int siteId, int threshold, CancellationToken cancellationToken)
    {
        await using var dbContext = CreateDbContext();
        var site = await dbContext.Sites.SingleAsync(s => s.Id == siteId, cancellationToken);
        site.KnownFailuresThreshold = threshold;
        await dbContext.SaveChangesAsync(cancellationToken);
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

/// <summary>
/// Records the Site Check status changes the save interceptor would broadcast to clients.
/// </summary>
internal sealed class RecordingEntityChangeService : IEntityChangeService
{
    private readonly List<(int SiteCheckId, CheckStatus Status)> _updates = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// A snapshot of every Site Check update broadcast so far, with the status at broadcast time.
    /// </summary>
    public IReadOnlyList<(int SiteCheckId, CheckStatus Status)> SiteCheckUpdates
    {
        get
        {
            lock (_lock)
            {
                return [.. _updates];
            }
        }
    }

    public Task OnEntityCreated(CreatedEntityChange change, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task OnEntityUpdated(UpdatedEntityChange change, CancellationToken cancellationToken = default)
    {
        if (change.NewEntity is SiteCheck siteCheck)
        {
            lock (_lock)
            {
                _updates.Add((siteCheck.Id, siteCheck.Status));
            }
        }
        return Task.CompletedTask;
    }

    public Task OnEntityDeleted(DeletedEntityChange change, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>
/// Fails the next save that matches <see cref="FailNextSaveWhen"/>, before anything is written.
/// </summary>
internal sealed class SaveFaultInterceptor : SaveChangesInterceptor
{
    public Func<DbContext, bool>? FailNextSaveWhen { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var failWhen = FailNextSaveWhen;
        if (failWhen != null && eventData.Context != null && failWhen(eventData.Context))
        {
            FailNextSaveWhen = null;
            throw new DbUpdateException("Injected save failure");
        }
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

/// <summary>
/// Records the notifications sent to it, regardless of the Site's channel settings.
/// </summary>
internal sealed class RecordingNotificationChannel : INotificationChannel
{
    private readonly List<Notification> _sent = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// When true, every send throws instead of recording.
    /// </summary>
    public bool Fail { get; set; }

    /// <summary>
    /// Runs at the start of every send, before it records or fails (for example, to cancel a token).
    /// </summary>
    public Action? BeforeSend { get; set; }

    /// <summary>
    /// Runs after a notification is recorded, before the send reports success.
    /// </summary>
    public Action? AfterSend { get; set; }

    public IReadOnlyList<Notification> Sent
    {
        get
        {
            lock (_lock)
            {
                return [.. _sent];
            }
        }
    }

    public Task<bool> SendAsync(Notification notification, Site site, CancellationToken cancellationToken)
    {
        BeforeSend?.Invoke();
        if (Fail)
        {
            throw new HttpRequestException("Channel is down");
        }

        lock (_lock)
        {
            _sent.Add(notification);
        }
        AfterSend?.Invoke();
        return Task.FromResult(true);
    }
}
