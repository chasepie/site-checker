namespace SiteChecker.Backend.UnitTests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SiteChecker.Backend.Services.Scraping;
using SiteChecker.Scraper;
using SiteChecker.Scraper.Browsers;
using SiteChecker.Scraper.Executors;

/// <summary>
/// The app's side of the Scrape Worker, against a fake worker.
/// </summary>
[TestClass]
public sealed class RemoteScraperServiceTests
{
    private static readonly RemoteScraperOptions FastOptions = new(
        ReadinessTimeout: TimeSpan.FromMilliseconds(300),
        ReadinessPollInterval: TimeSpan.FromMilliseconds(20),
        ResponseAllowance: TimeSpan.FromMilliseconds(100));

    private static readonly string[] HealthThenScrape = ["GET /healthz", "POST /scrape"];
    private static readonly string[] TwoEvictions = ["DELETE /scripts/3", "DELETE /scripts/4"];

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    /// <summary>
    /// Answers each request with <see cref="OnRequest"/>, and records them.
    /// </summary>
    private sealed class FakeWorker : HttpMessageHandler, IHttpClientFactory
    {
        private readonly Lock _lock = new();
        private readonly List<string> _requests = [];

        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> OnRequest { get; set; } =
            (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/healthz"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : Json(ScrapeResult.Succeeded("content")));

        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (_lock)
                {
                    return [.. _requests];
                }
            }
        }

        public HttpClient CreateClient(string name)
        {
            Assert.AreEqual(RemoteScraperService.HttpClientName, name);
            return new HttpClient(this, disposeHandler: false) { BaseAddress = new Uri("http://scrape-worker:8080/") };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            }
            return OnRequest(request, cancellationToken);
        }
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<(string Category, LogLevel Level, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, this);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(string category, RecordingLoggerProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (provider.Entries)
                {
                    provider.Entries.Add((category, logLevel, formatter(state, exception)));
                }
            }
        }
    }

    private static HttpResponseMessage Json(ScrapeResult result)
        => new(HttpStatusCode.OK) { Content = JsonContent.Create(result, options: ScrapeJson.Options) };

    private static ScrapeRequest Request(TimeSpan? timeout = null) => new()
    {
        SiteCheckId = 7,
        Site = new ScrapeSite(3, "Site", new Uri("https://example.com/"), UseVpn: false),
        Scraper = new ScriptSpec("Script.cs", "source", "hash"),
        Timeout = timeout ?? TimeSpan.FromSeconds(1),
    };

    private static RemoteScraperService CreateService(FakeWorker worker, ILoggerFactory? loggerFactory = null)
    {
        var config = new ConfigurationBuilder().Build();
        return new RemoteScraperService(
            worker,
            new BrowserSelector(config),
            new ScrapeTimeouts(config, NullLogger<ScrapeTimeouts>.Instance),
            FastOptions,
            TimeProvider.System,
            loggerFactory ?? NullLoggerFactory.Instance);
    }

    [TestMethod]
    public async Task Scrape_ReturnsTheWorkersResult_AfterItsHealthCheck()
    {
        var worker = new FakeWorker
        {
            OnRequest = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/healthz"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : Json(ScrapeResult.KnownFailure("Blocked", [Scripting.RequestedAction.Retry]) with
                {
                    Duration = TimeSpan.FromSeconds(4),
                    Screenshot = [1, 2, 3],
                    Logs = [new ScraperLogEntry(LogLevel.Information, "page loaded", null)],
                })),
        };

        var result = await CreateService(worker).ScrapeAsync(Request(), Ct);

        Assert.AreEqual(ScrapeOutcome.KnownFailure, result.Outcome);
        Assert.AreEqual("Blocked", result.Message);
        Assert.AreEqual(Scripting.RequestedAction.Retry, Assert.ContainsSingle(result.RequestedActions));
        Assert.AreEqual(TimeSpan.FromSeconds(4), result.Duration);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, result.Screenshot);
        CollectionAssert.AreEqual(HealthThenScrape, worker.Requests.ToArray());
    }

    [TestMethod]
    public async Task Scrape_RelogsWhatTheScraperLogged_UnderTheScriptCategory()
    {
        var worker = new FakeWorker
        {
            OnRequest = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/healthz"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : Json(ScrapeResult.Succeeded("content") with
                {
                    Logs =
                    [
                        new ScraperLogEntry(LogLevel.Information, "page loaded", null),
                        new ScraperLogEntry(LogLevel.Warning, "odd page", "System.InvalidOperationException: odd"),
                    ],
                })),
        };
        using var logs = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));

        await CreateService(worker, loggerFactory).ScrapeAsync(Request(), Ct);

        var scriptLogs = logs.Entries.Where(e => e.Category == ScriptExecutor.LoggerCategory).ToList();
        Assert.HasCount(2, scriptLogs);
        Assert.AreEqual((ScriptExecutor.LoggerCategory, LogLevel.Information, "page loaded"), scriptLogs[0]);
        Assert.AreEqual(LogLevel.Warning, scriptLogs[1].Level);
        Assert.Contains("System.InvalidOperationException: odd", scriptLogs[1].Message);
    }

    [TestMethod]
    public async Task Scrape_WaitsForTheWorkerToBecomeHealthy()
    {
        var healthChecks = 0;
        var worker = new FakeWorker
        {
            OnRequest = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/healthz"
                ? new HttpResponseMessage(Interlocked.Increment(ref healthChecks) < 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
                : Json(ScrapeResult.Succeeded("content"))),
        };

        var result = await CreateService(worker).ScrapeAsync(Request(), Ct);

        Assert.AreEqual(ScrapeOutcome.Succeeded, result.Outcome);
        Assert.AreEqual(3, healthChecks);
    }

    [TestMethod]
    public async Task Scrape_IsAnUnexpectedFailure_WhenTheWorkerNeverBecomesHealthy()
    {
        var worker = new FakeWorker
        {
            OnRequest = (_, _) => throw new HttpRequestException("Connection refused (scrape-worker:8080)"),
        };

        var result = await CreateService(worker).ScrapeAsync(Request(), Ct);

        Assert.AreEqual(ScrapeOutcome.UnexpectedFailure, result.Outcome);
        Assert.StartsWith("The Scrape Worker isn't available", result.Message);
        Assert.DoesNotContain("POST /scrape", worker.Requests);
        Assert.IsGreaterThan(TimeSpan.Zero, result.Duration);
    }

    [TestMethod]
    public async Task Scrape_IsAnUnexpectedFailure_WhenTheConnectionDrops()
    {
        var worker = new FakeWorker
        {
            OnRequest = (request, _) => request.RequestUri!.AbsolutePath == "/healthz"
                ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))
                : throw new HttpRequestException("The response ended prematurely."),
        };

        var result = await CreateService(worker).ScrapeAsync(Request(), Ct);

        Assert.AreEqual(ScrapeOutcome.UnexpectedFailure, result.Outcome);
        Assert.AreEqual("The Scrape Worker stopped responding: The response ended prematurely.", result.Message);
        Assert.AreEqual(typeof(HttpRequestException).FullName, result.ExceptionType);
    }

    [TestMethod]
    public async Task Scrape_IsAnUnexpectedFailure_WhenTheWorkerRefusesIt()
    {
        var worker = new FakeWorker
        {
            OnRequest = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/healthz"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("restarting") }),
        };

        var result = await CreateService(worker).ScrapeAsync(Request(), Ct);

        Assert.AreEqual(ScrapeOutcome.UnexpectedFailure, result.Outcome);
        Assert.AreEqual("The Scrape Worker refused the scrape (503 ServiceUnavailable): restarting", result.Message);
    }

    [TestMethod]
    public async Task Scrape_IsAnUnexpectedFailure_WhenNoResultComesBackInTime()
    {
        var worker = new FakeWorker
        {
            OnRequest = async (request, ct) =>
            {
                if (request.RequestUri!.AbsolutePath == "/healthz")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }
                await Task.Delay(Timeout.Infinite, ct);
                return Json(ScrapeResult.Succeeded("too late"));
            },
        };

        var result = await CreateService(worker).ScrapeAsync(Request(timeout: TimeSpan.FromMilliseconds(200)), Ct);

        Assert.AreEqual(ScrapeOutcome.UnexpectedFailure, result.Outcome);
        Assert.AreEqual("The Scrape Worker stopped responding: no result within 0 s.", result.Message);
    }

    [TestMethod]
    public async Task Scrape_Throws_WhenCancelled()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var worker = new FakeWorker
        {
            OnRequest = async (request, ct) =>
            {
                if (request.RequestUri!.AbsolutePath == "/healthz")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }
                await cancellation.CancelAsync();
                await Task.Delay(Timeout.Infinite, ct);
                return Json(ScrapeResult.Succeeded("unreachable"));
            },
        };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => CreateService(worker).ScrapeAsync(Request(), cancellation.Token));
    }

    [TestMethod]
    public async Task EvictScript_DeletesTheSitesScript_AndNeverThrows()
    {
        var worker = new FakeWorker
        {
            OnRequest = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)),
        };
        var service = CreateService(worker);

        await service.EvictScriptAsync(3, Ct);
        worker.OnRequest = (_, _) => throw new HttpRequestException("Connection refused");
        await service.EvictScriptAsync(4, Ct);

        CollectionAssert.AreEqual(TwoEvictions, worker.Requests.ToArray());
    }
}
