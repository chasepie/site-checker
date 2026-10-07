# 0001: Scraper architecture

|             |                                                                                                                       |
| ----------- | --------------------------------------------------------------------------------------------------------------------- |
| **Status**  | In Review                                                                                                             |
| **Author**  | Chase Pietrangelo                                                                                                     |
| **Created** | 2026-10-06                                                                                                            |
| **Related** | [ADR 0001](../adr/0001-database-is-the-site-check-queue.md), [ADR 0002](../adr/0002-runner-triggers-notifications.md), [0002 Steps Scrapers](0002-steps-scrapers.md), [0003 Prompt Scrapers](0003-prompt-scrapers.md) |

> Status lifecycle: `Draft` → `In Review` → `Accepted` | `Rejected` | `Superseded by NNNN`.
> Once accepted, record each lasting decision as a short ADR in `docs/adr/` that links back here.

## Summary

Sites stop being compiled `ScraperBase` subclasses and become data. Each Site's **Scraper** becomes data instead of a class: a Script Scraper (an uploaded C# file) first, a Steps Scraper (built in the UI) next, and a Prompt Scraper (an LLM) later. One shared pipeline runs every Scraper. It navigates to `Site.Url`, enforces a per-Site timeout, and ends each scrape as Succeeded, as a Known Failure the Scraper reports (optionally requesting a VPN Location change or a retry), or as an Unexpected Failure. Sites can be created, edited, test-run and deleted from the UI without a rebuild. The database starts fresh, and `DataSeeder` becomes an optional demo-data seeder that never deletes.

## Motivation

Today, adding a new Site requires a code change, rebuild and redeploy. The only option is to create a new `ScraperBase` subclass, which is a lot of work for a single URL.

## Goals

- Allow for multiple ways of defining a Site:
  - via code without a rebuild/redeploy (perhaps using a C# or TypeScript/JavaScript script that can be loaded at runtime)
  - via a simple set of operations that can be defined in the GUI (for example, "navigate to this URL, wait for this selector, extract this text, take a screenshot")
  - by using a prompt that can be passed to an LLM that will perform the scrape using something like the Playwright MCP server.
- Replace the compiled `ScraperBase` subclasses. `PiaLocationScraper` and `BotDetectionScraper` are ported to C# scripts.
- This doc designs stage 1 in full: the shared scrape pipeline and C# scripts. GUI steps (stage 2) and LLM prompts (stage 3) have their own draft docs, [0002](0002-steps-scrapers.md) and [0003](0003-prompt-scrapers.md). This doc only shows that the pipeline fits them.

## Non-goals

- **Parallel Site Checks.** Checks keep running one at a time (ADR 0001).
- **Untrusted script authors.** SiteChecker is single-user and self-hosted, so whoever writes a script also runs the app. Scripts don't need a hard sandbox.
- **Authentication.** The app stays without authentication, and stage 1 documents a trust boundary instead (see below). It's only reachable from the home LAN and over a private VPN (Tailscale).
- **Login and auth flows.** No built-in support for credentials or authenticated sessions.
- **Backward compatibility.** The author is the project's only user, so any breaking change in this design is acceptable: database schema and data, migrations, API, configuration, the `SiteChecker.Scripting` contract, domain terms, the frontend (pages, layout, navigation and components can be redesigned freely), and the test projects (they can be recreated and reorganized). Nothing needs a migration path, deprecation period or compatibility shim.

## Context

How a Site is defined and scraped today, as of `96c2209`. Only the parts that bear on the goals are listed.

### Defining a Site

Adding a Site takes three edits and a redeploy:

1. A `ScraperBase` subclass in `src/Scraper/Scrapers/` that overrides `Id`, `Url` and `DoScrapeAsync(IPage, ScrapeRequest)`.
2. `AddScraper<T>()` in `AddScraperServices()` ([ScraperService.cs](../../src/Scraper/ScraperService.cs)).
3. A seeded `Site` in [DataSeeder.cs](../../src/Backend/Services/DataSeeder.cs).

`Site.ScraperId` selects the Scraper at runtime. There are two today: `PiaLocationScraper` (VPN) and `BotDetectionScraper`.

### Who does what during a scrape

| Responsibility                                                                 | Where it lives today                                                                   |
| ------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------- |
| Choose browser (local, Browserless, Browserless VPN)                           | `ScraperService.GetBrowserType`, called by the runner                                  |
| Resolve and rotate the VPN Location                                            | `SiteCheckRunner.GetVpnLocationAsync`                                                  |
| Connect to the browser, get a page, set a 1920×1080 viewport, close afterwards | `ScraperService.RunScraperAsync`                                                       |
| Navigate, wait and extract content                                             | Each Scraper's `DoScrapeAsync`                                                         |
| Detect access-denied or blank pages (Known Failures)                           | `ScraperBase.WaitForFirstLocatorAsync`, only if the Scraper calls it                   |
| Turn exceptions into a `FailureScrapeResult`                                   | `ScraperBase.ScrapeAsync` and again in `ScraperService.ScrapeContentAsync`             |
| Screenshot on failure, or always per Site                                      | `ScraperBase.ScrapeAsync`                                                              |
| HTML and exception dumps                                                       | `ScraperBase`, written to `site-checker/logs` (`/app/logs` in Docker) as `{SiteCheckId}_{ScraperId}`; not shown in the UI |
| Classify Known vs Unexpected                                                   | `SiteCheckExtensions.Update`: Known only if the exception is a `KnownScraperException` |

### Constraints the new design has to work with

- **`DataSeeder` deletes Sites it doesn't know.** Any Site whose `ScraperId` isn't in its list is deleted at startup, so a Site created at runtime wouldn't survive a restart.
- **`Site.Url` has no effect.** It's editable through the API, but `ScrapeRequest` doesn't carry it, and each Scraper navigates to its own hard-coded `Url`.
- **Known Failure detection is opt-in and slow.** Neither current Scraper calls `WaitForFirstLocatorAsync`, so neither can produce a Known Failure. Each detector waits a fixed 10 s before it starts looking.
- **Updated notifications compare content exactly.** A Done check notifies Updated when its content differs from the previous Done check (ADR 0002, `GLOSSARY.md`). Whatever produces content must be stable from run to run when the page hasn't changed.
- **Checks run one at a time** (ADR 0001), so a slow or hung scrape delays every other Site. The scrape path takes no `CancellationToken`; it's bounded only by Playwright timeouts and the Browserless `TIMEOUT` (180 s in `docker-compose.yml`).
- **The browser is remote.** In Docker, pages run in Browserless containers connected over CDP. The backend only holds a Playwright client connection.

## Proposed design

### Overview

Each Site stores its **Scraper** in a JSON column: which kind it is (Script, Steps, Prompt) and that kind's payload. A script's source lives in its own table. One shared pipeline runs every Site Check. It owns the browser, navigation, timeout and screenshots, and hands the loaded page to an executor for the Scraper's kind. The executor returns content, or reports a Known Failure along with any Requested Actions. `ScraperBase`, `Site.ScraperId` and the per-Site classes go away.

```
SiteCheckRunner                                   ┐
  │  BrowserType + VPN Location (rotate if due)   │
  ▼                                               │
Scrape pipeline (IScraperService)                 │
  1. connect to browser, get page                 │
  2. navigate to Site.Url (errors captured)       │ scrape lock
  3. run the executor for Scraper.Kind            │
       └─ Script executor | Steps executor (0002) | Prompt executor (0003)
       ──► Succeeded (content)                    │
         | Known Failure (message, Requested Actions)
         | throws ──► Unexpected Failure          │
     (2–3 under the Site's timeout)               │
  4. screenshot (own budget), failure dumps       │
  ▼                                               │
Scrape Result                                     │
  ▼                                               ┘
SiteCheckRunner
  saves Succeeded / Failed (+ a Queued retry if requested), notifies (unchanged)
  marks a VPN rotation due if requested
```

### Shared scrape pipeline

- **Browser acquisition.** The runner picks the `BrowserType` from `Site.UseVpn` and resolves the VPN Location, as today. Browser selection (`GetBrowserType`) and the connection code keep today's logic, moved behind an `IBrowserProvider` interface so the pipeline can be unit-tested with a substituted `IPage`.
- **Navigation.** The pipeline navigates to `Site.Url` before running the Scraper, so `Site.Url` decides what gets scraped and every kind shares the same page-load handling. Scrapers don't navigate to the Site's page themselves, though a Scraper may still navigate elsewhere partway through.
  - Navigation errors don't fail the scrape. The pipeline catches them and always runs the Scraper, passing the navigation result: the `IResponse` (status code and headers), or the exception if there was no response.
  - That lets a Scraper treat the usual signs of a blocked VPN Location (a connection reset, a 403 or 429, a load timeout on a bot challenge) as a Known Failure and request Change VPN Location. A Scraper that doesn't care calls one method that turns a failed navigation into an Unexpected Failure (see the contract).
- **Timeout.** Each Site has an optional timeout, falling back to a global default of 120 s set by `SCRAPE_TIMEOUT` (seconds, documented in `docs/configuration.md`). Navigation and the executor run under it.
  - **Upper bound.** Browserless ends a session at its `TIMEOUT`, so a longer Site timeout can't work. The app reads the same value from `BROWSERLESS_TIMEOUT` (milliseconds, default 180000), which `docker-compose.yml` passes to both the app and the Browserless containers' `TIMEOUT`, so the two can't drift. `SCRAPE_TIMEOUT` and every Site timeout must leave room under it for the screenshot; anything longer is rejected on save and at startup.
  - **Enforcement.** The pipeline enforces the timeout itself, because a token alone can't stop a scrape: Playwright .NET calls (`GotoAsync`, `TextContentAsync`, `ClickAsync`, ...) take no `CancellationToken`, and a script only stops if it checks one.
  - The executor starts with `Task.Run`, so even a script that blocks synchronously before its first `await` doesn't block the runner.
  - The pipeline waits for it with `WaitAsync(timeout)`. The executor also gets a token that's cancelled at the timeout, for scripts that want to stop cooperatively.
  - On timeout, the pipeline takes the failure screenshot, then closes the browser context and connection, which makes any pending Playwright call throw. It records an Unexpected Failure with a message saying how long it ran, and the runner moves on to the next check.
  - **Limit:** .NET can't kill a thread. A script stuck in a synchronous loop (`while (true)`, `Thread.Sleep`, `.Result` on a task that never completes) keeps a thread busy, and keeps its load context from unloading, until the app restarts. Other checks still run. Running scripts in a separate, killable process was considered and rejected as too much machinery for stage 1.
- **Executors.** One per kind. Each takes the loaded `IPage`, the navigation result, the Scraper and the cancellation token, and returns either content or a Known Failure. Anything an executor throws is an Unexpected Failure; the pipeline converts exceptions in one place (today this happens in both `ScraperBase` and `ScraperService`).
- **Screenshots** keep today's rule: on failure, or always when `Site.AlwaysTakeScreenshot` is set. The screenshot has its own budget (10 s) outside the Site's timeout, so a timed-out scrape, the failure most worth seeing, still gets one.
- **Failure artifacts.** The pipeline takes over the HTML and exception dumps that `ScraperBase` writes today. On an Unexpected Failure it writes them to the same logs directory, named `{SiteCheckId}_{SiteId}` (Scrapers no longer have an id). Test Runs don't write dumps.
- **Scrape lock.** `SiteCheckRunner` holds one async lock around everything that uses the shared browser and VPN containers: resolving and rotating the VPN Location, scraping, Test Runs, and the VPN controller's manual location change (which restarts the containers too). A Test Run therefore never overlaps a real check or a container restart.
- **Test Runs.** While a Site's Scraper is being edited, the UI can run the unsaved version through the pipeline and show the outcome (content, Known Failure with its Requested Actions, or error), the screenshot and the duration. A Test Run creates no Site Check, sends no notification, carries out no Requested Actions, and uses the current VPN Location without rotating.
  - **Inputs.** The request carries the unsaved Scraper and the unsaved Site settings it depends on (URL, `UseVpn`, timeout, `AlwaysTakeScreenshot`). It tests exactly what's on screen, and works on the Create Site page before the Site exists.
  - **Compilation.** A Test Run's script is compiled into its own collectible load context and unloaded when the run ends. It never touches the per-Site cache.
  - **Delivery.** The client generates the test-run id and sends it with the request, along with its SignalR connection id, and the endpoint returns `202 Accepted`. The result is sent to that connection only (`Clients.Client(...)`), not through the save interceptor's entity broadcast, which deliberately skips the client that made the request. Because the client knows the id before it sends the request, a fast result can't arrive before the client expects it.
  - Test Runs aren't persisted, so one lost to a restart costs nothing, which is the same reasoning ADR 0001 uses for the wake-up channel.

### Kinds of Scraper

#### Script Scrapers (stage 1)

A script is an ordinary C# file containing one class that implements `IScript`. It's written and kept outside the app, in the author's own repo with full IDE support, and uploaded to the Site. There's no in-browser editor in this release.

For example, a variation on the PIA location Scraper that reports Known Failures instead of placeholder content:

```csharp
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
        catch (Microsoft.Playwright.TimeoutException) // qualified: System also has a TimeoutException
        {
            return ScriptOutcome.KnownFailure("Location not shown", RequestedAction.Retry);
        }

        var text = await location.TextContentAsync();
        return text?.Trim() ?? "[no content]"; // implicit conversion from string: Succeeded with this content
    }
}
```

**Contract.** The types a script compiles against live in a small contract assembly, `SiteChecker.Scripting`:

| Type              | Purpose                                                                                                                                                                                                               |
| ----------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `IScript`         | `Task<ScriptOutcome> RunAsync(ScriptContext ctx)`                                                                                                                                                                     |
| `ScriptContext`   | `Page` (the `IPage` the pipeline loaded from `Site.Url`), `Navigation` (the navigation `Response`, or its `Error`, and `EnsureSucceeded()`, which throws when navigation failed or the status isn't a success), `CancellationToken` (the Site's timeout), `Site` (read-only: name, URL, whether it uses the VPN), `Logger` (an `ILogger` scoped to the Site and Site Check) |
| `ScriptOutcome`   | Succeeded, via an implicit conversion from `string` (a `null` string throws, so it's an Unexpected Failure rather than empty content), or `ScriptOutcome.KnownFailure(message, params RequestedAction[])` |
| `RequestedAction` | `ChangeVpnLocation`, `Retry`                                                                                                                                                                                          |

Anything `RunAsync` throws is an Unexpected Failure. Scripts also get the existing Playwright extensions (`ScrollToBottomAsync`, `TakeFullPageScreenshotAsync`, the interval-based `ILocator.WaitForAsync`), which move into the contract assembly. Scripts aren't sandboxed (see Non-goals).

**Uploading.** In `edit-site`, the user picks a `.cs` file. The browser reads it as text and sends it in the normal Site create/update request, so the API needs no multipart endpoint. The source is stored in its own table (see Data model), and the UI fetches it for a read-only view with a download button.

**Compilation.** The backend compiles the source with `CSharpCompilation` rather than Roslyn scripting, against the app's own runtime assemblies, `Microsoft.Playwright` and `SiteChecker.Scripting`. It loads the result into a collectible `AssemblyLoadContext`.

The runtime compilation is the source of truth for what a valid script is, because an SDK project build differs from it: implicit usings, project settings, source generators, and other files in the same project.
- **Fixed global usings.** Every script is compiled with the same documented set: `System`, `System.Collections.Generic`, `System.Linq`, `System.Text.RegularExpressions`, `System.Threading`, `System.Threading.Tasks`, `Microsoft.Extensions.Logging`, `Microsoft.Playwright` and `SiteChecker.Scripting`, with nullable enabled and the app's C# version.
- **One file, no generators.** A script can't depend on other files or on source generators. Regex uses `new Regex(...)` rather than `[GeneratedRegex]`.
- **Authoring projects match it.** They turn `ImplicitUsings` off and declare the same set as `<Using>` items, so the IDE and the app agree.
- **On save and Test Run:** a source that doesn't compile, or that doesn't contain exactly one non-abstract `IScript` class with a parameterless constructor, is rejected with the Roslyn diagnostics (file, line, message), which the UI lists.
- **References:** the metadata reference set (the runtime assemblies, Playwright and the contract) is built once at startup and reused by every compile, rather than rebuilt from a few hundred assemblies each time.
- **Caching:** the compiled script is cached per Site and keyed by source hash. Replacing a Site's script or deleting the Site unloads the old load context, so edits don't leak assemblies. Test Run compiles aren't cached (see Test Runs).
- **Unloading:** this only works if nothing outside the load context still references the script's types. Scripts are instantiated per run and the cache holds only the load context, but a script that registers static event handlers could keep its context alive.
- **Restarts:** after a restart, each script compiles again the first time its Site is checked.

**Authoring setup.** A script is written in an ordinary C# project that references the contract. The project gives the author IntelliSense and compile errors, and only the `.cs` file is uploaded.

- **In this repo: `samples/DemoScrapers`.** A demo project written the way a script author's project would be, with one `.cs` file per demo Site (`PiaLocation.cs`, `BotDetection.cs`). These files are the ported Scrapers, the examples and the source the demo-data seeder uploads (see Demo data).
  - It references `SiteChecker.Scripting` with a `ProjectReference` and is part of `SiteChecker.slnx`, so the existing `ci.yml` build compiles it on every PR against the contract in the same commit. It turns `ImplicitUsings` off and declares the runtime's global usings as `<Using>` items (see Compilation).
  - Building the project is for the IDE. The guarantee comes from `Scraper.IntegrationTests`, which compiles each demo file with the app's own runtime compiler, so CI fails on exactly the errors the app would hit.
  - Its `README.md` shows the `PackageReference`, `nuget.config` and `<Using>` setup an author uses outside this repo, and the one-file, no-generators rule. CI doesn't exercise that path; a broken package would only show up when an author restores it.
- **In an author's own repo.** This repo publishes `SiteChecker.Scripting` as a NuGet package to GitHub Packages, and authors add a `PackageReference` to it. GitHub Packages requires authentication to restore NuGet packages, even public ones, so each author needs a personal access token with `read:packages` in their `nuget.config`. Whether the author's own repo also builds the project in CI is up to them.
- **Publishing.** Nothing publishes the package yet: `ci.yml` only builds and tests. Stage 1 adds a release workflow that packs `SiteChecker.Scripting` and pushes it to GitHub Packages when a version tag is pushed. The package version follows the tag (see Risks and open questions).

#### Later kinds

Steps Scrapers ([0002](0002-steps-scrapers.md), stage 2) and Prompt Scrapers ([0003](0003-prompt-scrapers.md), stage 3) are designed in their own docs. Each plugs into this pipeline the same way Script Scrapers do. It adds an executor for its kind and a payload property on `ScraperDefinition`, gets the loaded page, the navigation result and the cancellation token, and ends as Succeeded, as a Known Failure with Requested Actions, or by throwing.

### Known Failures and Requested Actions

Sites behave unpredictably depending on the site itself and the VPN Location, so a Known Failure marks a state the Scraper recognises, such as access denied or a blank page, rather than an edge case nobody accounted for. Which states count varies from Site to Site, so the pipeline doesn't detect them. The Scraper reports them:

- **Succeeded**: content.
- **Known Failure**: a message, and optionally Requested Actions.
- **Unexpected Failure**: anything the executor throws, a timeout, or a pipeline error. It never carries Requested Actions.

How each kind reports a Known Failure is part of that kind's design (see Kinds of Scraper).

The notification policy doesn't change. A Known Failure doesn't report a Failing Run by itself, but the run is still reported once its Known Failures reach the Site's Known Failure Threshold.

**Requested Actions** let a Known Failure ask the runner to respond. They're recorded in `SiteCheck.RequestedActions`, in the same save as the outcome:

| Action                  | What the runner does |
| ----------------------- | -------------------- |
| **Change VPN Location** | Marks a rotation as due, so the next VPN-routed check rotates first instead of waiting for `VPN_CHANGE_INTERVAL`. That rotation excludes the current location (`PiaService.ChangeLocationAsync(excludeCurrent: true)`), so the round-robin doesn't come back to it soon. Exclusion is global: every VPN Site skips the location until every location has been excluded, when `PiaService` resets the list. Ignored when the Site doesn't use the VPN, since rotating would restart the shared container for no benefit. |
| **Retry**               | Creates a new Queued Site Check for the Site, in the same save as the outcome, instead of waiting for its Schedule. Honored once per Failing Run; later Known Failures in the same run wait for the Schedule, which stops a Site that keeps hitting the same Known Failure from retrying in a loop. |

If a Known Failure requests both Change VPN Location and Retry, the retry runs at the new VPN Location.

**Retry** in detail:
- **No lost retries.** The retry Site Check is created in the same save that records the outcome, so a crash in between can't lose it. A recorded Retry always means a retry was queued, which is what "once per Failing Run" checks. Like every Site Check, it's created by the runner.
- **Queue position.** It's Queued like any other request and joins the back of the queue. It doesn't cut ahead of Site Checks that are already Queued, and there's no delay beyond waiting its turn.
- **Threshold.** The retry is part of the same Failing Run. A Known Failure on the retry counts toward the Known Failure Threshold like any other. A retry that Succeeds ends the run and follows the normal notification policy. If the run was already reported (for example when the Known Failure Threshold is 1, or an earlier Unexpected Failure reported it), it notifies Recovered, or Recovered and Updated. If the run wasn't reported there's no Recovery, but content that differs from the previous Succeeded check still notifies Updated.
- **Schedule.** The retry runs even if the Site is now outside its Schedule window, or its Schedule is disabled, because it finishes a check that was allowed to run. It also counts as the Site's latest Site Check for the Due rule, so the next scheduled check comes one interval after the retry.
- **History.** A retry isn't marked on its own Site Check. The previous check's `RequestedActions` shows that it was requested.

### Data model and API changes

**Model**

- `Site.Scraper`: a complex type (`ScraperDefinition`) mapped with `ComplexProperty(...).ToJson()`, like `SiteSchedule`. EF Core 10 doesn't support inheritance for complex types, so it can't be a class hierarchy. It's a flat type: a `Kind` enum and one nullable property per kind's payload. Stage 1 has only `Script`; 0002 and 0003 add `Steps` and `Prompt`. On save, exactly one payload must be set and it must match `Kind`; anything else is rejected. The rejected alternative is a value converter that serializes a `System.Text.Json` polymorphic hierarchy to a plain string column, which gives up EF's ability to query and track changes inside the JSON.
- The `Script` payload holds only metadata: the file name, the source hash and when it was uploaded.
- `SiteScript`: a new entity holding a script's source, one row per Site, deleted with its Site. Keeping the source out of `Site` keeps it out of every Site list response and Site broadcast. The save interceptor sends both old and new values, so a source on `Site` would go to every client twice on every Site save.
- `Site.TimeoutSeconds`: nullable; `null` uses `SCRAPE_TIMEOUT`. It must leave room under `BROWSERLESS_TIMEOUT` (see Timeout).
- `SiteCheck.RequestedActions`: the actions a Known Failure requested, shown in the UI and used to tell whether a retry has already been queued in the current Failing Run.
- `Site.ScraperId` is removed.
- Site Checks don't record which version of the Scraper produced them, and changing a Site's Scraper isn't marked in history. If the new Scraper produces different content, the next Succeeded check notifies Updated, like any other change.

**API** (`SiteController`)

- `POST /api/site` and `PUT /api/site/{id}`: create a Site (today only `DataSeeder` can) and update one, now including `Scraper` and `TimeoutSeconds`. Both take a request body that carries the script source, which is stored in `SiteScript`.
- `GET /api/site/{id}/script`: the script source, for the read-only view and download.
- `DELETE /api/site/{id}`: hard delete. Site Checks, screenshots and the script go with it through the required-FK cascade, which `DataSeeder` already relies on.
  - The cascade bypasses the save interceptor for the Site's checks, so SignalR clients only hear that the Site was deleted. The client that sent the DELETE doesn't hear even that, because the interceptor's broadcast skips the requesting connection (`EntityChangesService.GetClients`). The frontend therefore drops the Site's cached checks, and anything derived from them, in two places: after its own successful DELETE (as `deleteAllSiteChecks` already does) and when another tab's delete arrives as the Site's Deleted event.
  - A check still running when its Site is deleted ends in `MarkFailedAsync`, which logs and returns when the check no longer exists. This needs a test.
- `POST /api/site/test-run`: start a Test Run of an unsaved Scraper and Site settings. The client supplies the test-run id and its SignalR connection id; the endpoint returns `202 Accepted` (see Test Runs).
- The existing Create Empty Check endpoint on `SiteCheckController` stays, renamed to record a Baseline Reset (`SiteCheckRunner.RecordBaselineResetAsync`, today's `RecordEmptyCheckAsync`). Its behavior doesn't change.

**Generated types.** New models and enums (`ScraperDefinition`, the script payload, the create/update and test-run requests, compile diagnostics, `RequestedAction` and the test-run result) must be added to the lists in `ReinforcedTypingsConfiguration`.

**Frontend**

The frontend can be redesigned as much as these changes need (see Non-goals); the items below are the minimum, not a constraint to fit the current layout.

- A Create Site page.
- A delete action on the Site details page. The Site Check store drops the deleted Site's checks both after its own successful delete and on a Deleted event from another client.
- In `edit-site`: a test-run panel, a timeout field, and a script file picker with a read-only source view and the compile errors.
- The Site details page's **Create Empty Check** action becomes **Reset Baseline**.

### Domain vocabulary

Proposed changes to `GLOSSARY.md`, applied once this doc is accepted:

- **Scraper** (kept, redefined): what a Site runs to get its content. Each Site has exactly one, of one kind: a **Script Scraper**, **Steps Scraper** or **Prompt Scraper**. It's no longer a class compiled into the app, but the term stays.
- **Succeeded** (renames **Done**): a Site Check whose scrape produced content. "Done" was ambiguous, because `DoneDate` and `IsComplete` also cover Failed checks; "completed" now means finished either way.
- **Baseline** (new): the content of a Site's latest Succeeded Site Check, which the next one is compared against to decide Updated.
- **Baseline Reset** (renames **Empty Check**): a Site Check recorded without a scrape that resets the Site's Baseline to empty, so the next Succeeded Site Check notifies Updated with its content. The reset itself never notifies, and it ends any Failing Run without a Recovery. Behavior is unchanged from today's Empty Check.
- **Known Failure** (revised): a Failed Site Check whose Scraper recognised the state it found (such as access denied or a blank page).
- **Unexpected Failure** (new): a Failed Site Check its Scraper didn't recognise, such as an error, a timeout or a broken page.
- **Requested Action** (new): something a Known Failure asks the Site Check Runner to do after recording it: change the VPN Location, or retry.
- **Test Run** (new): a scrape of a Site's Scraper, usually an unsaved one, run to try it out. It isn't a Site Check: it isn't recorded, never notifies, and carries out no Requested Actions.

Code renames that follow: `CheckStatus.Done` → `Succeeded` (the stored integer is pinned, so the column doesn't change), `SiteCheck.DoneDate` → `CompletedDate`, `RecordEmptyCheckAsync` → `RecordBaselineResetAsync`, and the frontend labels.

`CLAUDE.md` changes with stage 1:
- **Notifications:** the policy in the new terms (Succeeded, Baseline Reset).
- **Scrapers and Sites:** rewritten. Sites are data, Script Scrapers are uploaded, the pipeline navigates, and `DataSeeder` seeds demo data and never deletes.
- **Testing and Commands:** the new test project names.
- **Configuration:** `SCRAPE_TIMEOUT`, `BROWSERLESS_TIMEOUT`, `SEED_DEMO_DATA`, and the trust boundary.

## Alternatives considered

### Keep compiled Scrapers as one of the kinds

Keeping `ScraperBase` subclasses alongside the new kinds would avoid porting, but every Site would still go through one of two paths, and `ScraperId`/`DataSeeder` would remain. Runtime C# scripts use the same Playwright API without the rebuild, so compiled Scrapers add nothing that scripts don't.

### Store Scrapers as files on disk

Scrapers in a mounted folder could be edited in an IDE and versioned in git. Rejected because Sites are created and edited through the API and UI (scripts are uploaded rather than read from disk), and a second source of truth would need file watching and conflict handling with edits made through the API. A JSON column keeps SQLite as the only source of truth.

### Let Scrapers navigate to their own page

This is what Scrapers do today. It's the most flexible, but `Site.Url` would stay decorative and each kind would handle page loading its own way. A Scraper can still navigate elsewhere partway through.

### Detect Known Failures in the pipeline

The pipeline could check every Site for access-denied or blank pages, either from a built-in list or from conditions declared on the Site. Rejected because which states count as Known Failures varies from Site to Site, and only the Scraper knows what it expected to find. Scripts can still check for them, and 0002 proposes them as presets for Steps Scrapers.

### Script bodies with Roslyn scripting

A script could be a bare method body with top-level `await` and globals, run through `CSharpScript`. That's less ceremony, but Roslyn scripting loads every compiled script into the default load context, which never unloads, so each edit would leak an assembly until restart. A bare body is also awkward to write in an IDE. A full class compiles as an ordinary `.cs` file in the author's repo, and `CSharpCompilation` can load it into a collectible context.

### In-browser script editor

Monaco with server-side diagnostics, or full completion, would let scripts be written in the UI. Deferred: uploading a file written in the author's own IDE is simpler for a first release and gets full tooling for free.

### Polymorphic Scraper type behind a value converter

A `System.Text.Json` polymorphic class hierarchy serialized to a string column would give each kind its own type. Rejected because EF can't query or track changes inside a converted value. A flat complex type keeps both.

## Migration and rollout

Each stage ships on its own.

1. **Shared pipeline and C# scripts.** The pipeline (navigation to `Site.Url`, timeout, outcomes, Requested Actions, Test Runs), the script kind, the Site create/update/delete API, and script upload in the UI. A release workflow publishes `SiteChecker.Scripting` to GitHub Packages. `ScraperBase` and `Site.ScraperId` are removed, `DataSeeder` becomes the demo-data seeder, and the database starts fresh (see below).
2. **Steps Scrapers**: see [0002](0002-steps-scrapers.md).
3. **Prompt Scrapers**: see [0003](0003-prompt-scrapers.md).

**Breaking change: fresh database.** Breaking changes are acceptable (see Non-goals), so stage 1 doesn't migrate existing data. The existing migrations are deleted and replaced with a new initial migration for the new model, which loses Site and Site Check history. Stage 1 is deployed as a brand new deployment: the old database file (`site-checker/data/SiteChecker.db`, or the file in the `/app/data` volume) is deleted by hand beforehand. The app doesn't detect an old database; started against one, `MigrateAsync` fails with "table Sites already exists". This avoids a data migration that would turn `ScraperId` into script source.

**Porting the existing Sites.** `PiaLocationScraper` and `BotDetectionScraper` are rewritten as `IScript` classes in `samples/DemoScrapers`. Each is the Scraper's `DoScrapeAsync` minus the `GotoAsync` call, since the pipeline navigates now. `BotDetectionScraper` returns a `FailureScrapeResult` when it finds no results; its script throws instead, which is an Unexpected Failure, as it is today. They also become the demo data (below). Outside Development, after upgrading, the user re-creates the two Sites through the UI and uploads these scripts, or sets `SEED_DEMO_DATA=true` once.

**Demo data.** `DataSeeder` stays, repurposed to seed demo data:
- **When it runs:** controlled by `SEED_DEMO_DATA`, documented in `docs/configuration.md`. When it's unset, seeding is on if the ASP.NET environment is Development and off otherwise. It doesn't depend on `#if DEBUG`, so the Release-built Docker image can enable it too.
- **What it seeds:** the two ported Sites (PIA Location and Bot Detection), with their scripts, Schedules, notification settings, `UseVpn` (PIA Location) and `AlwaysTakeScreenshot` (both, as today). The Bot Detection script relies on `AlwaysTakeScreenshot` for its success screenshot, because a `ScriptOutcome` has no screenshot of its own. It seeds no Site Checks; history builds up from real checks, so Site Checks are still created only through the runner.
- **Only into an empty database:** it adds the demo Sites only when there are no Sites at all, and never updates or deletes. A demo Site you delete stays deleted while any other Site exists; deleting every Site brings the demo Sites back on the next restart if seeding is on. Sites created at runtime are never touched. This removes today's behavior of deleting every Site it doesn't know.
- **Where the script source comes from:** each demo Site maps to a `.cs` file in `samples/DemoScrapers`. The Backend embeds those files with `EmbeddedResource` (not `Compile`, so they're not built into the Backend), and the seeder uploads the matching file's text as the Site's script. These are the same files `Scraper.IntegrationTests` compiles with the runtime compiler, and there's no second copy to keep in sync.

## Testing strategy

The existing test projects (`Backend.Test`, `Scraper.Test`, `Utilities.Test`) are recreated and split by category, so each project's name says what kind of test it holds and how fast it is. Tests worth keeping move into the new projects, and tests of deleted code (`ScraperBase`) go with it. `ScraperServiceTests`' browser-selection cases (`GetBrowserType`) move to `Scraper.UnitTests`, because that logic stays. Both categories run in CI through `dotnet test`. MSTest v4 and the current analyzer rules stay.

| Project | Category | Covers |
| --- | --- | --- |
| `Utilities.UnitTests` | Unit | Today's `Utilities.Test` |
| `Scraper.UnitTests` | Unit | The pipeline with a substituted `IBrowserProvider` and `IPage`; browser selection; `ScriptOutcome` and the result types |
| `Backend.UnitTests` | Unit | Notification channel adapters against a fake HTTP handler (today's `PushoverChannelTests`); Site and Scraper validation |
| `Scraper.IntegrationTests` | Integration | Real Roslyn compilation and collectible load contexts; the demo scripts compiled and run |
| `Backend.IntegrationTests` | Integration | `SiteCheckRunner` through `RunnerHarness` (real DI, migrated in-memory SQLite, `FakeTimeProvider`), notifications, migrations, the demo-data seeder, and the Site API |

**`Scraper.UnitTests`: pipeline**
- Navigates to `Site.Url` before the executor runs, and a navigation error reaches the executor instead of failing the scrape.
- A timeout ends the run even when the executor ignores its token or blocks synchronously, takes the failure screenshot, closes the browser context, and records an Unexpected Failure with the elapsed time; the next check still runs.
- An executor that throws produces an Unexpected Failure and writes the failure dumps.
- A Known Failure keeps its message and Requested Actions.
- Screenshots follow the failure / `AlwaysTakeScreenshot` rule.

**`Scraper.IntegrationTests`: scripts**
- A valid script compiles and runs.
- Compile errors come back as diagnostics with line numbers.
- A file with zero or two `IScript` classes is rejected.
- Replacing a script unloads the old load context, checked with a `WeakReference` to the context after a forced GC. So does finishing a Test Run.
- The cache reuses a compiled script for an unchanged source hash.
- Each script in `samples/DemoScrapers` compiles with the runtime compiler (not just the SDK build). Given a substituted page, it returns the content today's Scraper returns for the same page. The expected values are captured from today's Scrapers before they're deleted, then hard-coded.

**`Backend.IntegrationTests`: runner**, through `SiteCheckRunner`'s public methods with `RunnerHarness` (extended, not mocked):
- **Retry:** creates the Queued retry in the same save as the outcome, behind any already Queued; is honored only once per Failing Run; runs outside the Schedule window; and counts toward the Known Failure Threshold. A successful retry after an unreported run sends no Recovery but still notifies Updated when the content changed.
- **Change VPN Location:** makes the next VPN-routed check rotate first, excluding the current location; ignored for a Site that doesn't use the VPN.
- **Scrape lock:** VPN rotation and manual location changes never overlap a scrape or a Test Run.
- **Known Failure Threshold:** unchanged behavior with Known Failures that carry Requested Actions.
- **Test Runs:** wait for a running check and never overlap one, create no Site Check, send no notification, and deliver their result to the requesting connection only.
- **Deleting a Site mid-check:** the running check ends without an unhandled exception.

**`Backend.IntegrationTests`: demo data**
- With seeding on, an empty database gets the two demo Sites, with their `UseVpn` and `AlwaysTakeScreenshot` settings.
- A database that already has any Site is left unchanged.
- With seeding off, nothing is added.
- API tests built on `WebApplicationFactory` run in Development, so they set `SEED_DEMO_DATA=false`.

**`Backend.IntegrationTests`: API**
- Creating, updating and deleting a Site.
- A script that doesn't compile is rejected with its diagnostics.
- A Scraper whose `Kind` doesn't match its payload, and a timeout that doesn't fit under `BROWSERLESS_TIMEOUT`, are rejected.
- Site responses don't include the script source; `GET /api/site/{id}/script` does.

**Real browser.** Nothing in CI needs one. The demo scripts are checked against live pages manually, using a Test Run after upgrading. If that becomes tedious, a separate opt-in `*.E2ETests` project could run them against a local Playwright browser outside CI.

## Risks and open questions

- **How are releases versioned?** The app has no version or release process today. The package version should follow the app's release tags, so a scheme (for example SemVer tags like `v1.2.0`) has to be chosen before the first publish.
- **Contract changes break uploaded scripts.** Breaking changes to `SiteChecker.Scripting` are acceptable (see Non-goals), but a script that no longer compiles fails its next Site Check as an Unexpected Failure. When the contract changes, bump the package's major version and update the uploaded scripts in the same release.
- **Anyone who can reach the app can run code on the host.** The API has no authentication, and saving a Site and starting a Test Run both accept C# source that runs inside the app. The app container mounts `/var/run/docker.sock` for VPN rotation, and `:ro` on a socket doesn't restrict API calls. A script can therefore start a privileged container and take over the host, and it can also read the database file and environment variables.
  - **Accepted, with a trust boundary.** The app must only be reachable from networks where everyone is trusted: today the home LAN and a private VPN (Tailscale). It must never be exposed to the internet, even through a reverse proxy, unless that proxy requires a login.
  - **Stage 1 documents it** in `README.md` and `docs/configuration.md`, next to the Docker socket mount in `docker-compose.yml`.
  - **Revisit before any wider exposure.** Gating script upload and Test Runs behind an admin token, or adding a login for the whole app, are the options considered.
