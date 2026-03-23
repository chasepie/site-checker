namespace SiteChecker.Scraper.Test;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using NSubstitute;
using SiteChecker.Scraper;
using SiteChecker.Scraper.Exceptions;
using SiteChecker.Scraper.Scrapers;

[TestClass]
public sealed class ScraperBaseTests
{
    private sealed class TestScraper(ILogger logger) : ScraperBase(logger)
    {
        public override string Id => "TEST";
        public override string Url => "https://test.example.com";

        public Func<IPage, ScrapeRequest, Task<IScrapeResult>>? DoScrapeImpl { get; set; }

        protected override Task<IScrapeResult> DoScrapeAsync(IPage page, ScrapeRequest request)
            => DoScrapeImpl?.Invoke(page, request)
                ?? Task.FromResult<IScrapeResult>(new SuccessScrapeResult { Content = "default" });

        protected override Task SaveExceptionsToLogsAsync(
            string filePathBase, ScrapeRequest request, Exception ex,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private static (TestScraper scraper, IPage page, ScrapeRequest request) CreateFixture()
    {
        var page = Substitute.For<IPage>();
        page.EvaluateAsync<int>(Arg.Any<string>()).Returns(Task.FromResult(1920));
        var request = new ScrapeRequest { Id = 1, ScraperId = "TEST" };
        return (new TestScraper(NullLogger.Instance), page, request);
    }

    [TestMethod]
    public async Task ScrapeAsync_ReturnsSuccessResult_WhenDoScrapeAsyncSucceeds()
    {
        var (scraper, page, request) = CreateFixture();
        scraper.DoScrapeImpl = (_, _) =>
            Task.FromResult<IScrapeResult>(new SuccessScrapeResult { Content = "ok" });

        var result = await scraper.ScrapeAsync(page, request);

        Assert.IsTrue(result.IsSuccess(out var success));
        Assert.AreEqual("ok", success.Content);
    }

    [TestMethod]
    public async Task ScrapeAsync_ReturnsFailureResult_WhenKnownScraperExceptionThrown()
    {
        var (scraper, page, request) = CreateFixture();
        scraper.DoScrapeImpl = (_, _) => throw new AccessDeniedScraperException();

        var result = await scraper.ScrapeAsync(page, request);

        Assert.IsTrue(result.IsFailure(out var failure));
        Assert.Contains(failure.ErrorMessage, "Access Denied");
    }

    [TestMethod]
    public async Task ScrapeAsync_ReturnsFailureResult_WhenBlankPageExceptionThrown()
    {
        var (scraper, page, request) = CreateFixture();
        scraper.DoScrapeImpl = (_, _) => throw new BlankPageScraperException();

        var result = await scraper.ScrapeAsync(page, request);

        Assert.IsTrue(result.IsFailure(out _));
    }

    [TestMethod]
    public async Task ScrapeAsync_ReturnsFailureResult_WhenUnexpectedExceptionThrown()
    {
        var (scraper, page, request) = CreateFixture();
        scraper.DoScrapeImpl = (_, _) => throw new InvalidOperationException("boom");

        var result = await scraper.ScrapeAsync(page, request);

        Assert.IsTrue(result.IsFailure(out var failure));
        Assert.IsInstanceOfType<UnexpectedScraperException>(failure.Exception);
    }

    [TestMethod]
    public async Task ScrapeAsync_DoesNotSetScreenshot_WhenSuccessAndAlwaysTakeScreenshotFalse()
    {
        var (scraper, page, request) = CreateFixture();
        request.AlwaysTakeScreenshot = false;
        scraper.DoScrapeImpl = (_, _) =>
            Task.FromResult<IScrapeResult>(new SuccessScrapeResult { Content = "ok" });

        var result = await scraper.ScrapeAsync(page, request);

        Assert.IsNull(result.Screenshot);
    }

    [TestMethod]
    public async Task ScrapeAsync_SetsScreenshot_WhenAlwaysTakeScreenshotTrue()
    {
        var (scraper, page, request) = CreateFixture();
        request.AlwaysTakeScreenshot = true;
        var expectedBytes = new byte[] { 1, 2, 3 };
        page.ScreenshotAsync(Arg.Any<PageScreenshotOptions?>()).Returns(Task.FromResult(expectedBytes));
        scraper.DoScrapeImpl = (_, _) =>
            Task.FromResult<IScrapeResult>(new SuccessScrapeResult { Content = "ok" });

        var result = await scraper.ScrapeAsync(page, request);

        CollectionAssert.AreEqual(expectedBytes, result.Screenshot);
    }

    [TestMethod]
    public async Task ScrapeAsync_SetsScreenshot_WhenResultIsFailure_EvenWithAlwaysTakeScreenshotFalse()
    {
        var (scraper, page, request) = CreateFixture();
        request.AlwaysTakeScreenshot = false;
        var expectedBytes = new byte[] { 1, 2, 3 };
        page.ScreenshotAsync(Arg.Any<PageScreenshotOptions?>()).Returns(Task.FromResult(expectedBytes));
        scraper.DoScrapeImpl = (_, _) => throw new AccessDeniedScraperException();

        await scraper.ScrapeAsync(page, request);

        await page.Received().ScreenshotAsync(Arg.Any<PageScreenshotOptions?>());
    }

    [TestMethod]
    public async Task ScrapeAsync_DoesNotRetakeScreenshot_WhenScreenshotAlreadyPresent()
    {
        var (scraper, page, request) = CreateFixture();
        request.AlwaysTakeScreenshot = true;
        var existingBytes = new byte[] { 1, 2, 3 };
        scraper.DoScrapeImpl = (_, _) =>
            Task.FromResult<IScrapeResult>(new SuccessScrapeResult { Content = "ok", Screenshot = existingBytes });

        await scraper.ScrapeAsync(page, request);

        await page.DidNotReceive().ScreenshotAsync(Arg.Any<PageScreenshotOptions?>());
    }
}
