namespace SiteChecker.Scraper.IntegrationTests;

using Microsoft.Playwright;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SiteChecker.Scripting;
using static SiteChecker.Scraper.IntegrationTests.ScriptTesting;

/// <summary>
/// The demo scripts in <c>samples/DemoScrapers</c>, compiled with the runtime compiler (not the SDK
/// build) and run against substituted pages. The expected content was captured from the compiled
/// <c>PiaLocationScraper</c> and <c>BotDetectionScraper</c> these scripts replace.
/// </summary>
[TestClass]
public sealed class DemoScriptTests
{
    public TestContext TestContext { get; set; } = null!;

    private static string DemoScrapersDirectory => Path.Combine(AppContext.BaseDirectory, "DemoScrapers");

    public static IEnumerable<object[]> DemoScripts =>
        Directory.GetFiles(DemoScrapersDirectory, "*.cs").Select(path => new object[] { Path.GetFileName(path) });

    [TestMethod]
    [DynamicData(nameof(DemoScripts))]
    public async Task DemoScript_CompilesWithTheRuntimeCompiler(string fileName)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(DemoScrapersDirectory, fileName), TestContext.CancellationToken);

        Assert.IsEmpty(Compiler.Validate(source, fileName));
    }

    // ---- PiaLocation.cs ----

    [TestMethod]
    [DataRow("  Michigan  ", "Michigan")]
    [DataRow("Ohio", "Ohio")]
    [DataRow("", "")]
    [DataRow(null, "[no content]")]
    public async Task PiaLocation_ReturnsTheContentTheCompiledScraperReturned(string? text, string expected)
    {
        var outcome = await RunAsync("PiaLocation.cs", PageWithText(text), Navigated(200));

        Assert.IsFalse(outcome.IsKnownFailure);
        Assert.AreEqual(expected, outcome.Content);
    }

    [TestMethod]
    [DataRow(403)]
    [DataRow(429)]
    public async Task PiaLocation_WhenBlocked_IsAKnownFailure_AskingForAnotherVpnLocationAndARetry(int status)
    {
        var outcome = await RunAsync("PiaLocation.cs", PageWithText("Michigan"), Navigated(status));

        Assert.AreEqual("Blocked", outcome.KnownFailureMessage);
        CollectionAssert.AreEqual(
            new[] { RequestedAction.ChangeVpnLocation, RequestedAction.Retry },
            outcome.RequestedActions.ToArray());
    }

    [TestMethod]
    public async Task PiaLocation_WhenTheLocationIsNotShown_IsAKnownFailure_AskingForARetry()
    {
        var page = PageWithText(null, waitError: new TimeoutException("Timeout 10000ms exceeded."));

        var outcome = await RunAsync("PiaLocation.cs", page, Navigated(200));

        Assert.AreEqual("Location not shown", outcome.KnownFailureMessage);
        CollectionAssert.AreEqual(new[] { RequestedAction.Retry }, outcome.RequestedActions.ToArray());
    }

    [TestMethod]
    public async Task PiaLocation_WhenNavigationFailedOtherwise_Throws()
    {
        var navigation = NavigationResult.FromError(new InvalidOperationException("net::ERR_CONNECTION_RESET"));

        await Assert.ThrowsExactlyAsync<NavigationFailedException>(
            () => RunAsync("PiaLocation.cs", PageWithText("Michigan"), navigation));
    }

    // ---- BotDetection.cs ----

    [TestMethod]
    [DataRow("Test Results: Normal")]
    [DataRow("Test Results:  Robot")]
    public async Task BotDetection_ReturnsTheTestResults(string text)
    {
        var outcome = await RunAsync("BotDetection.cs", PageWithText(text), Navigated(200));

        Assert.AreEqual(text, outcome.Content);
    }

    [TestMethod]
    public async Task BotDetection_WhenThereAreNoResults_Throws()
    {
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => RunAsync("BotDetection.cs", PageWithText(null), Navigated(200)));

        Assert.AreEqual("Could not find test results on the page.", ex.Message);
    }

    private async Task<ScriptOutcome> RunAsync(string fileName, IPage page, NavigationResult navigation)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(DemoScrapersDirectory, fileName), TestContext.CancellationToken);
        using var script = CompileOrFail(source, fileName);
        return await script.CreateInstance().RunAsync(Context(page, navigation));
    }

    private static IPage PageWithText(string? text, Exception? waitError = null)
    {
        var locator = Substitute.For<ILocator>();
        locator.TextContentAsync(Arg.Any<LocatorTextContentOptions?>()).Returns(text);
        if (waitError != null)
        {
            locator.WaitForAsync(Arg.Any<LocatorWaitForOptions?>()).ThrowsAsync(waitError);
        }

        var page = Substitute.For<IPage>();
        page.Locator(Arg.Any<string>(), Arg.Any<PageLocatorOptions?>()).Returns(locator);
        return page;
    }

    private static NavigationResult Navigated(int status)
    {
        var response = Substitute.For<IResponse>();
        response.Status.Returns(status);
        response.Ok.Returns(status is >= 200 and < 300);
        response.Url.Returns("https://example.com/");
        response.StatusText.Returns(string.Empty);
        return NavigationResult.FromResponse(response);
    }
}
