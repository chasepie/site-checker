# Demo Scrapers

The Script Scrapers for the two demo Sites, written the way you'd write your own. `DemoDataSeeder`
uploads these files as the demo Sites' scripts, and `Scraper.IntegrationTests` compiles each one with
the app's runtime compiler.

- [`PiaLocation.cs`](PiaLocation.cs) reports the VPN Location PIA's "What is my IP" page shows. A 403
  or 429 is a Known Failure that asks for another VPN Location and a retry.
- [`BotDetection.cs`](BotDetection.cs) reports BrowserScan's bot detection result.

## Writing a script

A script is one C# file containing exactly one non-abstract class that implements `IScript` and has
a public parameterless constructor. A new instance runs for every Site Check.

```csharp
public sealed class Example : IScript
{
    public async Task<ScriptOutcome> RunAsync(ScriptContext ctx)
    {
        if (ctx.Navigation.Response?.Status is 403 or 429)
        {
            return ScriptOutcome.KnownFailure("Blocked", RequestedAction.ChangeVpnLocation, RequestedAction.Retry);
        }
        ctx.Navigation.EnsureSucceeded();

        var price = await ctx.Page.Locator(".price").TextContentAsync();
        return price?.Trim() ?? "[no price]";
    }
}
```

- **The page is already loaded.** The pipeline navigates to the Site's URL before your script runs,
  and `ctx.Navigation` holds the response, or the error if there was none. Navigation errors don't
  fail the check by themselves; call `ctx.Navigation.EnsureSucceeded()` if you don't handle them.
  You can still navigate elsewhere from `ctx.Page`.
- **Return the content** as a `string`. It's compared exactly with the previous content to decide
  whether the Site was Updated, so keep it stable while the page is unchanged. A `null` string is an
  Unexpected Failure, not empty content.
- **Return `ScriptOutcome.KnownFailure(message, ...)`** for a state you recognise, such as access
  denied or a blank page. It can ask the runner to change the VPN Location (VPN Sites only) and to
  retry (once per Failing Run).
- **Throw** for anything else: it's recorded as an Unexpected Failure.
- **Timeouts.** The run ends at the Site's timeout whether or not your script notices.
  `ctx.CancellationToken` is cancelled at that point, so loops can stop cleanly. Playwright reports
  its own timeouts as `System.TimeoutException`.
- `ctx.Logger` logs with the Site and Site Check attached. `ctx.Site` has the Site's name and URL, and
  whether the page goes through the VPN.

### What the app compiles

The app is the source of truth for what a valid script is. It compiles each upload on its own, with:

- C# 14, nullable enabled, and warnings ignored.
- These global usings, and no others: `System`, `System.Collections.Generic`, `System.Linq`,
  `System.Text.RegularExpressions`, `System.Threading`, `System.Threading.Tasks`,
  `Microsoft.Extensions.Logging`, `Microsoft.Playwright` and `SiteChecker.Scripting`.
- References to the .NET runtime, Playwright, `Microsoft.Extensions.Logging.Abstractions` and
  `SiteChecker.Scripting` only.
- **One file, no source generators.** A script can't use other files or attributes like
  `[GeneratedRegex]`; write `new Regex(...)` instead.

A script that doesn't compile is rejected when you save or test-run it, with its errors.

## An authoring project in your own repo

Scripts are written in an ordinary project for IntelliSense and compile errors; only the `.cs` file is
uploaded. Set it up to match the app's compiler:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <!-- Match the app: no implicit usings, the same fixed global usings. -->
    <ImplicitUsings>disable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <!-- Use the version that matches the app's release. -->
    <PackageReference Include="SiteChecker.Scripting" Version="0.1.0" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="System" />
    <Using Include="System.Collections.Generic" />
    <Using Include="System.Linq" />
    <Using Include="System.Text.RegularExpressions" />
    <Using Include="System.Threading" />
    <Using Include="System.Threading.Tasks" />
    <Using Include="Microsoft.Extensions.Logging" />
    <Using Include="Microsoft.Playwright" />
    <Using Include="SiteChecker.Scripting" />
  </ItemGroup>

</Project>
```

`SiteChecker.Scripting` is published to GitHub Packages, which needs authentication to restore even
public packages. Create a personal access token (classic) with the `read:packages` scope, set it as
`GITHUB_PACKAGES_TOKEN`, and add a `nuget.config` next to the project:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="github" value="https://nuget.pkg.github.com/chasepie/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <github>
      <add key="Username" value="YOUR_GITHUB_USERNAME" />
      <add key="ClearTextPassword" value="%GITHUB_PACKAGES_TOKEN%" />
    </github>
  </packageSourceCredentials>
</configuration>
```

In this repo, `DemoScrapers.csproj` references the contract with a `ProjectReference` instead.

## Uploading

In the app, open **New Site** or **Edit Site**, choose the `.cs` file, and use **Run test** to try it
against the Site's URL before saving. A Test Run records nothing, sends no notifications and carries
out no Requested Actions.
