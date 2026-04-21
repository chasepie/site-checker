# Hexagonal Architecture Migration — site-checker

## Context

The project is currently a three-project .NET 10 solution (`Backend` → `Database`/`Scraper` → `Utilities`) where business rules are scattered across controllers, background services, EF entities, and extension methods. Specific examples: "is a site due for a check?" lives in [SiteCheckTimer.cs](src/Backend/Services/CheckQueue/SiteCheckTimer.cs); "is this a known failure?" string-matches exception class names in [SiteCheckExtensions.cs](src/Backend/Extensions/SiteCheckExtensions.cs); "should we notify?" runs a DB query inside [NotifierService.cs](src/Backend/Services/NotifierService.cs); entity-change propagation piggybacks on an EF Core [ChangesInterceptor.cs](src/Database/ChangesInterceptor.cs) + an `IEntityChangeService` observer.

The goal is to **learn hexagonal (ports & adapters) architecture by doing it**. After the migration:
- `src/Domain` holds entities, value objects, enums, events, and ports — zero non-BCL dependencies
- `src/Application` holds use cases orchestrating ports — references Domain only
- `src/Database`, `src/Scraper`, `src/Backend` become **adapters** that implement Domain ports
- `test/Domain.Test` and `test/Application.Test` prove the domain and use cases are testable without a DB, a browser, or a web host

Boundaries are enforced by **project references** (not naming conventions), so the compiler prevents regressions. Each step leaves the app building and running so you can verify incremental progress.

## Starting state

- Branch: `hexagonal2` (fresh from main; the prior `hexagonal` branch was abandoned and should not be consulted)
- Last commit `b3a6723` removed the `test/` directory but [SiteChecker.slnx](SiteChecker.slnx) still references `test/Scraper.Test/Scraper.Test.csproj` and `test/Utilities.Test/Utilities.Test.csproj` — Step 1 removes those stale entries
- [global.json](global.json) provides `MSTest.Sdk 4.1.0` via `msbuild-sdks`, so test csprojs use `Sdk="MSTest.Sdk"` with no version attribute
- [Directory.Build.props](Directory.Build.props): `TreatWarningsAsErrors=true`, `AnalysisMode=Recommended`, `EnableReferenceTrimmer=true`, `GenerateDocumentationFile=true` — CA analyzers are build errors, so new projects need targeted `NoWarn` for CA1711 (port names), RT0002 (empty project), CS1591 (already globally suppressed)
- [Directory.Packages.props](Directory.Packages.props) already lists `NSubstitute 5.3.0` and `Microsoft.Extensions.Logging.Abstractions 10.0.5` — use centrally-versioned `PackageReference` entries

---

## Step 1 — Scaffold Domain and Application projects

**Goal:** Create the two new projects with correct references and clean up the stale slnx entries.

**Actions:**
- Create [src/Domain/Domain.csproj](src/Domain/Domain.csproj): `Microsoft.NET.Sdk`, no PackageReferences, no ProjectReferences, `<NoWarn>$(NoWarn);RT0002;CA1711</NoWarn>`
- Create [src/Application/Application.csproj](src/Application/Application.csproj): same NoWarn, one `<ProjectReference Include="../Domain/Domain.csproj" />`
- Add a single placeholder file in each (e.g. `Domain/_Placeholder.cs` with just `namespace SiteChecker.Domain;`) so doc generation has something to produce
- Update [SiteChecker.slnx](SiteChecker.slnx): add the two new projects; **remove** the stale `test/Scraper.Test` and `test/Utilities.Test` entries (those directories don't exist)
- Update [src/Backend/Backend.csproj](src/Backend/Backend.csproj) to reference both new projects; leave Database and Scraper references as-is for now

**Why this matters for hexagonal:** The hexagon is enforced by project references, not folders. `Domain` with zero outward references means the compiler physically prevents domain code from importing EF Core, Playwright, or ASP.NET. `Application` depending on Domain only means use cases can only talk to infrastructure through interfaces that Domain owns.

**Checkpoint:** `dotnet build` succeeds; app runs unchanged.

---

## Step 2 — Move pure value types and enums into Domain

**Goal:** Move types that already have zero framework dependencies.

**Actions:**
- Move [src/Database/Model/IEntityWithId.cs](src/Database/Model/IEntityWithId.cs) → `src/Domain/Common/IEntityWithId.cs`
- Extract `CheckStatus` enum from [src/Database/Model/SiteCheck.cs](src/Database/Model/SiteCheck.cs) into `src/Domain/SiteChecks/CheckStatus.cs`
- Extract `PushoverPriority` enum from [src/Database/Model/PushoverConfig.cs](src/Database/Model/PushoverConfig.cs) into `src/Domain/Sites/PushoverPriority.cs`
- Move `PagedResponse<T>` (search for it) → `src/Domain/Common/PagedResponse.cs`
- Add `<ProjectReference Include="../Domain/Domain.csproj" />` to [src/Database/Database.csproj](src/Database/Database.csproj)
- Update `using` directives at every call site; update [src/Backend/Generators/ReinforcedTypingsConfiguration.cs](src/Backend/Generators/ReinforcedTypingsConfiguration.cs) type references
- Delete the Domain placeholder file

**Why this matters for hexagonal:** Enums and marker interfaces *are* domain vocabulary. They describe invariants ("a check is in one of five states") with no knowledge of persistence or transport. First litmus test: "does this type need framework knowledge to exist?" If no, it's Domain.

**Checkpoint:** Build passes; `src/Frontend/src/app/generated/model.ts` regenerates byte-identical output (namespaces moved but RT resolves them by type).

---

## Step 3 — Move value objects (PushoverConfig, DiscordConfig, SiteSchedule) into Domain

**Goal:** Strip serialization attributes off value objects; move them to Domain.

**Actions:**
- Move [src/Database/Model/PushoverConfig.cs](src/Database/Model/PushoverConfig.cs) and [src/Database/Model/DiscordConfig.cs](src/Database/Model/DiscordConfig.cs) → `src/Domain/Sites/`
- Extract `SiteSchedule` from `Site.cs` → `src/Domain/Sites/SiteSchedule.cs`
- Remove `[JsonNumberHandling(...)]` from `DiscordConfig.ChannelId`; register equivalent behavior centrally in [src/Backend/Program.cs](src/Backend/Program.cs) via `JsonSerializerOptions` (or keep the attribute via an adapter-side partial class if that's simpler short-term)
- Keep the `Update(...)` mutation methods — they're domain behaviors
- EF Fluent mapping in [src/Database/SiteCheckerDbContext.cs](src/Database/SiteCheckerDbContext.cs) already uses `ComplexProperty(... ToJson())`; no change needed since CLR types are the same

**Why this matters for hexagonal:** Value objects model concepts ("a schedule window with an interval"). When they carry EF or JSON attributes they're admitting knowledge of their adapters. Rule: infrastructure-configuring attributes belong on the adapter side (Fluent API, `JsonSerializerOptions`). Domain types describe *what they are*, not *how they're stored*.

**Checkpoint:** Build passes; existing Discord ChannelId still serializes as a string over the wire.

---

## Step 4 — Move Site, SiteCheck, SiteCheckScreenshot entities into Domain

**Goal:** Move the aggregate roots; replace EF/JSON attributes with Fluent API and central JSON config.

**Actions:**
- Move [src/Database/Model/Site.cs](src/Database/Model/Site.cs) (both `Site` and `SiteUpdate`) → `src/Domain/Sites/Site.cs`
- Move [src/Database/Model/SiteCheck.cs](src/Database/Model/SiteCheck.cs) → `src/Domain/SiteChecks/SiteCheck.cs`
- Move `SiteCheckScreenshot` → `src/Domain/SiteChecks/SiteCheckScreenshot.cs`
- Drop `[Index(...)]`, `[NotMapped]`, `[JsonIgnore]`, `[Range(...)]` attributes
- Rename public constant `KNOWN_FAILURES_THRESHOLD_DEFAULT` → `KnownFailuresThresholdDefault` (CA1707)
- In [src/Database/SiteCheckerDbContext.cs](src/Database/SiteCheckerDbContext.cs) `OnModelCreating`, add Fluent replacements:
  - `modelBuilder.Entity<Site>().HasIndex(s => s.ScraperId).IsUnique();`
  - `modelBuilder.Entity<SiteCheckScreenshot>().HasIndex(s => s.SiteCheckId).IsUnique();`
  - `.Ignore(sc => sc.MetadataLocal)` on `SiteCheck` (replaces `[NotMapped]`)
- Replace `[JsonIgnore]` on nav properties by either (a) introducing response DTOs in Step 11, or (b) short-term: register a `DefaultJsonTypeInfoResolver` modifier in `Program.cs` that skips those properties
- Replace `SiteCheck.Update(Exception ex)` with `SiteCheck.MarkSucceeded(string content)` and `SiteCheck.MarkFailed(string errorMessage, string? failureCategory)` — domain shouldn't take `Exception` as input
- Run `dotnet ef migrations script` before and after to confirm schema delta is empty

**Why this matters for hexagonal:** Entities are the heart of the domain. An entity carrying `[Index]`, `[NotMapped]`, `[JsonIgnore]`, `[Range]` leaks storage schema, storage mapping, and wire shape. Moving them to Domain forces each concern to its proper adapter. The resistance you'll feel (e.g. "but I needed `[JsonIgnore]` to stop JSON cycles") is the lesson — cycles are a consequence of bidirectional EF navigation (an infrastructure detail); the fix is DTOs at the edge.

**Checkpoint:** Build passes; HTTP GETs return identical JSON; EF migration diff is empty; app runs end-to-end.

---

## Step 5 — Create the first port: `IWebScraperPort`; reshape Scraper as an adapter

**Goal:** Flip the Scraper project from "thing Backend depends on" to "adapter implementing a Domain port."

**Actions:**
- Create `src/Domain/Scraping/IScrapeResult.cs`, `SuccessScrapeResult.cs`, `FailureScrapeResult.cs` — domain versions; no `Exception` reference. Failures carry a `FailureCategory` string/enum (`Known`, `AccessDenied`, `BlankPage`, `Unexpected`)
- Create `src/Domain/Scraping/ScrapeRequest.cs` (drop `BrowserType` — that's adapter-internal)
- Create `src/Domain/Scraping/IWebScraperPort.cs`: `Task<IScrapeResult> ScrapeAsync(ScrapeRequest req, CancellationToken ct)`
- Add `<ProjectReference Include="../Domain/Domain.csproj" />` to [src/Scraper/Scraper.csproj](src/Scraper/Scraper.csproj)
- Rename [src/Scraper/ScraperService.cs](src/Scraper/ScraperService.cs) to implement `IWebScraperPort`; keep existing `ScraperException` hierarchy **inside** Scraper; add a small adapter-local mapper: `ScraperException -> FailureCategory`. This replaces the string-name matching in `SiteCheckExtensions.IsKnownExceptionFailure`
- Delete the old Scraper-project `IScrapeResult`/`SuccessScrapeResult`/`FailureScrapeResult`/`ScrapeRequest` — superseded by Domain versions
- Update [src/Backend/Services/CheckQueue/SiteCheckProcessor.cs](src/Backend/Services/CheckQueue/SiteCheckProcessor.cs) to call `IWebScraperPort` and `SiteCheck.MarkSucceeded` / `MarkFailed`. Delete `SiteCheckExtensions.Update(IScrapeResult)` and `IsKnownExceptionFailure`

**Why this matters for hexagonal:** A **port** is an interface *owned by Domain* describing a capability. An **adapter** is an infrastructure implementation. The dependency inverts. Exceptions are the anti-corruption boundary: Domain never references `ScraperException` because Playwright is a technology, not a business concept. The adapter translates at the edge.

**Checkpoint:** Scrapes run end-to-end; known-failure classification still works via `FailureCategory`.

---

## Step 6 — Introduce the first use case (`RunSiteCheckUseCase`) and repository ports

**Goal:** Extract orchestration out of `SiteCheckProcessor` into an Application use case behind repository ports.

**Actions:**
- Create `src/Domain/Sites/ISiteRepository.cs` and `src/Domain/SiteChecks/ISiteCheckRepository.cs`. Method names describe domain intent, not SQL: `GetByIdAsync`, `GetLatestSuccessfulBefore`, `AddAsync`, `SaveChangesAsync`. **No `IQueryable<T>` in signatures** — that leaks EF
- Create `src/Application/UseCases/RunSiteCheckUseCase.cs` injecting `ISiteCheckRepository`, `ISiteRepository`, `IWebScraperPort`, `ILogger<RunSiteCheckUseCase>`
- Create `src/Database/Repositories/EfSiteRepository.cs` and `EfSiteCheckRepository.cs` wrapping `SiteCheckerDbContext`
- Register ports in [src/Backend/Program.cs](src/Backend/Program.cs) as `Scoped`; keep the existing `IServiceScopeFactory.CreateAsyncScope()` pattern inside `SiteCheckProcessor` since it's a singleton BackgroundService
- `SiteCheckProcessor.ExecuteAsync` shrinks to: dequeue → `await _useCase.ExecuteAsync(id, ct)`

**Why this matters for hexagonal:** A **use case** names a single application operation in terms of ports. **Repositories** abstract persistence as a *collection* of domain objects, not a queryable table. The renamed methods read like sentences ("get the latest successful check before this one") instead of LINQ — that renaming is the win.

**Checkpoint:** Queued checks still execute and persist; SignalR still broadcasts (still via `ChangesInterceptor` until Step 10).

---

## Step 7 — Add `test/Domain.Test` with pure, mock-free tests

**Goal:** Prove the domain is testable with zero mocks.

**Actions:**
- Create `test/Domain.Test/Domain.Test.csproj` — `Sdk="MSTest.Sdk"` (no version attribute), `<ProjectReference Include="../../src/Domain/Domain.csproj" />`, `<NoWarn>CA1707</NoWarn>` (MSTest naming)
- Add to [SiteChecker.slnx](SiteChecker.slnx)
- Write tests covering: `SiteCheck.MarkSucceeded` / `MarkFailed` state transitions; `Site.Update(SiteUpdate)` field copying; `SiteSchedule` invariants (start < end, interval > 0)
- **No NSubstitute** in this project — if you want to mock something, that dependency belongs behind a port and the test belongs in Application.Test

**Note on MSTest quirks (learned from prior attempt):** `Assert.ThrowsExceptionAsync` doesn't exist — use `Assert.ThrowsExactlyAsync<T>(...)`. `MSTEST0032` fires on assertions against compile-time constants. `CA2201` fires on `new Exception()` — use specific exception types.

**Why this matters for hexagonal:** The "test without mocks" property is the diagnostic for a healthy domain. Pure domain tests compile with zero infrastructure NuGet packages, run in milliseconds, and survive infrastructure change. That's the core payoff of the architecture.

**Checkpoint:** `dotnet test test/Domain.Test` is green.

---

## Step 8 — Extract scheduling + VPN rotation as domain policy + adapter

**Goal:** Peel two more business rules out of background services.

**Actions:**
- Move "is this site due?" from [SiteCheckTimer.cs](src/Backend/Services/CheckQueue/SiteCheckTimer.cs) into `SiteSchedule.IsDue(DateTimeOffset now, SiteCheck? latestCheck)` — pure domain method
- Create `src/Domain/Common/IClock.cs` with `DateTimeOffset UtcNow`; implement `SystemClock : IClock` in Backend. Domain must never call `DateTime.UtcNow` directly (testability)
- Create `src/Domain/Scraping/IVpnRotationPort.cs` (`GetLocationAsync`, `ChangeLocationAsync(bool excludeCurrent)`); in Backend, rename [src/Backend/Services/VPN/PiaService.cs](src/Backend/Services/VPN/PiaService.cs) to implement it (keep Docker client + file IO internal to the adapter)
- Create `src/Application/UseCases/EvaluateScheduledSitesUseCase.cs` — iterates scheduled sites, calls `IsDue`, creates new `SiteCheck`, saves
- [SiteCheckTimer.cs](src/Backend/Services/CheckQueue/SiteCheckTimer.cs) becomes a thin wrapper: tick → resolve scope → call use case
- Wire `IVpnRotationPort` into `RunSiteCheckUseCase` (or a policy object) so VPN rotation is decided at the use-case layer, not the processor

**Why this matters for hexagonal:** A **domain service** handles logic that doesn't fit on a single entity ("is due" crosses Site + SiteCheck + clock). Treating time as infrastructure via `IClock` is the first honest concession that "now" is an external dependency. Even single-implementation ports like `IVpnRotationPort` pay off: the use case reads as business language without Docker, JSON, or file IO in sight.

**Checkpoint:** Scheduled checks still fire on the minute; VPN rotates as before.

---

## Step 9 — Add `test/Application.Test` with NSubstitute on ports

**Goal:** Prove use cases are testable with in-memory port substitutes and a fake clock.

**Actions:**
- Create `test/Application.Test/Application.Test.csproj` — `Sdk="MSTest.Sdk"`, references `src/Application` and `src/Domain`, `<PackageReference Include="NSubstitute" />` (central version), `<NoWarn>CA1707</NoWarn>`
- Add to [SiteChecker.slnx](SiteChecker.slnx)
- Write tests:
  - `RunSiteCheckUseCase` with a scraper substitute returning success → repository saves a `Done` check with correct content
  - `RunSiteCheckUseCase` with scraper returning `FailureCategory=Known` → check becomes `Failed` and (after Step 10) a known-failure event carries through
  - `EvaluateScheduledSitesUseCase` with `IClock` at 10:30, site window 09:00–17:00, 60-min interval, latest check at 09:00 → picked; at 09:30 → not picked
- Mock only ports (`IWebScraperPort`, `ISiteRepository`, `ISiteCheckRepository`, `IClock`, `IVpnRotationPort`). Never mock Domain classes — construct them with real data

**Why this matters for hexagonal:** This is the payoff. Use-case tests describe end-to-end business flows with no DB, no browser, no web host. Every flaky or slow test comes from mixing layers; the hexagon draws the line so the important layers test fast.

**Checkpoint:** `dotnet test` across both test projects is green.

---

## Step 10 — Replace `ChangesInterceptor` + `IEntityChangeService` with explicit domain events

**Goal:** Stop using EF as an event bus. Entities raise domain events; use cases dispatch them after `SaveChanges`.

**Actions:**
- Create `src/Domain/Common/IDomainEvent.cs` (marker) and `AggregateRoot` base with `IReadOnlyCollection<IDomainEvent> DomainEvents` + `RaiseEvent` + `ClearEvents`. Have `Site` and `SiteCheck` inherit it
- Define event records in `src/Domain/Events/`: `SiteCheckCreatedEvent(int Id, int SiteId)`, `SiteCheckCompletedEvent(int Id, int SiteId, CheckStatus Status, string? PreviousValue, string? NewValue, string? FailureCategory)`, `SiteCreatedEvent`, `SiteUpdatedEvent`, `SiteDeletedEvent`
- Create `src/Domain/Common/IDomainEventHandler<T>` and `src/Application/Common/IDomainEventPublisher`
- `SiteCheck.MarkSucceeded(string content, string? previousValue)` raises `SiteCheckCompletedEvent` carrying both previous and new value — this moves "did content change?" from a DB query in NotifierService into a pure domain computation. The use case pre-fetches `previousValue` via `ISiteCheckRepository.GetLatestSuccessfulBefore`
- In `RunSiteCheckUseCase` (and others), after `SaveChangesAsync`, drain each aggregate's events and call `IDomainEventPublisher.PublishAsync`
- In Backend, implement `DomainEventPublisher : IDomainEventPublisher` that resolves `IEnumerable<IDomainEventHandler<T>>` from DI and awaits them
- Convert [src/Backend/Services/NotifierService.cs](src/Backend/Services/NotifierService.cs) to `IDomainEventHandler<SiteCheckCompletedEvent>` — "is this a known failure?" collapses to a `FailureCategory` check; formatting (`ToPushoverContents` / `ToDiscordEmbed`) stays in Backend since it's presentation
- Convert [src/Backend/Services/CheckQueue/SiteCheckQueueService.cs](src/Backend/Services/CheckQueue/SiteCheckQueueService.cs) to `IDomainEventHandler<SiteCheckCreatedEvent>`
- Convert [src/Backend/Services/SignalR/EntityChangesService.cs](src/Backend/Services/SignalR/EntityChangesService.cs) to handlers for the Site/SiteCheck events (emit the DTO + event-name envelope over SignalR)
- **Delete:** [src/Database/ChangesInterceptor.cs](src/Database/ChangesInterceptor.cs), [src/Database/Services/IEntityChangeService.cs](src/Database/Services/IEntityChangeService.cs), `IEntityChange`/`CreatedEntityChange`/`UpdatedEntityChange`/`DeletedEntityChange`, and the interceptor registration in `SiteCheckerDbContext`
- Update [ReinforcedTypingsConfiguration.cs](src/Backend/Generators/ReinforcedTypingsConfiguration.cs): remove the `IEntityChange*` type exports; define a new Backend-owned SignalR envelope type (or hand-write a `signalr-types.ts` file). Adjust `src/Frontend/src/app/generated/signalr-*.ts` consumers accordingly

**Why this matters for hexagonal:** **Domain events** are the domain's own vocabulary for "something meaningful happened." Today the interceptor asks EF "what rows changed?" and downstream services do type-name string matching — that's infrastructure masquerading as domain semantics. Explicit domain events make the pub/sub contract discoverable in code, and carrying previous+new values on the event replaces a DB round-trip with a pure domain computation.

**Checkpoint:** Pushover/Discord notifications still fire on meaningful changes; SignalR still broadcasts; queue auto-enqueues on create. No more EF interceptor.

---

## Step 11 — Controllers call use cases; DTOs shape the wire

**Goal:** Controllers become inbound adapters. Entities stop traveling over HTTP/SignalR.

**Actions:**
- Create Application use cases for all remaining controller actions: `ListSitesUseCase`, `GetSiteUseCase`, `UpdateSiteUseCase`, `ListSiteChecksUseCase`, `GetSiteCheckUseCase`, `CreateSiteCheckUseCase`, `DeleteSiteCheckUseCase`, `DeleteAllSiteChecksUseCase`, `GetSiteCheckScreenshotUseCase`
- Create DTOs in `src/Application/Dtos/` (e.g. `SiteDto`, `SiteCheckSummaryDto`, `SiteCheckDetailDto`). These are the wire shapes — domain entities stop leaving Application
- Expand repository ports with the needed intent-named queries (e.g. `GetAllWithLatestCheck`, `DeleteAllForSiteAsync`); keep `IQueryable` out of signatures
- Rewrite [src/Backend/Controllers/SiteController.cs](src/Backend/Controllers/SiteController.cs), [src/Backend/Controllers/SiteCheckController.cs](src/Backend/Controllers/SiteCheckController.cs), [src/Backend/Controllers/VpnController.cs](src/Backend/Controllers/VpnController.cs) to inject use cases and return DTOs
- Now that entities no longer serialize, remove the `[JsonIgnore]` workaround from Step 4 (the JSON type-info resolver modifier)
- Update [ReinforcedTypingsConfiguration.cs](src/Backend/Generators/ReinforcedTypingsConfiguration.cs) to export the DTOs. Regenerate TS, diff `src/Frontend/src/app/generated/model.ts`, update Angular consumers as needed
- Add Application.Test cases for the new use cases

**Why this matters for hexagonal:** Controllers are an **inbound adapter** translating HTTP into use-case calls; they don't know about EF or entity internals. DTOs protect Domain from wire-shape coupling — Domain can grow a field without breaking consumers, and the wire can expose aggregated views without Domain pollution.

**Checkpoint:** All HTTP endpoints return identical payloads; Angular compiles; test projects green.

---

## Step 12 — Tighten boundaries; add architecture tests; document

**Goal:** Lock in the boundaries so future changes can't regress them.

**Actions:**
- Audit `Domain.csproj`: confirm zero PackageReferences. If domain wants logging, define a tiny `IDomainLogger` port rather than pulling `Microsoft.Extensions.Logging.Abstractions` in
- Audit `Application.csproj`: references `Domain` only. `Microsoft.Extensions.Logging.Abstractions` is acceptable (it's an abstraction package, no runtime dependency on a framework)
- Confirm `Database.csproj` references `Domain` only (not `Application`); `Backend.csproj` references `Application`, `Domain`, `Database`, `Scraper`, `Utilities`; `Scraper.csproj` references `Domain`, `Utilities`
- Add an architecture test (in `Domain.Test` or a new `test/ArchitectureTest`) using `System.Reflection` or NetArchTest that asserts:
  - No type in `SiteChecker.Domain` references `Microsoft.EntityFrameworkCore`, `Microsoft.Playwright`, or ASP.NET namespaces
  - No type in `SiteChecker.Application` references `SiteChecker.Database` or `SiteChecker.Backend`
- Drop NoWarn entries that no longer fire (e.g. RT0002 once projects have real content)
- Update [README.md](README.md) architecture section: explain the four layers (Domain → Application → Adapters → Composition Root), show the dependency direction, and point new readers at `test/Domain.Test` as the best starting read
- Full manual regression: create/update a site, trigger a scheduled check, verify screenshots, Pushover/Discord notifications, SignalR updates, VPN rotation logs

**Why this matters for hexagonal:** Architecture tests make the boundaries executable — the build fails if someone imports EF Core into Domain. That's the only durable way to keep the hexagon intact as the codebase grows. The teaching recap: every infrastructure concern you moved (EF, JSON, HTTP, Playwright, Docker, SignalR, Pushover, NetCord) sits in one identifiable adapter. Every business concept (schedule-due, known-failure, content-changed, queueable-on-create) lives in Domain or Application. Onboarding now means reading ~500 lines of Domain + Application before touching a framework.

**Checkpoint:** `dotnet build && dotnet test` green across all projects; full manual regression matches pre-migration behavior.

---

## Critical files to be modified

- [src/Database/Model/Site.cs](src/Database/Model/Site.cs), [src/Database/Model/SiteCheck.cs](src/Database/Model/SiteCheck.cs), [src/Database/Model/PushoverConfig.cs](src/Database/Model/PushoverConfig.cs), [src/Database/Model/DiscordConfig.cs](src/Database/Model/DiscordConfig.cs) — moved to Domain
- [src/Database/SiteCheckerDbContext.cs](src/Database/SiteCheckerDbContext.cs) — Fluent API replaces attributes; interceptor removed
- [src/Database/ChangesInterceptor.cs](src/Database/ChangesInterceptor.cs), [src/Database/Services/IEntityChangeService.cs](src/Database/Services/IEntityChangeService.cs) — deleted
- [src/Scraper/ScraperService.cs](src/Scraper/ScraperService.cs), [src/Scraper/ScrapeResult.cs](src/Scraper/ScrapeResult.cs), [src/Scraper/ScrapeRequest.cs](src/Scraper/ScrapeRequest.cs) — domain types extracted; service implements `IWebScraperPort`
- [src/Backend/Services/CheckQueue/SiteCheckProcessor.cs](src/Backend/Services/CheckQueue/SiteCheckProcessor.cs), [src/Backend/Services/CheckQueue/SiteCheckTimer.cs](src/Backend/Services/CheckQueue/SiteCheckTimer.cs) — thin shells over use cases
- [src/Backend/Services/NotifierService.cs](src/Backend/Services/NotifierService.cs) — becomes a domain event handler
- [src/Backend/Services/SignalR/EntityChangesService.cs](src/Backend/Services/SignalR/EntityChangesService.cs) — becomes domain event handlers
- [src/Backend/Services/VPN/PiaService.cs](src/Backend/Services/VPN/PiaService.cs) — implements `IVpnRotationPort`
- [src/Backend/Extensions/SiteCheckExtensions.cs](src/Backend/Extensions/SiteCheckExtensions.cs) — domain logic removed; only presentation (`ToPushoverContents` / `ToDiscordEmbed`) remains
- [src/Backend/Controllers/SiteController.cs](src/Backend/Controllers/SiteController.cs), [src/Backend/Controllers/SiteCheckController.cs](src/Backend/Controllers/SiteCheckController.cs), [src/Backend/Controllers/VpnController.cs](src/Backend/Controllers/VpnController.cs) — inject use cases, return DTOs
- [src/Backend/Program.cs](src/Backend/Program.cs) — DI wiring for ports, use cases, event dispatcher
- [src/Backend/Generators/ReinforcedTypingsConfiguration.cs](src/Backend/Generators/ReinforcedTypingsConfiguration.cs) — updated type and DTO exports
- [SiteChecker.slnx](SiteChecker.slnx) — add Domain, Application, Domain.Test, Application.Test; remove stale test entries

## Existing utilities to reuse

- `ControllerExtensions.OkOrNotFound` (in Backend) — continues to work against DTOs; don't replace it
- `SiteCheckScreenshot` byte[] storage pattern — keep the current two-query approach in controllers
- `IServiceScopeFactory.CreateAsyncScope()` pattern inside `BackgroundService`s — already present; retain
- Central `Directory.Packages.props` — add new `PackageReference` entries here (never pin versions in csprojs)
- `MSTest.Sdk 4.1.0` from `global.json msbuild-sdks` — test csprojs use `Sdk="MSTest.Sdk"` with no version attribute

## Verification

After each step:
1. `dotnet build` at repo root — must succeed with `TreatWarningsAsErrors=true`
2. `dotnet test` — Domain.Test and Application.Test must pass (from Step 7 / Step 9 onward)
3. Run the backend (`dotnet run --project src/Backend`) and the frontend (`npm start` in `src/Frontend`)
4. Smoke test in browser:
   - Load the site list (SignalR connects, list renders)
   - Create a new site check — status transitions Created → Queued → Checking → Done/Failed in real time via SignalR
   - Trigger a scheduled check by setting a site window that's currently active
   - Confirm Pushover/Discord notifications fire only on content changes or unknown failures (check logs if creds aren't wired in dev)
   - Confirm VPN rotation logs appear at the configured interval

After Step 10, specifically verify there is no remaining reference to `IEntityChangeService`, `ChangesInterceptor`, or `IEntityChange` in the codebase (`grep -r` across `src/` should return zero hits).

After Step 12, run the architecture tests — they are the long-term guardrail that prevents regressions.
