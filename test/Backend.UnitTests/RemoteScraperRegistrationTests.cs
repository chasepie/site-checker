namespace SiteChecker.Backend.UnitTests;

using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SiteChecker.Backend.Services.Scraping;
using SiteChecker.Scraper;

/// <summary>
/// The Scrape Worker client that <c>TryAddRemoteScraperService</c> registers.
/// </summary>
[TestClass]
public sealed class RemoteScraperRegistrationTests
{
    public TestContext TestContext { get; set; } = null!;

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static (HttpClient Client, CapturingHandler Handler) CreateClient(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var handler = new CapturingHandler();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration);
        Assert.IsTrue(services.TryAddRemoteScraperService(configuration));
        services.AddHttpClient(RemoteScraperService.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        var client = services.BuildServiceProvider()
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(RemoteScraperService.HttpClientName);
        return (client, handler);
    }

    [TestMethod]
    public async Task Client_SendsTheSecret_ToTheWorkersAddress()
    {
        var (client, handler) = CreateClient(new()
        {
            [RemoteScraperService.ScrapeWorkerUrlKey] = "http://scrape-worker:8080",
            [ScrapeWorkerSecret.Key] = " worker-secret ",
        });

        await client.GetAsync(ScrapeWorkerPaths.Health, TestContext.CancellationToken);

        Assert.AreEqual(new Uri("http://scrape-worker:8080/healthz"), handler.Request!.RequestUri);
        Assert.AreEqual(new AuthenticationHeaderValue("Bearer", "worker-secret"), handler.Request.Headers.Authorization);
    }

    [TestMethod]
    public async Task Client_WithoutASecret_SendsNoAuthorization()
    {
        var (client, handler) = CreateClient(new()
        {
            [RemoteScraperService.ScrapeWorkerUrlKey] = "http://localhost:5280",
        });

        await client.GetAsync(ScrapeWorkerPaths.Health, TestContext.CancellationToken);

        Assert.IsNull(handler.Request!.Headers.Authorization);
    }
}
