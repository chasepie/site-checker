namespace SiteChecker.Backend.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SiteChecker.Database.Model;
using SiteChecker.Scraper.Scripts;

/// <summary>
/// The Site API, through the real app.
/// </summary>
[TestClass]
public sealed class SiteApiTests
{
    private const string HelloScript = """
        public sealed class Hello : IScript
        {
            public Task<ScriptOutcome> RunAsync(ScriptContext ctx) => Task.FromResult<ScriptOutcome>("hello");
        }
        """;

    private const string GoodbyeScript = """
        public sealed class Goodbye : IScript
        {
            public Task<ScriptOutcome> RunAsync(ScriptContext ctx) => Task.FromResult<ScriptOutcome>("goodbye");
        }
        """;

    // Doesn't implement IScript.RunAsync: CS0535 on line 1.
    private const string BrokenScript = """
        public sealed class Broken : IScript
        {
        }
        """;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    private static object SiteBody(
        string? source = HelloScript,
        string fileName = "Hello.cs",
        int? timeoutSeconds = null,
        int id = 0,
        string name = "Example") => new
        {
            id,
            name,
            url = "https://example.com/",
            useVpn = false,
            alwaysTakeScreenshot = false,
            knownFailuresThreshold = 5,
            timeoutSeconds,
            schedule = new { enabled = false },
            pushoverConfig = new { },
            discordConfig = new { successEnabled = false, failureEnabled = false },
            scraper = new { kind = "Script", script = source is null ? null : new { fileName, source } },
        };

    private async Task<JsonNode> CreateSiteAsync(HttpClient client, object? body = null)
    {
        var response = await client.PostAsJsonAsync("/api/site", body ?? SiteBody(), Ct);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
        return await ReadJsonAsync(response);
    }

    private async Task<JsonNode> ReadJsonAsync(HttpResponseMessage response)
        => JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;

    [TestMethod]
    public async Task CreateSite_StoresItsScript_ButSiteResponsesNeverIncludeTheSource()
    {
        await using var factory = new SiteApiFactory();
        using var client = factory.CreateClient();

        var created = await CreateSiteAsync(client);

        var id = created["id"]!.GetValue<int>();
        var script = created["scraper"]!["script"]!;
        Assert.AreEqual("Script", (string?)created["scraper"]!["kind"]);
        Assert.AreEqual("Hello.cs", (string?)script["fileName"]);
        Assert.AreEqual(ScriptSource.Hash(HelloScript), (string?)script["sourceHash"]);

        foreach (var json in new[]
        {
            created.ToJsonString(),
            await client.GetStringAsync("/api/site", Ct),
            await client.GetStringAsync($"/api/site/{id}", Ct),
        })
        {
            Assert.DoesNotContain("Task.FromResult", json);
        }

        var source = JsonNode.Parse(await client.GetStringAsync($"/api/site/{id}/script", Ct))!;
        Assert.AreEqual(HelloScript, (string?)source["source"]);
    }

    [TestMethod]
    public async Task CreateSite_WithAScriptThatDoesNotCompile_IsRejectedWithItsDiagnostics()
    {
        await using var factory = new SiteApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/site", SiteBody(BrokenScript, "Broken.cs"), Ct);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var diagnostic = (await ReadJsonAsync(response))["diagnostics"]!.AsArray().Single()!;
        Assert.AreEqual("Broken.cs", (string?)diagnostic["fileName"]);
        Assert.AreEqual(1, diagnostic["line"]!.GetValue<int>());
        Assert.AreEqual("CS0535", (string?)diagnostic["id"]);
        await using var dbContext = factory.CreateDbContext();
        Assert.IsFalse(await dbContext.Sites.AnyAsync(Ct));
    }

    [TestMethod]
    public async Task CreateSite_WhoseScraperHasNoScript_IsRejected()
    {
        await using var factory = new SiteApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/site", SiteBody(source: null), Ct);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual("A Script Scraper needs a script file.", (string?)(await ReadJsonAsync(response))["errors"]![0]);
    }

    [TestMethod]
    public async Task CreateSite_WithATimeoutThatDoesNotFitUnderBrowserless_IsRejected()
    {
        await using var factory = new SiteApiFactory();
        using var client = factory.CreateClient();

        // The default BROWSERLESS_TIMEOUT of 180 s leaves at most 170 s.
        var response = await client.PostAsJsonAsync("/api/site", SiteBody(timeoutSeconds: 171), Ct);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("between 1 and 170 seconds", (string?)(await ReadJsonAsync(response))["errors"]![0] ?? "");
    }

    [TestMethod]
    public async Task UpdateSite_WithoutAScriptKeepsTheCurrentOne_AndANewScriptReplacesIt()
    {
        await using var factory = new SiteApiFactory();
        using var client = factory.CreateClient();
        var id = (await CreateSiteAsync(client))["id"]!.GetValue<int>();

        var renamed = await client.PutAsJsonAsync($"/api/site/{id}", SiteBody(source: null, id: id, name: "Renamed", timeoutSeconds: 60), Ct);
        Assert.AreEqual(HttpStatusCode.OK, renamed.StatusCode, await renamed.Content.ReadAsStringAsync(Ct));
        var site = await ReadJsonAsync(renamed);
        Assert.AreEqual("Renamed", (string?)site["name"]);
        Assert.AreEqual(60, site["timeoutSeconds"]!.GetValue<int>());
        Assert.AreEqual(ScriptSource.Hash(HelloScript), (string?)site["scraper"]!["script"]!["sourceHash"]);

        var replaced = await client.PutAsJsonAsync($"/api/site/{id}", SiteBody(GoodbyeScript, "Goodbye.cs", id: id), Ct);
        Assert.AreEqual(HttpStatusCode.OK, replaced.StatusCode, await replaced.Content.ReadAsStringAsync(Ct));
        Assert.AreEqual("Goodbye.cs", (string?)(await ReadJsonAsync(replaced))["scraper"]!["script"]!["fileName"]);
        var source = JsonNode.Parse(await client.GetStringAsync($"/api/site/{id}/script", Ct))!;
        Assert.AreEqual(GoodbyeScript, (string?)source["source"]);
    }

    [TestMethod]
    public async Task DeleteSite_RemovesTheSiteWithItsChecksAndScript()
    {
        await using var factory = new SiteApiFactory();
        using var client = factory.CreateClient();
        var id = (await CreateSiteAsync(client))["id"]!.GetValue<int>();
        await using (var dbContext = factory.CreateDbContext())
        {
            var site = await dbContext.Sites.SingleAsync(s => s.Id == id, Ct);
            dbContext.SiteChecks.Add(new SiteCheck(site, DateTime.UtcNow));
            await dbContext.SaveChangesAsync(Ct);
        }

        var response = await client.DeleteAsync($"/api/site/{id}", Ct);

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync($"/api/site/{id}", Ct)).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync($"/api/site/{id}/script", Ct)).StatusCode);
        await using var check = factory.CreateDbContext();
        Assert.IsFalse(await check.SiteChecks.AnyAsync(Ct));
        Assert.IsFalse(await check.SiteScripts.AnyAsync(Ct));
    }

    [TestMethod]
    public async Task StartTestRun_IsAccepted_ForAValidScript_AndRejectedWithDiagnosticsForABrokenOne()
    {
        await using var factory = new SiteApiFactory();
        using var client = factory.CreateClient();
        object TestRun(string source) => new
        {
            testRunId = "run-1",
            connectionId = "connection-1",
            url = "https://example.com/",
            scraper = new { kind = "Script", script = new { fileName = "Draft.cs", source } },
        };

        var accepted = await client.PostAsJsonAsync("/api/site/test-run", TestRun(HelloScript), Ct);
        var rejected = await client.PostAsJsonAsync("/api/site/test-run", TestRun(BrokenScript), Ct);

        Assert.AreEqual(HttpStatusCode.Accepted, accepted.StatusCode, await accepted.Content.ReadAsStringAsync(Ct));
        Assert.AreEqual(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.AreEqual("CS0535", (string?)(await ReadJsonAsync(rejected))["diagnostics"]![0]!["id"]);
    }
}
