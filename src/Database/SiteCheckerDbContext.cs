using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SiteChecker.Database.Model;
using SiteChecker.Database.Services;
using SiteChecker.Utilities;

namespace SiteChecker.Database;

public class SiteCheckerDbContext : DbContext
{
    public DbSet<Site> Sites { get; set; }
    public DbSet<SiteCheck> SiteChecks { get; set; }
    public DbSet<SiteCheckScreenshot> SiteCheckScreenshots { get; set; }
    public DbSet<SiteScript> SiteScripts { get; set; }

    private readonly IEnumerable<IEntityChangeService> _entityUpdateServices;

    /// <summary>
    /// Used by design-time tooling (<c>dotnet ef</c>); connects to the default database file.
    /// </summary>
    public SiteCheckerDbContext(
        IEnumerable<IEntityChangeService>? entityUpdateServices = null)
    {
        _entityUpdateServices = entityUpdateServices ?? [];
    }

    /// <summary>
    /// Used by dependency injection. If <paramref name="options"/> configures no database
    /// provider, the default database file is used.
    /// </summary>
    public SiteCheckerDbContext(
        DbContextOptions<SiteCheckerDbContext> options,
        IEnumerable<IEntityChangeService>? entityUpdateServices = null)
        : base(options)
    {
        _entityUpdateServices = entityUpdateServices ?? [];
    }

    private static string GetDefaultDbPath()
    {
        string dbDir;

        if (!EnvironmentUtils.IsDockerContainer()
            && RepoUtils.TryGetRepoDirectory(out var repoRoot))
        {
            dbDir = Path.Join(repoRoot, "site-checker/data");
        }
        else
        {
            dbDir = Path.Join(AppContext.BaseDirectory, "data");
        }

        if (!Directory.Exists(dbDir))
        {
            Directory.CreateDirectory(dbDir);
        }
        return Path.Join(dbDir, "SiteChecker.db");
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite($"Data Source={GetDefaultDbPath()}");
        }
        optionsBuilder.AddInterceptors(new ChangesInterceptor(_entityUpdateServices));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Site>()
            .ComplexProperty(s => s.Schedule, b => b.ToJson());

        modelBuilder.Entity<Site>()
            .ComplexProperty(s => s.PushoverConfig, b => b.ToJson());

        modelBuilder.Entity<Site>()
            .ComplexProperty(s => s.DiscordConfig, b => b.ToJson());

        modelBuilder.Entity<Site>()
            .ComplexProperty(s => s.Scraper, b =>
            {
                b.ToJson();
                b.ComplexProperty(d => d.Script);
            });

        modelBuilder.Entity<SiteScript>(b =>
        {
            b.HasKey(s => s.SiteId);
            b.HasOne(s => s.Site)
                .WithOne(s => s.SiteScript)
                .HasForeignKey<SiteScript>(s => s.SiteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Site>()
            .Property(s => s.KnownFailuresThreshold)
            .HasDefaultValue(SiteUpdate.KNOWN_FAILURES_THRESHOLD_DEFAULT);

        JsonSerializerOptions? options = null;
        modelBuilder.Entity<SiteCheck>()
            .Property(s => s.Metadata)
            .HasConversion(
                v => JsonSerializer.Serialize(v, options),
                v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, options)
                    ?? new Dictionary<string, string>());
    }
}
