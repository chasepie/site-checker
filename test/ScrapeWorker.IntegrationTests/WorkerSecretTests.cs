namespace SiteChecker.ScrapeWorker.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using SiteChecker.Scraper;
using SiteChecker.Scraper.Scripts;

/// <summary>
/// The worker only takes requests that carry its secret, and only for its own host names.
/// </summary>
[TestClass]
public sealed class WorkerSecretTests
{
    private const string HelloScript = """
        public sealed class Hello : IScript
        {
            public Task<ScriptOutcome> RunAsync(ScriptContext ctx) => Task.FromResult<ScriptOutcome>("hello");
        }
        """;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    private static ScrapeRequest Request() => new()
    {
        SiteCheckId = 7,
        Site = new ScrapeSite(3, "Site", new Uri("https://example.com/"), UseVpn: false),
        Scraper = new ScriptSpec("Script.cs", HelloScript, ScriptSource.Hash(HelloScript)),
    };

    private Task<HttpResponseMessage> SendAsync(HttpClient client, string action) => action switch
    {
        "scrape" => client.PostAsJsonAsync(ScrapeWorkerEndpoints.ScrapePath, Request(), ScrapeJson.Options, Ct),
        "evict" => client.DeleteAsync($"{ScrapeWorkerEndpoints.ScriptsPath}/3", Ct),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    [TestMethod]
    [DataRow("scrape")]
    [DataRow("evict")]
    public async Task Request_WithoutTheSecret_IsUnauthorized_AndRunsNothing(string action)
    {
        await using var factory = new ScrapeWorkerFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;

        var response = await SendAsync(client, action);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.IsEmpty(factory.OpenedBrowsers);
    }

    [TestMethod]
    [DataRow("scrape")]
    [DataRow("evict")]
    public async Task Request_WithAWrongSecret_IsUnauthorized(string action)
    {
        await using var factory = new ScrapeWorkerFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "not-the-secret");

        var response = await SendAsync(client, action);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Request_WithTheSecret_IsServed()
    {
        await using var factory = new ScrapeWorkerFactory();
        using var client = factory.CreateClient();

        var scrape = await SendAsync(client, "scrape");
        var evict = await SendAsync(client, "evict");

        Assert.AreEqual(HttpStatusCode.OK, scrape.StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, evict.StatusCode);
    }

    [TestMethod]
    public async Task HealthCheck_NeedsNoSecret()
    {
        await using var factory = new ScrapeWorkerFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.GetAsync(ScrapeWorkerEndpoints.HealthPath, Ct);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task Development_WithoutASecret_ServesEveryone()
    {
        await using var factory = new ScrapeWorkerFactory(secret: null);
        using var client = factory.CreateClient();

        var response = await SendAsync(client, "scrape");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task Production_WithoutASecret_FailsStartup()
    {
        await using var factory = new ScrapeWorkerFactory(Environments.Production, secret: null);

        var exception = Assert.Throws<Exception>(() => factory.CreateClient());

        Assert.Contains(ScrapeWorkerSecret.Key, exception.GetBaseException().Message);
    }

    [TestMethod]
    [DataRow("scrape-worker", HttpStatusCode.OK)]
    [DataRow("localhost", HttpStatusCode.OK)]
    [DataRow("rebound.example.com", HttpStatusCode.BadRequest)]
    public async Task OnlyTheWorkersOwnHostNames_AreServed(string host, HttpStatusCode expected)
    {
        await using var factory = new ScrapeWorkerFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri($"http://{host}:8080{ScrapeWorkerEndpoints.HealthPath}"), Ct);

        Assert.AreEqual(expected, response.StatusCode);
    }
}
