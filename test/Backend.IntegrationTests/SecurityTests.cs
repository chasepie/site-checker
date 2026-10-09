namespace SiteChecker.Backend.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using SiteChecker.Backend.Services.Security;

/// <summary>
/// The admin token on the actions that upload or run a script, and host filtering.
/// </summary>
[TestClass]
public sealed class SecurityTests
{
    private const string HelloScript = """
        public sealed class Hello : IScript
        {
            public Task<ScriptOutcome> RunAsync(ScriptContext ctx) => Task.FromResult<ScriptOutcome>("hello");
        }
        """;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    private static object SiteBody(int id = 0) => new
    {
        id,
        name = "Example",
        url = "https://example.com/",
        useVpn = false,
        alwaysTakeScreenshot = false,
        knownFailuresThreshold = 5,
        schedule = new { enabled = false },
        pushoverConfig = new { },
        discordConfig = new { successEnabled = false, failureEnabled = false },
        scraper = new { kind = "Script", script = new { fileName = "Hello.cs", source = HelloScript } },
    };

    private async Task<int> CreateSiteAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/site", SiteBody(), Ct);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["id"]!.GetValue<int>();
    }

    private static Task<HttpResponseMessage> SendGatedAsync(HttpClient client, string action, int siteId, CancellationToken ct) => action switch
    {
        "create" => client.PostAsJsonAsync("/api/site", SiteBody(), ct),
        "update" => client.PutAsJsonAsync($"/api/site/{siteId}", SiteBody(siteId), ct),
        "test-run" => client.PostAsJsonAsync("/api/site/test-run", new { }, ct),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    [TestMethod]
    [DataRow("create")]
    [DataRow("update")]
    [DataRow("test-run")]
    public async Task GatedAction_WithoutTheToken_IsUnauthorized(string action)
    {
        await using var factory = new SiteApiFactory();
        using var admin = factory.CreateClient();
        var siteId = await CreateSiteAsync(admin);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;

        var response = await SendGatedAsync(client, action, siteId, Ct);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    [DataRow("create")]
    [DataRow("update")]
    [DataRow("test-run")]
    public async Task GatedAction_WithAWrongToken_IsUnauthorized(string action)
    {
        await using var factory = new SiteApiFactory();
        using var admin = factory.CreateClient();
        var siteId = await CreateSiteAsync(admin);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "not-the-token");

        var response = await SendGatedAsync(client, action, siteId, Ct);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ActionsThatDontUploadOrRunScripts_NeedNoToken()
    {
        await using var factory = new SiteApiFactory();
        using var admin = factory.CreateClient();
        var siteId = await CreateSiteAsync(admin);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;

        var list = await client.GetAsync("/api/site", Ct);
        var script = await client.GetAsync($"/api/site/{siteId}/script", Ct);
        var delete = await client.DeleteAsync($"/api/site/{siteId}", Ct);

        Assert.AreEqual(HttpStatusCode.OK, list.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, script.StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [TestMethod]
    public async Task Development_WithoutAToken_LeavesGatedActionsOpen()
    {
        await using var factory = new SiteApiFactory(settings: new Dictionary<string, string?>
        {
            [AdminToken.AdminTokenKey] = null,
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;

        await CreateSiteAsync(client);
    }

    [TestMethod]
    public async Task Production_WithoutAToken_FailsStartup()
    {
        await using var factory = new SiteApiFactory(Environments.Production, new Dictionary<string, string?>
        {
            [AdminToken.AdminTokenKey] = null,
            [SecurityExtensions.AllowedHostsKey] = "sitechecker.lan",
        });

        var exception = Assert.Throws<Exception>(() => factory.CreateClient());

        Assert.Contains(AdminToken.AdminTokenKey, exception.GetBaseException().Message);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("*")]
    [DataRow("sitechecker.lan;*")]
    public async Task Production_WithoutSpecificAllowedHosts_FailsStartup(string? allowedHosts)
    {
        await using var factory = new SiteApiFactory(Environments.Production, new Dictionary<string, string?>
        {
            [SecurityExtensions.AllowedHostsKey] = allowedHosts,
        });

        var exception = Assert.Throws<Exception>(() => factory.CreateClient());

        Assert.Contains(SecurityExtensions.AllowedHostsKey, exception.GetBaseException().Message);
    }

    [TestMethod]
    [DataRow("sitechecker.lan", HttpStatusCode.OK)]
    [DataRow("SiteChecker.ts.net", HttpStatusCode.OK)]
    [DataRow("localhost", HttpStatusCode.OK)]
    [DataRow("rebound.example.com", HttpStatusCode.BadRequest)]
    public async Task AllowedHosts_RejectOtherHostHeaders(string host, HttpStatusCode expected)
    {
        await using var factory = new SiteApiFactory(settings: new Dictionary<string, string?>
        {
            [SecurityExtensions.AllowedHostsKey] = "sitechecker.lan; sitechecker.ts.net",
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri($"http://{host}/api/site"), Ct);

        Assert.AreEqual(expected, response.StatusCode);
    }
}
