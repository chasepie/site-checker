# Plan: Scraper architecture, stage 1 (design 0001)

## Context

Adding a Site today takes a new `ScraperBase` subclass, an `AddScraper<T>()` call, a `DataSeeder` entry and a redeploy. `DataSeeder` also deletes any Site it doesn't know, and `Site.Url` has no effect. Design 0001 (Accepted) and ADRs 0003–0005 turn Sites and their Scrapers into data. One shared pipeline navigates to `Site.Url`, enforces a per-Site timeout and runs the Site's Scraper. Stage 1's only kind is a Script Scraper: an uploaded C# file compiled with Roslyn into a collectible load context. A Scraper ends as Succeeded, as a Known Failure (optionally requesting Change VPN Location or Retry), or as an Unexpected Failure. Sites can be created, edited, test-run and deleted from the UI, the database starts fresh, and `DataSeeder` becomes a demo-data seeder that never deletes. `GLOSSARY.md` already has the new terms.

## Working across sessions

- **First action:** create branch `scraper-architecture-stage-1` from `main`, copy this plan to `PLAN.md` at the repo root, and commit it ("Add stage 1 working plan").
- **Each session** starts by reading `PLAN.md` and `git log main..HEAD`, then continues with the first unchecked commit below.
- **After each phase commit,** tick its box in the Progress list below, add a line about anything that deviated from the plan, and commit `PLAN.md` along with that phase.
- **Before merge,** the last commit deletes `PLAN.md` ("Remove stage 1 working plan"), so it never reaches `main`.

### Progress
- [x] 1. Reorganize the test projects
  - Deviation: `Scraper.IntegrationTests` is created in commit 3, with its first tests, because a test project with no tests fails `dotnet test`.
- [x] 2. Domain renames and a fresh migration
  - Deviation: CLAUDE.md's Notifications bullet and ADR 0002 were updated to the new terms here, not in commit 8, because they named the renamed `DoneDate` column.
- [x] 3. Scripting contract, runtime compiler and demo scripts
  - Deviation: Playwright for .NET throws `System.TimeoutException`; there's no `Microsoft.Playwright.TimeoutException`. Fixed the design doc's example and `PiaLocation.cs` to `catch (TimeoutException)`.
  - Deviation: the reference set lives in `ScriptCompiler` (built in its constructor) rather than a separate `ScriptReferences`. Besides the framework, it includes the dependency closure of Logging.Abstractions, Playwright and the contract, because Playwright targets netstandard2.0 and its public types need `Microsoft.Bcl.AsyncInterfaces`.
  - Deviation: `IScriptCompiler` has `Validate(source, fileName)` (compile only, nothing loaded) for the Site validator in commit 6. `ScriptOutcome` exposes `IsKnownFailure`, `Content`, `KnownFailureMessage` and `RequestedActions`, plus `Success(string)` alongside the implicit conversion.
  - Deviation: `IScriptCompiler` and `ScriptCache` are already registered in `AddScraperServices()`.
- [x] 4. Shared pipeline, new model and switchover
  - Deviation: `TryResult` and its tests were deleted; the pipeline uses plain try/catch for the screenshot and dumps.
  - Deviation: `ScrapeRequest.IsTestRun` is derived (`SiteCheckId is null`) rather than a separate flag. `ScriptSite.UsesVpn` reports whether the page actually goes through the VPN (`BrowserType.BrowserlessVpn`).
  - Deviation: `ScraperKind`, `RequestedAction`, `ScriptScraper` and `ScraperDefinition` were added to `ReinforcedTypingsConfiguration` here, because the frontend build needs them as soon as `Site` changes.
  - Deviation: CLAUDE.md's "Scrapers and Sites" section was rewritten here, since it described deleted code; commit 8 only touches it up.
  - Note: a local `site-checker/data/SiteChecker.db` from `main` must be deleted before running this branch (the fresh `Initial` migration can't apply to it).
- [ ] 5. Requested Actions, the scrape lock and VPN fixes
- [ ] 6. Site API and Test Runs (backend)
- [ ] 7. Frontend
- [ ] 8. Release workflow and docs
- [ ] Remove `PLAN.md`, then open the PR

## Decisions

**From you:**
- **Delivery:** one PR from branch `scraper-architecture-stage-1`, with one commit per phase below. Each commit builds in Release and passes `dotnet test`. `PLAN.md` is kept on the branch while the work is in progress, and removed before merge.
- **Versioning:** SemVer app tags starting at `v0.1.0`. The release workflow packs `SiteChecker.Scripting` with the tag's version. You push the tag after merge; it isn't part of the PR.
- **PIA demo script:** the design's variation. A 403 or 429 is a Known Failure requesting Change VPN Location and Retry. A missing element is a Known Failure requesting Retry. Success content is unchanged.
- **Editor UI:** one routed `SiteEditor` page at `/sites/new` and `/sites/:id/edit`. The `edit-site` modal is deleted.

**My calls (push back if any is wrong):**
- **Two `RequestedAction` enums.** The published contract (`SiteChecker.Scripting`) has one for scripts. `Database` has a pinned, persisted one, and the runner maps between them. `Database` doesn't reference the contract, because the contract drags in Playwright, and a contract version bump shouldn't touch the schema.
- **`ScraperKind` lives only in `Database`.** The Scraper project dispatches on the request's payload type (`ScriptSpec`), so it needs no copy of the enum.
- **`SiteCheck.RequestedActions` records only the actions the runner carried out.** A Retry that isn't honored (one is already queued in this run) isn't recorded. Neither is Change VPN Location on a Site that doesn't use the VPN. This keeps the design's rule that "a recorded Retry always means a retry was queued."
- **Timeout limits.** An invalid `SCRAPE_TIMEOUT` fails startup. An oversized Site timeout is rejected on save. If `BROWSERLESS_TIMEOUT` is later lowered, a stored timeout that no longer fits is clamped at run time with a warning, rather than failing startup.
- **`SiteCheck.VpnLocationId`** is the PIA location id, or `null` for checks that don't use the VPN. The UI shows the location's name, and "No VPN" for `null`.
- **Demo Sites keep today's default Schedule (disabled) and notification settings,** so Development doesn't start scraping on its own.
- **No migration between commits.** The fresh `Initial` migration is regenerated whenever a commit changes the model. Don't deploy mid-branch.

## Projects after stage 1

| Project | Notes |
| --- | --- |
| `src/Scripting/Scripting.csproj` (new) | `AssemblyName`/`PackageId` `SiteChecker.Scripting` (root namespace is already `SiteChecker.Scripting`). References `Microsoft.Playwright` and `Microsoft.Extensions.Logging.Abstractions`. Packable, with package metadata (MIT, repo URL). |
| `src/Scraper` | References Scripting. Adds `Microsoft.CodeAnalysis.CSharp` 5.x (Roslyn with C# 14) to `Directory.Packages.props`. |
| `samples/DemoScrapers/DemoScrapers.csproj` (new) | `ProjectReference` to Scripting, `ImplicitUsings` off, the runtime's global usings as `<Using>` items, `IsPackable=false`. Contains `PiaLocation.cs`, `BotDetection.cs` and `README.md`. |
| `test/Utilities.UnitTests`, `Scraper.UnitTests`, `Backend.UnitTests`, `Scraper.IntegrationTests`, `Backend.IntegrationTests` | Replace `Backend.Test`, `Scraper.Test` and `Utilities.Test`. Built on MSTest.Sdk. `Backend.IntegrationTests` adds `Microsoft.AspNetCore.Mvc.Testing`. |

Add all of them to `SiteChecker.slnx` and commit every new `packages.lock.json` (CI restores with `--locked-mode`).

## Commits

### 1. Reorganize the test projects (mechanical)
- Move the existing tests unchanged:
  - `RepoUtilsTests` → `Utilities.UnitTests`.
  - `ScraperServiceTests`, `TryResultTests`, `ScrapeResultTests`, `ScraperExceptionTests` and `ScraperBaseTests` → `Scraper.UnitTests`. The last three are deleted in commit 4.
  - `PushoverChannelTests` and `SiteTests` → `Backend.UnitTests`.
  - `RunnerHarness`, `SiteCheckRunnerTests`, `NotificationTests` and `MigrationTests` → `Backend.IntegrationTests`.
- Delete the old projects. Update `SiteChecker.slnx` and the Testing/Commands sections of CLAUDE.md.

### 2. Domain renames and a fresh migration
- `CheckStatus.Done` → `Succeeded` (still `= 3`), `SiteCheck.DoneDate` → `CompletedDate`, `RecordEmptyCheckAsync` → `RecordBaselineResetAsync` (content `"[Baseline Reset]"`), and `SiteCheckController.CreateEmptyCheck` → `ResetBaseline`. Update comments and log text in `NotifierService` and `SiteCheckRunner` to the new terms.
- Frontend (`model.ts` is regenerated by the build): `site-check.store.ts`, `site-details.*` and `site-check-details.html`. The menu item becomes **Reset Baseline**.
- Delete `src/Database/Migrations/*`, then run `dotnet ef migrations add Initial` from `src/Database`.
- Replace the backfill `MigrationTests` with one test: an empty database migrates, and `HasPendingModelChanges()` is false.

### 3. Scripting contract, runtime compiler and demo scripts
**Contract** (`src/Scripting`), matching the design's table:
```csharp
public interface IScript { Task<ScriptOutcome> RunAsync(ScriptContext ctx); }
public sealed class ScriptContext { required IPage Page; required NavigationResult Navigation;
    required CancellationToken CancellationToken; required ScriptSite Site; required ILogger Logger; }
public sealed class NavigationResult { IResponse? Response; Exception? Error; bool Succeeded; void EnsureSucceeded(); }
public sealed record ScriptSite(string Name, Uri Url, bool UsesVpn);
public sealed class ScriptOutcome {   // Succeeded(Content) | KnownFailure(Message, RequestedActions)
    public static implicit operator ScriptOutcome(string content);   // ArgumentNullException on null
    public static ScriptOutcome KnownFailure(string message, params RequestedAction[] actions); }
public enum RequestedAction { ChangeVpnLocation, Retry }
public sealed class NavigationFailedException : Exception   // thrown by EnsureSucceeded
```
- `EnsureSucceeded()` throws when `Error` is set, or when `Response` is not `Ok`. A `null` response with no error counts as success (Playwright returns `null` for same-document navigations).
- Move `ScrollToBottomAsync`, `TakeFullPageScreenshotAsync` and the interval-based `ILocator.WaitForAsync` overloads from `src/Scraper/Extensions/PlaywrightExtensions.cs` into the contract. The `Try*`/`SaveHTMLContentAsync` helpers stay in Scraper as internal pipeline helpers. The access-denied and blank-page detectors are deleted in commit 4.

**Compiler** (`src/Scraper/Scripts/`):
- `IScriptCompiler` / `ScriptCompiler` (singleton):
  - `Compile(source, fileName, assemblyName)` returns `CompiledScript` or `IReadOnlyList<ScriptDiagnostic>`.
  - Adds a synthetic syntax tree of `global using` lines for the documented set (`System`, `System.Collections.Generic`, `System.Linq`, `System.Text.RegularExpressions`, `System.Threading`, `System.Threading.Tasks`, `Microsoft.Extensions.Logging`, `Microsoft.Playwright`, `SiteChecker.Scripting`).
  - Uses `LanguageVersion.CSharp14` with `NullableContextOptions.Enable`. Only errors fail the compile.
  - Before emitting, checks with symbols that the file holds exactly one non-abstract class implementing `IScript` with a public parameterless constructor. If not, it reports a diagnostic at the class, or at 1:1 when there's none.
  - Emits the PE and a portable PDB with the source embedded, so stack traces show script line numbers.
- `ScriptReferences`:
  - The `MetadataReference` set: managed DLLs from the shared-framework directory (`typeof(object).Assembly.Location`), plus the assemblies of `ILogger`, `IPage` and `IScript`.
  - Built once in the constructor. `Program` resolves it at startup.
- `CompiledScript : IDisposable` loads into `new AssemblyLoadContext(name, isCollectible: true)`. `CreateInstance()` makes a script per run, and `Dispose()` unloads. The context holds no other references, so unloading works.
- `ScriptCache` (singleton):
  - `GetOrCompile(siteId, sourceHash, source)` replaces and unloads a stale entry when the hash differs.
  - `Evict(siteId)` is called after a script replace or a Site delete.
- `ScriptSource.Hash(string)` returns the SHA-256 hex digest used by the Backend on save.
- `ScriptDiagnostic(FileName, Line, Column, Id, Message)` is a record.

**Demo scripts:**
- `PiaLocation.cs` is the design's example.
- `BotDetection.cs` is today's `DoScrapeAsync` minus `GotoAsync`, using `new Regex(...)` instead of `[GeneratedRegex]`. It throws when it finds no results, and drops its own screenshot because the pipeline's `AlwaysTakeScreenshot` covers it.
- Capture today's outputs first: run `PiaLocationScraper` and `BotDetectionScraper` once against the same NSubstitute pages, then hard-code those values into the new tests.

**Tests** (`Scraper.IntegrationTests`):
- A valid script compiles and runs.
- Errors come back with line numbers.
- A file with zero or two `IScript` classes is rejected.
- Replacing a script, and finishing an uncached (Test Run) compile, both unload the context. Checked with a `WeakReference` to the context, a `[MethodImpl(NoInlining)]` helper and a GC loop.
- The cache reuses the compiled script for an unchanged hash.
- Each `samples/DemoScrapers/*.cs` compiles with the runtime compiler and returns the captured content from a substituted page. Also cover the PIA 403 and missing-element Known Failures.

### 4. Shared pipeline, new model and switchover
**Pipeline** (`src/Scraper`):
- `IBrowserProvider` has `GetBrowserType(bool useVpn)` and `OpenAsync(BrowserType, ct)`, which returns an `IBrowserSession : IAsyncDisposable { IPage Page; Task CloseAsync(); }`. `BrowserProvider` holds today's `GetBrowserType`, `LaunchLocalBrowserAsync`, `LaunchBrowserlessBrowserAsync` and the context/page/1920×1080 code from `ScraperService`. Closing is idempotent.
- `ScrapeRequest` (rewritten): `int? SiteCheckId`, `ScrapeSite Site` (Id?, Name, Url, UseVpn), `BrowserType`, `TimeSpan? Timeout`, `bool AlwaysTakeScreenshot`, `bool IsTestRun`, `ScraperSpec Scraper` (the abstract record; stage 1 has only `ScriptSpec(Source, SourceHash, FileName)`).
- `ScrapeResult` replaces `IScrapeResult`, `Success`/`FailureScrapeResult` and the `ScraperException` hierarchy:
  - Fields: `ScrapeOutcome Outcome` (Succeeded, KnownFailure, UnexpectedFailure), `Content?`, `Message?`, `RequestedActions` (contract enum), `Screenshot?`, `Duration`, `Exception?`, `Diagnostics`.
  - Factories: `Succeeded(...)`, `KnownFailure(...)`, `Unexpected(...)`.
- `IScrapeExecutor` has `Type SpecType` and `Task<ScrapeResult> ExecuteAsync(ExecutorContext)`, where `ExecutorContext` holds the page, navigation, request and token. It returns Succeeded or KnownFailure; anything it throws is Unexpected.
- `ScriptExecutor`:
  - Takes the script from `ScriptCache` for a real check, or compiles it uncached and disposes it after a Test Run. A compile failure is Unexpected, with its diagnostics.
  - Builds the `ScriptContext`. The logger comes from `ILoggerFactory`, inside `BeginScope` with the Site and Site Check.
  - Maps `ScriptOutcome` to `ScrapeResult`.
- `ScraperService : IScraperService` (`GetBrowserType`, `ScrapeAsync(request, ct)`):
  1. Opens the session.
  2. Calls `Task.Run(NavigateAndExecuteAsync)`. That method catches navigation exceptions into `NavigationResult`, then runs the executor for `request.Scraper.GetType()`.
  3. Awaits the task with `.WaitAsync(timeout, timeProvider, ct)`. The executor also gets a token that's cancelled at the timeout.
  4. On timeout: takes the failure screenshot, writes the HTML dump, then calls `session.CloseAsync()` and observes the abandoned task's exception. The result is Unexpected: "Timed out after N s".
  5. Screenshot rule: on failure, or when `AlwaysTakeScreenshot` is set. The screenshot and the HTML dump share a 10 s budget outside the Site timeout.
  6. `FailureArtifacts` (moved out of `ScraperBase`) writes the HTML and exception dumps as `{SiteCheckId}_{SiteId}` on an Unexpected Failure, and never for a Test Run. Its logs directory is injectable, so tests write to a temp directory.
  7. Every exception is converted here, and only here.
- `ScrapeTimeouts` (singleton):
  - Reads `SCRAPE_TIMEOUT` (seconds, default 120) and `BROWSERLESS_TIMEOUT` (ms, default 180000).
  - `MaxTimeout` is `BROWSERLESS_TIMEOUT` minus the 10 s budget.
  - Its constructor throws if the default doesn't fit, and `Program` resolves it at startup so that fails fast.
  - `Resolve(TimeSpan?)` clamps an oversized stored timeout and logs a warning.
- `AddScraperServices()` registers the provider, executor, compiler, cache, timeouts and service.
- Delete `Scrapers/`, `ScraperBase`, `Exceptions/`, the detectors and `ScrapeRequestExtensions`.

**Model** (`src/Database/Model`):
- New: `ScraperKind { Script = 1 }`, `ScraperDefinition { Kind; ScriptScraper? Script }` and `ScriptScraper { required FileName; required SourceHash; required DateTime UploadedAt }`.
  - Mapped with `ComplexProperty(s => s.Scraper, b => b.ToJson())`. EF 10 optional complex types need a required member, which `ScriptScraper` has.
  - New `RequestedAction { ChangeVpnLocation = 1, Retry = 2 }`.
- `SiteScript`:
  - Holds `SiteId` (PK and FK) and `Source`, configured with `HasOne(...).WithOne(s => s.SiteScript)` and cascade delete. The navigation `Site.SiteScript` is `[JsonIgnore]`.
  - It deliberately does **not** implement `IEntityWithId`, so `ChangesInterceptor` never broadcasts the source.
- `Site`: drop `ScraperId` and its unique index, and add `Scraper` (`ScraperDefinition`). `SiteUpdate` gains `int? TimeoutSeconds`, and `Site.Update` copies it.
- `SiteCheck`: add `List<RequestedAction> RequestedActions = []` (a primitive collection stored as a JSON column). `VpnLocationId` now holds the location id.
- Regenerate `Initial`.

**Backend:**
- `SiteCheckExtensions.Update(ScrapeResult, completedDate)` sets the status, `Value` (content or message) and `FailureKind`, and puts the exception type in `Metadata`.
- `SiteCheckRunner.PerformCheckAsync`:
  - Loads the check with its Site and `SiteScript`, and builds a `ScriptSpec`. A missing or mismatched payload throws, which marks the check failed.
  - Calls `ScrapeAsync(request, ct)`.
  - Records `VpnLocationId = location?.Id`.
- `DataSeeder` → `DemoDataSeeder`:
  - Seeds only when `SEED_DEMO_DATA` is true, or when it's unset and the environment is Development, and only when `!Sites.Any()`. It never deletes.
  - Adds two Sites using today's URLs, `UseVpn` and `AlwaysTakeScreenshot` values, each with a `SiteScript` read from embedded resources.
  - Embedded in `Backend.csproj` with `<EmbeddedResource Include="../../samples/DemoScrapers/*.cs" LogicalName="DemoScrapers.%(Filename)%(Extension)" />`. The Dockerfile's `COPY . .` already brings in `samples/`.
- `docker-compose.yml`: add `BROWSERLESS_TIMEOUT=${BROWSERLESS_TIMEOUT:-180000}` on `app`, and set `TIMEOUT=${BROWSERLESS_TIMEOUT:-180000}` on both Browserless containers.
- Frontend: compile fixes only (`scraperId` is gone).

**Tests:**
- `Scraper.UnitTests`, using an NSubstitute `IBrowserProvider`, `IBrowserSession`, `IPage` and fake executors:
  - The pipeline navigates to `Site.Url` before the executor runs, and a navigation exception reaches the executor.
  - A timeout ends the run even when the executor ignores its token or blocks synchronously (block on a `ManualResetEventSlim` released in cleanup). It takes the screenshot, calls `CloseAsync` and reports the elapsed time.
  - A throwing executor is Unexpected and writes the dumps; a Test Run writes none.
  - A Known Failure keeps its message and actions.
  - The screenshot rule holds.
  - The `GetBrowserType` cases move to `BrowserProvider`.
  - `ScriptOutcome`: a null string throws.
- Update `RunnerHarness`: `FakeScraperService` gets the new signature and a settable `BrowserType`, and `AddSiteAsync` adds a script Site.
- Delete `ScraperBaseTests`, `ScraperExceptionTests` and `ScrapeResultTests`, and add result-type tests.
- `Backend.IntegrationTests` demo-data cases:
  - Seeding on with an empty database adds the two Sites with their settings.
  - Any existing Site leaves the database unchanged.
  - Seeding off adds nothing.

### 5. Requested Actions, the scrape lock and VPN fixes
**PiaService split** (`src/Backend/Services/VPN`), so the runner tests use the real rotation logic:
- New `IPiaContainers` with `IsVpnRunningAsync`, `ListLocationsAsync`, `ReadCurrentLocationIdAsync` and `SetLocationAndRestartAsync(id)`. `DockerPiaContainers` holds today's Docker code.
- `PiaService` keeps the logic:
  - The location cache, and a new `ExcludeLocation(id)`.
  - Resetting exclusions when fewer than 5 locations stay eligible.
  - `ChangeLocationAsync` now picks the next eligible location after the current one in the full list order. Today it picks the first eligible one because `FindIndex` returns -1.
- `AddPiaService()` registers both.

**`SiteCheckRunner`:**
- A `SemaphoreSlim _scrapeLock` wraps VPN resolve and rotate, the pre-scrape save, the scrape, `RunTestAsync` and a new public `ChangeVpnLocationAsync(excludeCurrent, ct)`. `VpnController.ChangeLocation` calls that method and resets `_lastVpnChangeTimestamp`.
- `ResolveVpnLocationAsync`: for `BrowserlessVpn`, it drains the pending failed-location ids and calls `ExcludeLocation` on each. It rotates if the current location is one of them, or if `VPN_CHANGE_INTERVAL` has elapsed. Other browser types get `null`.
- Recording the outcome, all in one save:
  - **Retry:** honored when the outcome is a Known Failure requesting Retry and no check in the current Failing Run already recorded a Retry. The runner then adds a Queued `SiteCheck(site, now)` under `_createLock`. It goes to the back of the queue and ignores the Schedule.
  - **Change VPN Location:** honored only when the check ran on a real VPN location. The location id is added to the pending set after the save succeeds.
- Extract the Failing Run query from `NotifierService.DecideAsync` (`finishedBefore`/`failedRunBefore`) into `src/Database/Extensions/SiteCheckQueries.cs`, and use it in both places.
- `RunTestAsync(ScrapeRequest, ct)`: runs under the lock with the current VPN location, no rotation and no pending exclusions applied, and returns the `ScrapeResult`.

**Tests** (`Backend.IntegrationTests`, with a `FakePiaContainers` in the harness):
- **Retry:**
  - Queued in the same save as the outcome, behind any already Queued checks.
  - Honored once per Failing Run.
  - Runs outside the Schedule window.
  - Counts toward the Known Failure Threshold.
  - A successful retry after an unreported run sends no Recovery, but still notifies Updated when the content changed.
- **Change VPN Location:**
  - A failure on A excludes A, the next VPN check rotates first, and the retry doesn't run on A.
  - If a manual A→B change happens first, A is excluded, B isn't, there's no second rotation, and the retry runs on B.
  - Ignored for a Site that doesn't use the VPN.
  - Rotation moves to the next eligible location after the current one.
- **Scrape lock:** a manual change and a Test Run wait for a running scrape. Checked with a `TaskCompletionSource`-gated fake scrape.
- **Delete mid-check:** `OnScrape` deletes the Site, `RunNextAsync` returns without throwing, and the check is gone.
- **Threshold:** Known Failures with actions behave as before.

### 6. Site API and Test Runs (backend)
**DTOs** (`src/Backend/Models/`):
- `SiteRequest : SiteUpdate { required ScraperRequest Scraper }`
- `ScraperRequest { Kind; ScriptUpload? Script }` and `ScriptUpload { FileName; string? Source }`
- `SiteValidationResult { Errors; Diagnostics }`
- `TestRunRequest { TestRunId (string); ConnectionId; Name; Url; UseVpn; AlwaysTakeScreenshot; TimeoutSeconds; Scraper }`
- `TestRunResult { TestRunId; ScrapeOutcome Outcome; Content?; Message?; RequestedActions; Screenshot?; DurationMs }`

**`SiteValidator`** (`IScriptCompiler` injected, so it can be unit tested):
- The payload must match `Kind`.
- Create requires a source.
- `TimeoutSeconds` must be between 1 and `ScrapeTimeouts.MaxTimeout`.
- The script must compile, and its diagnostics come back.

**`SiteController`** (keeps the `CancellationToken` property and `OkOrNotFound` conventions):
- `POST /api/site` and `PUT /api/site/{id}` return 400 with `SiteValidationResult` when invalid.
  - On PUT, a `null` `Source` keeps the stored script.
  - A new source sets the hash and `UploadedAt` (from `TimeProvider`), upserts `SiteScript`, and calls `ScriptCache.Evict` after the save.
- `DELETE /api/site/{id}` removes the Site (cascade) and evicts its script.
- `GET /api/site/{id}/script` returns `SiteScript`.
- `POST /api/site/test-run` validates, then calls `TestRunService.Start` and returns `202 Accepted`.

**`TestRunService`** (singleton):
- Starts `Task.Run`, using `ApplicationStopping` as the token.
- Calls `runner.RunTestAsync`, maps the result, then sends it with `IHubContext<DataHub>.Clients.Client(connectionId)` under `SignalRConstants.OnTestRunCompletedKey`.
- Logs any errors and still sends an Unexpected result.

**Type bridge:**
- Add the new enums (`ScraperKind`, `RequestedAction`, `ScrapeOutcome`) and classes to `ReinforcedTypingsConfiguration`, with each dependency listed before the types that use it, since Zod constants are emitted in list order.
- Add `OnTestRunCompletedKey` to `SignalRConstants`.

**Tests:**
- `Backend.UnitTests`: validator rules.
- `Backend.IntegrationTests`, via `WebApplicationFactory<Program>`. `ConfigureTestServices` swaps the DbContext for in-memory SQLite (otherwise it falls back to the repo's `site-checker/data` file), and `SEED_DEMO_DATA=false` is set. Cases:
  - Create, update and delete.
  - A script that doesn't compile returns diagnostics.
  - A `Kind`/payload mismatch and an oversized timeout are rejected.
  - Site responses contain no source; `/script` does.
- `TestRunService` with a substituted `IHubContext`:
  - Delivers to `Client(id)` only, never `All`.
  - Creates no Site Check and sends no notification.

### 7. Frontend
- `SiteEditor` (standalone, `inject()`, Signal Forms like today's `edit-site`) is lazy-loaded at `sites/new` and `sites/:id/edit`. Delete `components/edit-site`.
  - Adds a timeout field; blank means the default.
  - Script section:
    - A `.cs` picker that reads `file.text()`.
    - The file name and upload date.
    - A read-only `<pre>` source view, fetched with `getSiteScript` for existing Sites, and a Blob download.
    - The diagnostics list, parsed from a 400 response with the `SiteValidationResult` Zod schema.
  - Test-run panel:
    - Generates the id with `crypto.randomUUID()` and subscribes to `signalr.testRunCompleted$` filtered by that id before POSTing with `connectionId`.
    - Shows a spinner, then the outcome, content, message, Requested Actions, the screenshot (base64 data URL) and the duration.
- `SiteStore`:
  - `createSite`, `updateSite(id, req)` and `deleteSite` (clears the selection and navigates home).
  - `SiteCheckStore.removeChecksForSite(siteId)` (and the screenshot cache) runs both after its own delete and on a `Site` Deleted event from `entityDeleted$`.
- `SiteDetails`:
  - Edit navigates to the editor, Delete Site asks for confirmation first, and Reset Baseline stays.
  - Shows the Scraper's file name and the timeout.
  - The VPN column shows the location's name (looked up through `VpnStore`), and "No VPN" for `null`.
  - Requested Actions appear in the checks table and in `site-check-details`.
- `SiteList` gets a **New Site** button, and `SignalrService` gets `testRunCompleted$`.
- `npm run lint` passes. The scaffolding specs stay as they are (CI doesn't run them).

### 8. Release workflow and docs
- `.github/workflows/release.yml` runs on `push: tags: ['v*']` with permissions `contents: read` and `packages: write`. It restores `src/Scripting` with `--locked-mode`, runs `dotnet pack src/Scripting -c Release -p:Version=${GITHUB_REF_NAME#v}`, then runs `dotnet nuget push` to `https://nuget.pkg.github.com/<owner>/index.json` with `GITHUB_TOKEN`. Confirm the owner with `git remote -v`.
- `samples/DemoScrapers/README.md` covers:
  - The `PackageReference` an author adds.
  - `nuget.config` for GitHub Packages, with a PAT that has `read:packages`.
  - The `<Using>` set.
  - The one-file, no-generators rule.
- `docs/configuration.md` and `example.env`: `SCRAPE_TIMEOUT`, `BROWSERLESS_TIMEOUT` and `SEED_DEMO_DATA`, plus a trust-boundary section.
- `README.md`: the trust boundary, and replacing the outdated scraper example with a script example and "add a Site in the UI".
- `docker-compose.yml`: a trust-boundary comment at the `docker.sock` mount.
- `CLAUDE.md`:
  - Notifications, in the new terms.
  - "Scrapers and Sites", rewritten: Sites are data, scripts are uploaded, the pipeline navigates, and `DemoDataSeeder` never deletes.
  - Testing/Commands, with the new project names.
  - Configuration: the three new variables and the trust boundary.
- ADRs 0003–0005: remove the "lands with stage 1 / until then" banners.

### Final: remove the working plan
- `git rm PLAN.md` and commit. Then push the branch and open the PR, whose body summarizes the eight phases.

## Critical files
`src/Backend/Services/CheckQueue/SiteCheckRunner.cs`, `src/Scraper/ScraperService.cs` (rewritten), `src/Scraper/Scripts/*` (new), `src/Scripting/*` (new), `src/Database/Model/{Site,SiteCheck}.cs`, `src/Database/SiteCheckerDbContext.cs`, `src/Backend/Services/VPN/PiaService.cs`, `src/Backend/Services/NotifierService.cs`, `src/Backend/Controllers/{Site,SiteCheck,Vpn}Controller.cs`, `src/Backend/Services/DataSeeder.cs` → `DemoDataSeeder.cs`, `src/Backend/Generators/ReinforcedTypingsConfiguration.cs`, `src/Backend/Program.cs`, `test/Backend.IntegrationTests/RunnerHarness.cs`, `src/Frontend/src/app/{services,components}/...`

## Verification
- **After every commit:**
  - `dotnet restore --locked-mode && dotnet build --configuration Release --no-restore && dotnet test`
  - In `src/Frontend`: `npm run lint`
- **Package:** `dotnet pack src/Scripting -c Release -p:Version=0.1.0`, then inspect the `.nupkg` (contract DLL, XML docs, dependencies on Playwright and Logging.Abstractions only).
- **End to end** (manual, using the `run` skill or Playwright MCP to drive the UI):
  1. Delete `site-checker/data/SiteChecker.db`.
  2. Start the full stack (`docker compose up --build`), or run locally with `USE_LOCAL_BROWSER`. In Development, both demo Sites should be seeded with their scripts.
  3. Test Run each demo Site from the editor against the live pages: content, screenshot and duration should appear.
  4. Queue a real check. It should succeed, its VPN location name should show, and an Updated notification should follow on a change.
  5. Create a Site by uploading a new `.cs`. Upload a broken script and confirm the diagnostics appear.
  6. Change the VPN location from the VPN page while a check runs; the change should wait for the check.
  7. Delete a Site. Its checks should disappear in this tab and in a second open tab.
