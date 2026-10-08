namespace DemoScrapers;

/// <summary>
/// Reports whether BrowserScan's bot detection test sees the browser as a person or a robot. The
/// Site turns on Always Take Screenshot to keep the full results.
/// </summary>
public sealed class BotDetection : IScript
{
    private static readonly Regex TestResults = new(@"^Test Results:\s*(Normal|Robot)");

    public async Task<ScriptOutcome> RunAsync(ScriptContext ctx)
    {
        ctx.Navigation.EnsureSucceeded();

        var results = ctx.Page.Locator("div", new() { HasTextRegex = TestResults });
        await results.WaitForAsync();

        var text = await results.TextContentAsync();
        return text ?? throw new InvalidOperationException("Could not find test results on the page.");
    }
}
