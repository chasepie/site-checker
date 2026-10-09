namespace SiteChecker.ScrapeWorker.IntegrationTests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using NSubstitute;
using SiteChecker.Scraper;
using SiteChecker.Scraper.Browsers;
using BrowserType = SiteChecker.Scraper.BrowserType;

/// <summary>
/// The real worker, compiling scripts for real, with a substituted browser and a monitor whose
/// abandoned runs a test sets, so nothing stops the test host.
/// </summary>
internal sealed class ScrapeWorkerFactory : WebApplicationFactory<SiteChecker.ScrapeWorker.Program>
{
    public static readonly byte[] ScreenshotBytes = [1, 2, 3];

    public IPage Page { get; } = Substitute.For<IPage>();

    public FakeAbandonedRunMonitor AbandonedRuns { get; } = new();

    /// <summary>
    /// Every browser the worker opened, by type.
    /// </summary>
    public List<BrowserType> OpenedBrowsers { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Page.GotoAsync(Arg.Any<string>(), Arg.Any<PageGotoOptions?>()).Returns((IResponse?)null);
        Page.ScreenshotAsync(Arg.Any<PageScreenshotOptions?>()).Returns(ScreenshotBytes);
        Page.ContentAsync().Returns("<html>page</html>");

        var session = Substitute.For<IBrowserSession>();
        session.Page.Returns(Page);
        var browsers = Substitute.For<IBrowserProvider>();
        browsers.OpenAsync(Arg.Any<BrowserType>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                lock (OpenedBrowsers)
                {
                    OpenedBrowsers.Add(call.Arg<BrowserType>());
                }
                return session;
            });

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(browsers);
            services.AddSingleton<IAbandonedRunMonitor>(AbandonedRuns);
        });
    }
}

internal sealed class FakeAbandonedRunMonitor : IAbandonedRunMonitor
{
    private int _tracked;

    public bool HasAbandonedRuns { get; set; }

    public int Tracked => Volatile.Read(ref _tracked);

    public void Track(Task run, ScrapeRequest request) => Interlocked.Increment(ref _tracked);
}
