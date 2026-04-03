using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SiteChecker.Domain.Entities;
using SiteChecker.Utilities;

namespace SiteChecker.Database;

public class SiteCheckerDbContext : DbContext
{
    public DbSet<Site> Sites { get; set; }
    public DbSet<SiteCheck> SiteChecks { get; set; }
    public DbSet<SiteCheckScreenshot> SiteCheckScreenshots { get; set; }

    private readonly string _dbPath;

    public SiteCheckerDbContext()
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
        _dbPath = Path.Join(dbDir, "SiteChecker.db");
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Site unique index on ScraperId (replaces [Index] attribute)
        modelBuilder.Entity<Site>()
            .HasIndex(s => s.ScraperId).IsUnique();

        // Site complex properties stored as JSON
        modelBuilder.Entity<Site>()
            .ComplexProperty(s => s.Schedule, b => b.ToJson());

        modelBuilder.Entity<Site>()
            .ComplexProperty(s => s.PushoverConfig, b => b.ToJson());

        modelBuilder.Entity<Site>()
            .ComplexProperty(s => s.DiscordConfig, b => b.ToJson());

        modelBuilder.Entity<Site>()
            .Property(s => s.KnownFailuresThreshold)
            .HasDefaultValue(Site.KnownFailuresThresholdDefault);

        // Site → SiteChecks (one-to-many), no navigation properties on entities
        modelBuilder.Entity<Site>()
            .HasMany<SiteCheck>()
            .WithOne()
            .HasForeignKey(sc => sc.SiteId)
            .OnDelete(DeleteBehavior.Cascade);

        // SiteCheck → SiteCheckScreenshot (one-to-one), no navigation properties on entities
        modelBuilder.Entity<SiteCheckScreenshot>()
            .HasIndex(s => s.SiteCheckId).IsUnique();

        modelBuilder.Entity<SiteCheckScreenshot>()
            .HasOne<SiteCheck>()
            .WithOne()
            .HasForeignKey<SiteCheckScreenshot>(s => s.SiteCheckId)
            .OnDelete(DeleteBehavior.Cascade);

        // SiteCheck.Metadata stored as JSON string
        JsonSerializerOptions? options = null;
        modelBuilder.Entity<SiteCheck>()
            .Property(s => s.Metadata)
            .HasConversion(
                v => JsonSerializer.Serialize(v, options),
                v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, options)
                    ?? new Dictionary<string, string>());
    }
}
