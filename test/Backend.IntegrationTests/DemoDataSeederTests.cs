namespace SiteChecker.Backend.IntegrationTests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using SiteChecker.Backend.Services;
using SiteChecker.Database;
using SiteChecker.Database.Model;
using SiteChecker.Scraper.Scripts;
using SiteChecker.Utilities;

[TestClass]
public sealed class DemoDataSeederTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    private static DemoDataSeeder CreateSeeder(
        RunnerHarness harness,
        SiteCheckerDbContext dbContext,
        string? seedDemoData,
        string environmentName = "Production")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DemoDataSeeder.SeedDemoDataKey] = seedDemoData })
            .Build();
        var environment = new HostingEnvironment { EnvironmentName = environmentName };
        return new DemoDataSeeder(dbContext, configuration, environment, harness.Time, NullLogger<DemoDataSeeder>.Instance);
    }

    [TestMethod]
    public async Task SeedsTheTwoDemoSites_WithTheirSettingsAndScripts_IntoAnEmptyDatabase()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        await using (var dbContext = harness.CreateDbContext())
        {
            await CreateSeeder(harness, dbContext, seedDemoData: "true").SeedAsync(Ct);
        }

        await using var check = harness.CreateDbContext();
        var sites = await check.Sites.Include(s => s.SiteScript).OrderBy(s => s.Name).ToListAsync(Ct);
        Assert.AreEqual("Bot Detection, PIA Location", string.Join(", ", sites.Select(s => s.Name)));

        var (botDetection, piaLocation) = (sites[0], sites[1]);
        Assert.IsFalse(botDetection.UseVpn);
        Assert.IsTrue(piaLocation.UseVpn);
        foreach (var (site, fileName) in new[] { (botDetection, "BotDetection.cs"), (piaLocation, "PiaLocation.cs") })
        {
            Assert.IsTrue(site.AlwaysTakeScreenshot);
            Assert.IsFalse(site.Schedule.Enabled);
            Assert.AreEqual(ScraperKind.Script, site.Scraper.Kind);
            Assert.AreEqual(fileName, site.Scraper.Script!.FileName);
            Assert.AreEqual(harness.Time.GetUtcNow().UtcDateTime, site.Scraper.Script.UploadedAt);

            // The seeded source is the sample file itself, embedded in the Backend.
            Assert.IsTrue(RepoUtils.TryGetRepoDirectory(out var repo));
            var sample = await File.ReadAllTextAsync(Path.Join(repo, "samples/DemoScrapers", fileName), Ct);
            Assert.AreEqual(sample, site.SiteScript!.Source);
            Assert.AreEqual(ScriptSource.Hash(sample), site.Scraper.Script.SourceHash);
        }
        Assert.IsEmpty(await check.SiteChecks.ToListAsync(Ct));
    }

    [TestMethod]
    [DataRow("Development", null, true)]
    [DataRow("Production", null, false)]
    [DataRow("Development", "false", false)]
    [DataRow("Production", "true", true)]
    public async Task Seeding_FollowsSeedDemoData_AndDefaultsToDevelopmentOnly(string environment, string? seedDemoData, bool seeds)
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        await using var dbContext = harness.CreateDbContext();
        var seeder = CreateSeeder(harness, dbContext, seedDemoData, environment);

        await seeder.SeedAsync(Ct);

        Assert.AreEqual(seeds, seeder.IsEnabled);
        Assert.AreEqual(seeds ? 2 : 0, await dbContext.Sites.CountAsync(Ct));
    }

    [TestMethod]
    public async Task LeavesADatabaseWithAnySiteUnchanged()
    {
        await using var harness = await RunnerHarness.CreateAsync(Ct);
        var existing = await harness.AddSiteAsync(null, Ct);
        await using var dbContext = harness.CreateDbContext();

        await CreateSeeder(harness, dbContext, seedDemoData: "true").SeedAsync(Ct);

        var site = Assert.ContainsSingle(await dbContext.Sites.ToListAsync(Ct));
        Assert.AreEqual(existing.Id, site.Id);
    }
}
