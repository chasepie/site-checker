namespace SiteChecker.Backend.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using SiteChecker.Backend.Services.Scraping;
using SiteChecker.Backend.Services.Security;
using SiteChecker.Scraper;

/// <summary>
/// The login, the antiforgery and rate limits around it, the hub's origin check, and the startup
/// checks on the security settings.
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

    private const string NegotiatePath = "/dataHub/negotiate?negotiateVersion=1";

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    private static object SiteBody() => new
    {
        id = 0,
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

    private Task<HttpResponseMessage> LoginAsync(HttpClient client, string password)
        => client.PostAsJsonAsync("/api/auth/login", new { password }, Ct);

    private async Task<JsonNode> GetSessionAsync(HttpClient client)
        => JsonNode.Parse(await client.GetStringAsync("/api/auth/session", Ct))!;

    // ---- Login ----

    [TestMethod]
    [DataRow("GET", "/api/site")]
    [DataRow("GET", "/api/site/1/check")]
    [DataRow("GET", "/api/vpn/CurrentLocation")]
    [DataRow("DELETE", "/api/site/1")]
    [DataRow("POST", NegotiatePath)]
    public async Task Endpoint_WithoutALogin_IsUnauthorized(string method, string path)
    {
        await using var factory = new SiteApiFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Ct);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Login_WithTheWrongPassword_IsUnauthorized_AndOpensNothing()
    {
        await using var factory = new SiteApiFactory();
        using var client = factory.CreateClient();

        var login = await LoginAsync(client, "not-the-password");

        Assert.AreEqual(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/site", Ct)).StatusCode);
        Assert.IsFalse((await GetSessionAsync(client))["loggedIn"]!.GetValue<bool>());
    }

    [TestMethod]
    public async Task Login_WithThePassword_OpensTheApp_WithAStrictHttpOnlyCookie()
    {
        await using var factory = new SiteApiFactory();
        using var client = factory.CreateClient();

        var login = await LoginAsync(client, $"  {SiteApiFactory.Password} ");

        Assert.AreEqual(HttpStatusCode.NoContent, login.StatusCode);
        var sessionCookie = login.Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith(SecurityExtensions.SessionCookieName + "=", StringComparison.Ordinal))
            .ToLowerInvariant();
        Assert.Contains("httponly", sessionCookie);
        Assert.Contains("samesite=strict", sessionCookie);
        Assert.Contains("expires=", sessionCookie);
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/api/site", Ct)).StatusCode);
        var session = await GetSessionAsync(client);
        Assert.IsTrue(session["loginRequired"]!.GetValue<bool>());
        Assert.IsTrue(session["loggedIn"]!.GetValue<bool>());
    }

    [TestMethod]
    public async Task Write_WithoutTheAntiforgeryToken_IsRejected()
    {
        await using var factory = new SiteApiFactory();
        using var client = await factory.CreateLoggedInClientAsync(Ct);
        client.DefaultRequestHeaders.Remove(SecurityExtensions.AntiforgeryHeaderName);

        var response = await client.PostAsJsonAsync("/api/site", SiteBody(), Ct);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.IsFalse(await AnySiteAsync(factory));
    }

    [TestMethod]
    public async Task Write_WithTheAntiforgeryToken_IsAccepted()
    {
        await using var factory = new SiteApiFactory();
        using var client = await factory.CreateLoggedInClientAsync(Ct);

        var response = await client.PostAsJsonAsync("/api/site", SiteBody(), Ct);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
    }

    [TestMethod]
    public async Task Logout_EndsTheSession()
    {
        await using var factory = new SiteApiFactory();
        using var client = await factory.CreateLoggedInClientAsync(Ct);

        var logout = await client.PostAsync("/api/auth/logout", content: null, Ct);

        Assert.AreEqual(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/site", Ct)).StatusCode);
    }

    [TestMethod]
    public async Task Login_AfterTooManyAttemptsInAMinute_IsTooManyRequests_EvenWithThePassword()
    {
        await using var factory = new SiteApiFactory();
        using var client = factory.CreateClient();

        for (var i = 0; i < SecurityExtensions.LoginAttemptsPerMinute; i++)
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await LoginAsync(client, $"guess-{i}")).StatusCode);
        }
        var limited = await LoginAsync(client, SiteApiFactory.Password);

        Assert.AreEqual(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [TestMethod]
    public async Task PublicEndpoints_NeedNoLogin()
    {
        await using var factory = new SiteApiFactory();
        using var client = factory.CreateClient();

        var health = await client.GetAsync("/healthz", Ct);
        var session = await client.GetAsync("/api/auth/session", Ct);
        var spa = await client.GetAsync("/sites/new", Ct);

        Assert.AreEqual(HttpStatusCode.OK, health.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, session.StatusCode);
        Assert.AreNotEqual(HttpStatusCode.Unauthorized, spa.StatusCode);
    }

    [TestMethod]
    public async Task Development_WithoutAPassword_IsOpen_ButStillChecksAntiforgery()
    {
        await using var factory = new SiteApiFactory(settings: new Dictionary<string, string?>
        {
            [AdminPassword.AdminPasswordKey] = null,
        });
        using var client = factory.CreateClient();

        var sessionResponse = await client.GetAsync("/api/auth/session", Ct);
        var session = JsonNode.Parse(await sessionResponse.Content.ReadAsStringAsync(Ct))!;
        var withoutToken = await client.PostAsJsonAsync("/api/site", SiteBody(), Ct);
        client.DefaultRequestHeaders.Add(SecurityExtensions.AntiforgeryHeaderName, SiteApiFactory.ReadAntiforgeryToken(sessionResponse));
        var withToken = await client.PostAsJsonAsync("/api/site", SiteBody(), Ct);

        Assert.IsFalse(session["loginRequired"]!.GetValue<bool>());
        Assert.IsTrue(session["loggedIn"]!.GetValue<bool>());
        Assert.AreEqual(HttpStatusCode.BadRequest, withoutToken.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, withToken.StatusCode);
    }

    [TestMethod]
    public async Task Production_WithoutAPassword_FailsStartup()
    {
        await using var factory = new SiteApiFactory(Environments.Production, new Dictionary<string, string?>
        {
            [AdminPassword.AdminPasswordKey] = null,
            [SecurityExtensions.AllowedHostsKey] = "sitechecker.lan",
        });

        var exception = Assert.Throws<Exception>(() => factory.CreateClient());

        Assert.Contains(AdminPassword.AdminPasswordKey, exception.GetBaseException().Message);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("-1")]
    [DataRow("a week")]
    public async Task InvalidSessionDays_FailsStartup(string sessionDays)
    {
        await using var factory = new SiteApiFactory(settings: new Dictionary<string, string?>
        {
            [SecurityExtensions.SessionDaysKey] = sessionDays,
        });

        var exception = Assert.Throws<Exception>(() => factory.CreateClient());

        Assert.Contains(SecurityExtensions.SessionDaysKey, exception.GetBaseException().Message);
    }

    // ---- The hub's origin check ----

    [TestMethod]
    [DataRow("http://localhost:8080", HttpStatusCode.OK)]
    [DataRow("https://sitechecker.lan", HttpStatusCode.OK)]
    [DataRow("https://other-machine.tailnet.ts.net", HttpStatusCode.Forbidden)]
    [DataRow("null", HttpStatusCode.Forbidden)]
    public async Task Hub_FromAPageOnAnotherHost_IsForbidden(string origin, HttpStatusCode expected)
    {
        await using var factory = new SiteApiFactory(settings: new Dictionary<string, string?>
        {
            [SecurityExtensions.AllowedHostsKey] = "sitechecker.lan",
        });
        using var client = await factory.CreateLoggedInClientAsync(Ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, NegotiatePath);
        request.Headers.Add("Origin", origin);

        var response = await client.SendAsync(request, Ct);

        Assert.AreEqual(expected, response.StatusCode);
    }

    private static async Task<bool> AnySiteAsync(SiteApiFactory factory)
    {
        await using var dbContext = factory.CreateDbContext();
        return dbContext.Sites.Any();
    }

    // ---- Startup checks ----

    [TestMethod]
    [DataRow(null)]
    [DataRow("*")]
    [DataRow("sitechecker.lan;*")]
    [DataRow("0.0.0.0")]
    [DataRow("[::]")]
    [DataRow("sitechecker.lan;0.0.0.0")]
    [DataRow("sitechecker.lan; [::]")]
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
    public async Task Production_WithAScrapeWorkerButNoSecret_FailsStartup()
    {
        await using var factory = new SiteApiFactory(Environments.Production, new Dictionary<string, string?>
        {
            [SecurityExtensions.AllowedHostsKey] = "sitechecker.lan",
            [RemoteScraperService.ScrapeWorkerUrlKey] = "http://scrape-worker:8080",
            [ScrapeWorkerSecret.Key] = null,
        });

        var exception = Assert.Throws<Exception>(() => factory.CreateClient());

        Assert.Contains(ScrapeWorkerSecret.Key, exception.GetBaseException().Message);
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

        // /healthz needs no login, so this checks the host alone.
        var response = await client.GetAsync(new Uri($"http://{host}/healthz"), Ct);

        Assert.AreEqual(expected, response.StatusCode);
    }
}
