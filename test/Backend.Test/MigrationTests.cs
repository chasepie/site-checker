namespace SiteChecker.Backend.Test;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SiteChecker.Database;
using SiteChecker.Database.Model;

[TestClass]
public sealed class MigrationTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [TestMethod]
    public async Task AddSiteCheckFailureKind_BackfillsFromRecordedExceptionType()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(Ct);
        var options = new DbContextOptionsBuilder<SiteCheckerDbContext>().UseSqlite(connection).Options;
        await using var dbContext = new SiteCheckerDbContext(options);
        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync("AddSiteCheckQueueIndex", cancellationToken: Ct);

        // Rows as the previous code wrote them: Known Failures only identifiable by type name.
        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO Sites (Id, Name, Url, ScraperId, UseVpn, AlwaysTakeScreenshot, KnownFailuresThreshold, Schedule, PushoverConfig, DiscordConfig)
            VALUES (1, 'Site', 'https://example.com', 'SCRAPER', 0, 0, 5, '{}', '{}', '{}');
            INSERT INTO SiteChecks (Id, Value, Status, StartDate, DoneDate, SiteId, Metadata) VALUES
                (1, 'content', 3, '2026-01-01', '2026-01-01', 1, '{}'),
                (2, 'Access Denied', 4, '2026-01-01', '2026-01-01', 1, '{"EXCEPTION_TYPE":"SiteChecker.Scraper.Exceptions.AccessDeniedScraperException"}'),
                (3, 'Blank Page', 4, '2026-01-01', '2026-01-01', 1, '{"EXCEPTION_TYPE":"SiteChecker.Scraper.Exceptions.BlankPageScraperException"}'),
                (4, 'boom', 4, '2026-01-01', '2026-01-01', 1, '{"EXCEPTION_TYPE":"SiteChecker.Scraper.Exceptions.UnexpectedScraperException"}'),
                (5, 'crashed', 4, '2026-01-01', '2026-01-01', 1, '{}');
            """;
        await insert.ExecuteNonQueryAsync(Ct);

        await migrator.MigrateAsync(cancellationToken: Ct);

        var kinds = await dbContext.SiteChecks
            .OrderBy(sc => sc.Id)
            .Select(sc => sc.FailureKind)
            .ToListAsync(Ct);
        CollectionAssert.AreEqual(
            new FailureKind?[] { null, FailureKind.Known, FailureKind.Known, FailureKind.Unexpected, FailureKind.Unexpected },
            kinds);
    }
}
