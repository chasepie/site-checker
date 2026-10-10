namespace SiteChecker.Backend.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using SiteChecker.Backend.Services.Scraping;
using SiteChecker.Backend.Services.Security;
using SiteChecker.Backend.Services.SignalR;
using SiteChecker.Scraper;

/// <summary>
/// The login, the antiforgery and rate limits around it, trusted proxies, the hub's origin check,
/// and the startup checks on the security settings.
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

    /// <summary>
    /// Logs in from <paramref name="remoteAddress"/>, with any other headers, such as a proxy's
    /// forwarded ones.
    /// </summary>
    private Task<HttpResponseMessage> LoginFromAsync(
        HttpClient client, string remoteAddress, string password, params (string Name, string Value)[] headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { password }) };
        request.Headers.Add(SiteApiFactory.RemoteAddressHeader, remoteAddress);
        foreach (var (name, value) in headers)
        {
            request.Headers.Add(name, value);
        }
        return client.SendAsync(request, Ct);
    }

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
    public async Task ChangingThePassword_EndsSessionsMadeWithTheOldOne()
    {
        var keys = Path.Combine(Path.GetTempPath(), $"site-checker-keys-{Guid.NewGuid():N}");
        try
        {
            string sessionCookie;
            await using (var before = new SiteApiFactory(settings: Password("old-password"), keysDirectory: keys))
            {
                using var client = before.CreateClient();
                var login = await LoginAsync(client, "old-password");
                Assert.AreEqual(HttpStatusCode.NoContent, login.StatusCode);
                sessionCookie = login.Headers.GetValues("Set-Cookie")
                    .Single(c => c.StartsWith(SecurityExtensions.SessionCookieName + "=", StringComparison.Ordinal))
                    .Split(';')[0];
            }

            // A restart with the same keys: the same password still works, a new one doesn't.
            Assert.AreEqual(HttpStatusCode.OK, await GetSitesWithCookieAsync(Password("old-password"), keys, sessionCookie));
            Assert.AreEqual(HttpStatusCode.Unauthorized, await GetSitesWithCookieAsync(Password("new-password"), keys, sessionCookie));
        }
        finally
        {
            // Missing if the first factory failed before writing a key; don't hide that failure.
            if (Directory.Exists(keys))
            {
                Directory.Delete(keys, recursive: true);
            }
        }
    }

    private static Dictionary<string, string?> Password(string password) => new()
    {
        [AdminPassword.AdminPasswordKey] = password,
    };

    private async Task<HttpStatusCode> GetSitesWithCookieAsync(Dictionary<string, string?> settings, string keys, string sessionCookie)
    {
        await using var factory = new SiteApiFactory(settings: settings, keysDirectory: keys);
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/site");
        request.Headers.Add("Cookie", sessionCookie);
        return (await client.SendAsync(request, Ct)).StatusCode;
    }

    [TestMethod]
    public async Task Logout_WithoutALogin_IsUnauthorized_AndClosesNoConnections()
    {
        await using var factory = new SiteApiFactory();
        var connection = new FakeHubConnection();
        factory.Services.GetRequiredService<HubConnections>().Add(connection);
        using var client = factory.CreateClient();
        // A valid antiforgery token, as anyone can get one from the session endpoint.
        var session = await client.GetAsync("/api/auth/session", Ct);
        client.DefaultRequestHeaders.Add(SecurityExtensions.AntiforgeryHeaderName, SiteApiFactory.ReadAntiforgeryToken(session));

        var logout = await client.PostAsync("/api/auth/logout", content: null, Ct);

        Assert.AreEqual(HttpStatusCode.Unauthorized, logout.StatusCode);
        Assert.IsFalse(connection.Aborted);
    }

    [TestMethod]
    public async Task Logout_ClosesEveryHubConnection()
    {
        await using var factory = new SiteApiFactory();
        var connection = new FakeHubConnection();
        factory.Services.GetRequiredService<HubConnections>().Add(connection);
        using var client = await factory.CreateLoggedInClientAsync(Ct);

        var logout = await client.PostAsync("/api/auth/logout", content: null, Ct);

        Assert.AreEqual(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.IsTrue(connection.Aborted);
    }

    private sealed class FakeHubConnection : HubCallerContext
    {
        public bool Aborted { get; private set; }

        public override string ConnectionId { get; } = Guid.NewGuid().ToString("N");

        public override string? UserIdentifier => null;

        public override ClaimsPrincipal? User => null;

        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

        public override IFeatureCollection Features { get; } = new FeatureCollection();

        public override CancellationToken ConnectionAborted => CancellationToken.None;

        public override void Abort() => Aborted = true;
    }

    [TestMethod]
    public async Task Login_AfterTooManyAttemptsInAMinute_IsTooManyRequests_EvenWithThePassword()
    {
        await using var factory = new SiteApiFactory();
        using var client = factory.CreateClient();

        for (var i = 0; i < LoginThrottle.AttemptsPerClient; i++)
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await LoginAsync(client, $"guess-{i}")).StatusCode);
        }
        var limited = await LoginAsync(client, SiteApiFactory.Password);

        Assert.AreEqual(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [TestMethod]
    public async Task Login_LimitedForOneClient_StillWorksForAnother()
    {
        await using var factory = new SiteApiFactory();
        using var client = factory.CreateClient();

        for (var i = 0; i < LoginThrottle.AttemptsPerClient; i++)
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await LoginFromAsync(client, "192.0.2.1", $"guess-{i}")).StatusCode);
        }
        var limited = await LoginFromAsync(client, "192.0.2.1", SiteApiFactory.Password);
        var other = await LoginFromAsync(client, "192.0.2.2", SiteApiFactory.Password);

        Assert.AreEqual(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, other.StatusCode);
    }

    [TestMethod]
    public async Task Login_AfterTooManyAttemptsFromAllClients_IsTooManyRequests_EvenFromANewOne()
    {
        await using var factory = new SiteApiFactory();
        using var client = factory.CreateClient();

        for (var i = 0; i < LoginThrottle.AttemptsInAll; i++)
        {
            var from = $"192.0.2.{1 + (i / LoginThrottle.AttemptsPerClient)}";
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await LoginFromAsync(client, from, $"guess-{i}")).StatusCode);
        }
        var limited = await LoginFromAsync(client, "198.51.100.1", SiteApiFactory.Password);

        Assert.AreEqual(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [TestMethod]
    public async Task Login_RefusedForItsClient_DoesNotCountTowardTheLimitInAll()
    {
        await using var factory = new SiteApiFactory();
        using var client = factory.CreateClient();

        for (var i = 0; i < LoginThrottle.AttemptsInAll; i++)
        {
            await LoginFromAsync(client, "192.0.2.1", $"guess-{i}");
        }
        var other = await LoginFromAsync(client, "192.0.2.2", SiteApiFactory.Password);

        Assert.AreEqual(HttpStatusCode.NoContent, other.StatusCode);
    }

    [TestMethod]
    public async Task Login_RequestsThatCantLogIn_DoNotUseUpAttempts()
    {
        await using var factory = new SiteApiFactory();
        using var client = factory.CreateClient();

        // A page on another site can send this without a CORS preflight, but not a JSON body.
        for (var i = 0; i < LoginThrottle.AttemptsInAll; i++)
        {
            using var formPost = new StringContent($"{{\"password\":\"guess-{i}\"}}", Encoding.UTF8, "text/plain");
            Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, (await client.PostAsync("/api/auth/login", formPost, Ct)).StatusCode);
        }
        var login = await LoginAsync(client, SiteApiFactory.Password);

        Assert.AreEqual(HttpStatusCode.NoContent, login.StatusCode);
    }

    // ---- Trusted proxies ----

    private static Dictionary<string, string?> TrustedProxies(string? proxies) => new()
    {
        [SecurityExtensions.TrustedProxiesKey] = proxies,
    };

    private static bool IsSecure(string setCookie)
        => setCookie.Split(';').Skip(1).Any(attribute => attribute.Trim().Equals("secure", StringComparison.OrdinalIgnoreCase));

    [TestMethod]
    [DataRow("10.0.0.1", "10.0.0.1", true)]
    // Dual-mode sockets report IPv4 clients like this.
    [DataRow("10.0.0.0/24", "::ffff:10.0.0.7", true)]
    [DataRow("fd00::/64", "fd00::5", true)]
    [DataRow("10.0.0.1", "10.0.0.2", false)]
    [DataRow(null, "10.0.0.1", false)]
    public async Task Cookies_BehindAProxyServingHttps_AreSecure_OnlyIfItsTrusted(string? trustedProxies, string proxy, bool secure)
    {
        await using var factory = new SiteApiFactory(settings: TrustedProxies(trustedProxies));
        using var client = factory.CreateClient();

        var login = await LoginFromAsync(client, proxy, SiteApiFactory.Password,
            ("X-Forwarded-For", "192.0.2.1"), ("X-Forwarded-Proto", "https"));

        Assert.AreEqual(HttpStatusCode.NoContent, login.StatusCode);
        var cookies = login.Headers.GetValues("Set-Cookie").ToList();
        Assert.AreEqual(secure, IsSecure(cookies.Single(c => c.StartsWith(SecurityExtensions.SessionCookieName + "=", StringComparison.Ordinal))));
        Assert.AreEqual(secure, IsSecure(cookies.Single(c => c.StartsWith(SecurityExtensions.AntiforgeryCookieName + "=", StringComparison.Ordinal))));
    }

    [TestMethod]
    public async Task Login_BehindATrustedProxy_IsLimitedPerForwardedClient()
    {
        await using var factory = new SiteApiFactory(settings: TrustedProxies("10.0.0.1"));
        using var client = factory.CreateClient();

        for (var i = 0; i < LoginThrottle.AttemptsPerClient; i++)
        {
            var guess = await LoginFromAsync(client, "10.0.0.1", $"guess-{i}", ("X-Forwarded-For", "192.0.2.1"));
            Assert.AreEqual(HttpStatusCode.Unauthorized, guess.StatusCode);
        }
        var limited = await LoginFromAsync(client, "10.0.0.1", SiteApiFactory.Password, ("X-Forwarded-For", "192.0.2.1"));
        var other = await LoginFromAsync(client, "10.0.0.1", SiteApiFactory.Password, ("X-Forwarded-For", "192.0.2.2"));

        Assert.AreEqual(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, other.StatusCode);
    }

    [TestMethod]
    public async Task Login_ThroughAnUntrustedProxy_IgnoresTheClientsItForwards()
    {
        await using var factory = new SiteApiFactory(settings: TrustedProxies("10.0.0.1"));
        using var client = factory.CreateClient();

        for (var i = 0; i < LoginThrottle.AttemptsPerClient; i++)
        {
            var guess = await LoginFromAsync(client, "10.0.0.2", $"guess-{i}", ("X-Forwarded-For", $"192.0.2.{i + 1}"));
            Assert.AreEqual(HttpStatusCode.Unauthorized, guess.StatusCode);
        }
        var limited = await LoginFromAsync(client, "10.0.0.2", SiteApiFactory.Password, ("X-Forwarded-For", "198.51.100.1"));

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
    [DataRow("36501")]
    public async Task InvalidSessionDays_FailsStartup(string sessionDays)
    {
        await using var factory = new SiteApiFactory(settings: new Dictionary<string, string?>
        {
            [SecurityExtensions.SessionDaysKey] = sessionDays,
        });

        var exception = Assert.Throws<Exception>(() => factory.CreateClient());

        Assert.Contains(SecurityExtensions.SessionDaysKey, exception.GetBaseException().Message);
    }

    [TestMethod]
    [DataRow("proxy.lan")]
    [DataRow("10.0.0.1;proxy.lan")]
    [DataRow("10.0.0.0/33")]
    public async Task InvalidTrustedProxies_FailsStartup(string trustedProxies)
    {
        await using var factory = new SiteApiFactory(settings: TrustedProxies(trustedProxies));

        var exception = Assert.Throws<Exception>(() => factory.CreateClient());

        Assert.Contains(SecurityExtensions.TrustedProxiesKey, exception.GetBaseException().Message);
    }

    // ---- The hub's origin check ----

    [TestMethod]
    [DataRow("http://localhost:8080", HttpStatusCode.OK)]
    [DataRow("https://sitechecker.lan", HttpStatusCode.OK)]
    [DataRow("http://[fd7a:115c::1]:8080", HttpStatusCode.OK)]
    [DataRow("https://other-machine.tailnet.ts.net", HttpStatusCode.Forbidden)]
    [DataRow("null", HttpStatusCode.Forbidden)]
    public async Task Hub_FromAPageOnAnotherHost_IsForbidden(string origin, HttpStatusCode expected)
    {
        await using var factory = new SiteApiFactory(settings: new Dictionary<string, string?>
        {
            // An IPv6 address is listed in brackets, as HostFilteringMiddleware matches it.
            [SecurityExtensions.AllowedHostsKey] = "sitechecker.lan;[fd7a:115c::1]",
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
