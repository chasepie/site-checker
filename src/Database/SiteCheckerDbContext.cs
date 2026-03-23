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

    private readonly string _dbPath;
    private readonly IEnumerable<IEntityChangeService> _entityUpdateServices;

    public SiteCheckerDbContext(
        IEnumerable<IEntityChangeService>? entityUpdateServices = null)
    {
        _entityUpdateServices = entityUpdateServices ?? [];
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
        _dbPath = Path.Join(dbDir, "SiteChecker.db");
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");
        if (_entityUpdateServices != null)
        {
            optionsBuilder.AddInterceptors(new ChangesInterceptor(_entityUpdateServices));
        }
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
