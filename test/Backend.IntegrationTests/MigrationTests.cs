namespace SiteChecker.Backend.IntegrationTests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SiteChecker.Database;

[TestClass]
public sealed class MigrationTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [TestMethod]
    public async Task Migrations_CreateAnEmptyDatabase_ThatMatchesTheModel()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(Ct);
        var options = new DbContextOptionsBuilder<SiteCheckerDbContext>().UseSqlite(connection).Options;
        await using var dbContext = new SiteCheckerDbContext(options);

        await dbContext.Database.MigrateAsync(Ct);

        Assert.IsFalse(dbContext.Database.HasPendingModelChanges(),
            "The model has changes that no migration covers; run 'dotnet ef migrations add'.");
        Assert.IsEmpty(await dbContext.Database.GetPendingMigrationsAsync(Ct));
    }
}
