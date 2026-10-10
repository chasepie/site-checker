namespace DemoScrapers;

/// <summary>
/// Reports the location PIA's "What is my IP" page shows, which is the VPN Location the check ran
/// through. A blocked request asks for another VPN Location and a retry.
/// </summary>
public sealed class PiaLocation : IScript
{
    public async Task<ScriptOutcome> RunAsync(ScriptContext ctx)
    {
        if (ctx.Navigation.Response?.Status is 403 or 429)
        {
            return ScriptOutcome.KnownFailure("Blocked", RequestedAction.ChangeVpnLocation, RequestedAction.Retry);
        }
        ctx.Navigation.EnsureSucceeded(); // any other navigation failure is an Unexpected Failure

        var location = ctx.Page.Locator(
            ".exposed-card-container-info .card-info-row:nth-of-type(3) .exposed-info span:nth-of-type(2)");
        try
        {
            await location.WaitForAsync(new() { Timeout = 10_000 });
        }
        catch (TimeoutException) // Playwright reports timeouts as System.TimeoutException
        {
            return ScriptOutcome.KnownFailure("Location not shown", RequestedAction.Retry);
        }

        var text = await location.TextContentAsync();
        return text?.Trim() ?? "[no content]"; // implicit conversion from string: Succeeded with this content
    }
}
