# Hexagonal Architecture Migration Plan

## Context

The site-checker app monitors websites by scraping them on a schedule, storing results, and sending notifications via Pushover/Discord. The current architecture has domain models coupled to EF Core (`src/Database/Model/`), business logic scattered across Backend services, and no clear boundary between domain and infrastructure. This migration restructures the codebase into hexagonal architecture (ports & adapters) so domain logic is isolated, testable, and framework-independent.

## Architecture: Three Rings

```
Domain  (zero dependencies — entities, value objects, ports, events, exceptions)
  ^
  |
Application  (depends only on Domain — use cases that orchestrate domain + ports)
  ^
  |
  +-- Database   (persistence adapter — implements repository ports)
  +-- Scraper    (scraping adapter — implements scraping port)
  +-- Backend    (web adapter + composition root — controllers, SignalR, notifiers, VPN, DI wiring)
```

**Domain** — *what* the business rules are: entities, value objects, enums, port interfaces, domain events, domain exceptions.

**Application** — *how* use cases are orchestrated: each use case is a plain C# class that coordinates domain objects and ports to fulfill a request. No framework dependencies — testable with just mocks.

**Infrastructure** (Database, Scraper, Backend) — *with what* technology: EF Core, Playwright, ASP.NET, Pushover, Discord, Docker/VPN.

---

## Step 1: Create Domain and Application Projects

**Goal:** Establish the two inner-ring projects.

**Create:**
- `src/Domain/Domain.csproj` — net10.0, zero NuGet/project references
- `src/Application/Application.csproj` — net10.0, references only Domain
- Update `SiteChecker.slnx` to include both

**Verify:** `dotnet build` succeeds, `dotnet test` passes.

---

## Step 2: Extract Domain Entities

**Goal:** Move models into Domain as clean types without EF Core attributes or serialization concerns.

**Create in `src/Domain/`:**
- `Entities/IEntityWithId.cs`
- `Entities/Site.cs` — no `[Index]`, no `SiteChecks` navigation collection, no `SiteUpdate` base class
- `Entities/SiteCheck.cs` — no `[JsonIgnore]`, no `[NotMapped]`, no `Site`/`Screenshot` navigation properties
- `Entities/SiteCheckScreenshot.cs` — no `[Index]`, no `SiteCheck` navigation property
- `ValueObjects/SiteSchedule.cs`, `PushoverConfig.cs`, `DiscordConfig.cs`
- `Enums/CheckStatus.cs`, `PushoverPriority.cs`
- `DTOs/SiteUpdate.cs` — standalone DTO, not a base class
- `DTOs/PagedResponse.cs`

**Modify:**
- `src/Database/Database.csproj` — add reference to Domain
- `src/Database/SiteCheckerDbContext.cs` — use Domain entities; replace `[Index]` with Fluent API in `OnModelCreating`; configure navigation properties and JSON serialization here
- Delete original model files from `src/Database/Model/` (except EF-specific configs if needed)
- `src/Backend/Backend.csproj` — add reference to Domain and Application
- Update `using` statements across Backend to reference `SiteChecker.Domain.Entities`

**Key decisions:**
- Navigation properties (Site.SiteChecks, SiteCheck.Site, etc.) are **not** on domain entities. EF Core configures relationships via Fluent API.
- `MetadataLocal` (the `[NotMapped]` dictionary for smuggling IScrapeResult between layers) is removed from the domain entity. Scrape results will be passed explicitly.
- `SiteUpdate` becomes a standalone DTO. `Site.Update(SiteUpdate)` stays as a domain method.

**Verify:** `dotnet build`, `dotnet test`, app runs, CRUD works.

---

## Step 3: Define Domain Ports (Interfaces)

**Goal:** Define what the domain needs from the outside world (driven ports).

**Create in `src/Domain/Ports/`:**
- `ISiteRepository.cs` — GetById, GetAll, GetAllWithLatestCheck, Update, Add, Remove
- `ISiteCheckRepository.cs` — CRUD, GetPaged, GetPreviousSuccessful, AddScreenshot, GetScreenshot
- `IScrapingService.cs` — `Task<IScrapeResult> ScrapeAsync(ScrapeRequest, CancellationToken)`
- `INotificationService.cs` — `Task NotifyAsync(SiteCheck, Site, SiteCheckScreenshot?, CancellationToken)`
- `IVpnService.cs` — GetCurrentLocation, ChangeLocation, GetAllLocations
- `ISiteCheckQueue.cs` — QueueCheck, DequeueCheck
- `IDomainEventDispatcher.cs` + `IDomainEventHandler<T>`

**Create in `src/Domain/`:**
- `ValueObjects/VpnLocation.cs` — simple domain record (replaces infrastructure `PiaLocation`)
- `Events/IDomainEvent.cs` — marker interface
- `Events/EntityCreatedEvent.cs`, `EntityUpdatedEvent.cs`, `EntityDeletedEvent.cs`

**Verify:** `dotnet build` (interfaces only, nothing implements them yet).

---

## Step 4: Extract Scraping Types to Domain

**Goal:** Move scrape result types and exception hierarchy to Domain so they're available to Application use cases.

**Move to `src/Domain/`:**
- `Exceptions/ScraperException.cs` (full hierarchy: Unexpected, Known, AccessDenied, BlankPage)
- `ScrapeResult.cs` (IScrapeResult, SuccessScrapeResult, FailureScrapeResult + extensions)

**Modify:**
- `src/Scraper/Scraper.csproj` — add reference to Domain
- `ScraperService.cs` — implement `IScrapingService` from Domain
- `ScraperBase.cs` — update imports for moved exception/result types
- `IScraper` interface stays internal to Scraper project (takes Playwright `IPage`)
- `BrowserType` enum stays in Scraper (infrastructure detail)
- `ScrapeRequest` stays in Scraper; Domain's `IScrapingService` takes domain-level parameters
- Update test imports in `test/Scraper.Test/`

**Verify:** `dotnet build`, `dotnet test`, trigger a check and confirm scraping works.

---

## Step 5: Create Application Use Cases

**Goal:** Extract business orchestration logic from Backend services into Application use cases. Each use case is a plain class depending only on domain ports.

**Create `src/Application/UseCases/`:**

### PerformSiteCheckUseCase
Extracted from `SiteCheckProcessor.PerformCheckAsync()`:
- Load SiteCheck from `ISiteCheckRepository`
- Set status to `Checking`, get VPN location via `IVpnService`
- Save interim state (for SignalR progress)
- Call `IScrapingService.ScrapeAsync()`
- Update SiteCheck with result (`siteCheck.CompleteWithResult()`)
- Save screenshot if present
- Dispatch domain events

### ScheduleSiteChecksUseCase
Extracted from `SiteCheckTimer.CheckForQueableSitesAsync()`:
- Get all sites with enabled schedules from `ISiteRepository`
- Evaluate which are due via `SiteSchedule.IsDueForCheck()`
- Create new SiteChecks, save via `ISiteCheckRepository`

### CreateSiteCheckUseCase
Extracted from `SiteCheckController.CreateSiteCheck()`:
- Validate site exists
- Create SiteCheck entity
- Save via `ISiteCheckRepository`
- Returns created check

### NotifyCheckCompletedUseCase
Extracted from `NotifierService.OnEntityUpdated()` decision logic:
- Was it an incomplete→complete transition?
- If Done: was content different from previous check? (via `ISiteCheckRepository.GetPreviousSuccessful`)
- If Failed: is it a known failure? (via `siteCheck.IsKnownFailure`)
- If should notify: call `INotificationService.NotifyAsync()`

### ManageSitesUseCase
Extracted from `SiteController` and `SiteCheckController` CRUD:
- Get, Update, Delete operations through repository ports

**Modify:**
- `src/Application/Application.csproj` — references only Domain (already done in Step 1)
- `src/Backend/Backend.csproj` — references Application

**Verify:** `dotnet build` (use cases are created but not wired in yet — that happens in subsequent steps).

---

## Step 6: Implement Repository Adapters

**Goal:** Database project implements repository ports.

**Create:**
- `src/Database/Repositories/SiteRepository.cs` — implements `ISiteRepository`
- `src/Database/Repositories/SiteCheckRepository.cs` — implements `ISiteCheckRepository`

**Modify:**
- `Program.cs` — register repository implementations in DI

**Note:** ChangesInterceptor still works during this step. We replace it in Step 8.

**Verify:** `dotnet build`, repositories are registered and injectable.

---

## Step 7: Wire Controllers and Services to Use Cases

**Goal:** Controllers delegate to Application use cases instead of using DbContext directly. Background services call use cases instead of containing orchestration logic.

**Modify controllers:**
- `SiteController.cs` — inject `ManageSitesUseCase` instead of `SiteCheckerDbContext`
- `SiteCheckController.cs` — inject `CreateSiteCheckUseCase` + `ManageSitesUseCase` instead of `SiteCheckerDbContext`
- `VpnController.cs` — inject `IVpnService` instead of `PiaService`

**Modify background services:**
- `SiteCheckProcessor.cs` — call `PerformSiteCheckUseCase.ExecuteAsync()` instead of containing the orchestration logic inline
- `SiteCheckTimer.cs` — call `ScheduleSiteChecksUseCase.ExecuteAsync()` instead of containing scheduling logic inline

**Key insight:** The background services (`SiteCheckProcessor`, `SiteCheckTimer`) become thin infrastructure shells. They handle the ASP.NET `BackgroundService` lifecycle (loops, timers, scopes) but delegate all business decisions to use cases.

**Modify:**
- `Program.cs` — register use cases in DI (transient or scoped)

**Verify:** Full end-to-end test — create check, watch it process, verify SignalR + notifications still work (still via ChangesInterceptor at this point).

---

## Step 8: Replace ChangesInterceptor with Domain Events

**Goal:** Replace implicit EF Core interceptor events with explicit domain events dispatched by repositories.

**Create in `src/Backend/Services/DomainEvents/`:**
- `DomainEventDispatcher.cs` — resolves `IDomainEventHandler<T>` from DI, calls them
- `SignalREventHandler.cs` — replaces `EntityChangesService` (broadcasts via SignalR)
- `QueueEventHandler.cs` — replaces queue auto-enqueue logic (handles `EntityCreatedEvent` for SiteCheck)
- `NotificationEventHandler.cs` — calls `NotifyCheckCompletedUseCase` when SiteCheck updated

**Modify:**
- `SiteCheckRepository.cs` / `SiteRepository.cs` — inject `IDomainEventDispatcher`, dispatch events after `SaveChangesAsync`
- `SiteCheckerDbContext.cs` — remove `ChangesInterceptor`, remove `IEntityChangeService` dependency
- `SiteCheckQueueService.cs` — no longer implements `IEntityChangeService`, just `ISiteCheckQueue`
- `Program.cs` — remove `IEntityChangeService` registrations, add dispatcher + handlers

**Delete:**
- `src/Database/ChangesInterceptor.cs`
- `src/Database/Services/IEntityChangeService.cs`
- `src/Backend/Services/SignalR/EntityChangesService.cs`

**Verify:** Thorough end-to-end — create check, confirm it queues, confirm SignalR broadcasts, confirm notifications fire.

---

## Step 9: Wire Up VPN and Notification Adapters

**Goal:** Infrastructure services implement domain ports.

**Modify:**
- `PiaService.cs` — implement `IVpnService`, map `PiaLocation` ↔ `VpnLocation`
- `NotifierService.cs` — implement `INotificationService` (wraps Pushover + Discord)
- `SiteCheckProcessor.cs` — already uses `IVpnService` (from Step 7)
- `DataSeeder.cs` — inject `ISiteRepository`

**Verify:** VPN operations work, notifications fire.

---

## Step 10: Move Business Logic to Domain Entities

**Goal:** Ensure business rules live on domain entities, not scattered in extension methods.

**Move to domain entities:**
- `SiteCheck.CompleteWithResult(IScrapeResult)` — from `SiteCheckExtensions.Update(IScrapeResult)`
- `SiteCheck.FailWithException(Exception)` — from `SiteCheck.Update(Exception)`
- `SiteCheck.IsKnownFailure` property — from `SiteCheckExtensions.IsKnownExceptionFailure()`
- `SiteSchedule.IsDueForCheck(DateTime lastCheckTime, DateTime now)` — from `SiteCheckTimer`

**Modify:**
- `PerformSiteCheckUseCase` — calls `siteCheck.CompleteWithResult()`
- `ScheduleSiteChecksUseCase` — calls `schedule.IsDueForCheck()`
- `NotifyCheckCompletedUseCase` — uses `siteCheck.IsKnownFailure`
- `SiteCheckExtensions.cs` — keep only adapter formatting (ToPushoverContents, ToDiscordEmbed)

**Verify:** All tests pass, processing + scheduling + notifications work.

---

## Step 11: Add Domain and Application Tests

**Goal:** Prove both inner rings are testable in isolation.

**Create:**
- `test/Domain.Test/Domain.Test.csproj` — MSTest SDK, references only Domain
- `test/Domain.Test/Entities/SiteCheckTests.cs` — status transitions, completion, known failure detection
- `test/Domain.Test/Entities/SiteTests.cs` — update application
- `test/Domain.Test/ValueObjects/SiteScheduleTests.cs` — schedule evaluation

- `test/Application.Test/Application.Test.csproj` — MSTest SDK, references Application + Domain + NSubstitute
- `test/Application.Test/UseCases/PerformSiteCheckUseCaseTests.cs` — mock all ports, verify orchestration
- `test/Application.Test/UseCases/ScheduleSiteChecksUseCaseTests.cs` — mock repos, verify scheduling decisions
- `test/Application.Test/UseCases/NotifyCheckCompletedUseCaseTests.cs` — mock repos + notification service, verify notification rules

- Update `SiteChecker.slnx`

**Verify:** `dotnet test` — domain tests need zero mocks, application tests mock only ports (no DB, no Playwright, no HTTP).

---

## Step 12: Final Cleanup

**Goal:** Verify architectural boundaries and clean up.

- Confirm dependency directions: Domain → nothing; Application → Domain; Database → Domain; Scraper → Domain; Backend → all
- Remove stale `using SiteChecker.Database.Model` references
- Update `ReinforcedTypingsConfiguration.cs` for Domain type references
- Verify EF Core migrations: `dotnet ef migrations add VerifyHexArch` (should be empty)
- Remove `SiteUpdate` base class inheritance

**Verify:** Full `dotnet build`, `dotnet test`, end-to-end manual test.

---

## Key Risks

| Risk                                                             | Mitigation                                                                   |
| ---------------------------------------------------------------- | ---------------------------------------------------------------------------- |
| EF Core migration snapshot breaks after moving entity namespaces | Keep table/column names identical; run verification migration                |
| Silent event loss after removing ChangesInterceptor              | All SaveChanges go through repositories that dispatch events                 |
| Reinforced.Typings breaks after type moves                       | Update generator config in Step 12                                           |
| Navigation property removal breaks LINQ queries                  | Fluent API for relationships; refactor queries in repository implementations |

## Critical Files

- `src/Database/Model/Site.cs` — primary domain model to extract
- `src/Database/Model/SiteCheck.cs` — second key model
- `src/Database/ChangesInterceptor.cs` — event mechanism to replace
- `src/Database/SiteCheckerDbContext.cs` — must be simplified
- `src/Backend/Services/CheckQueue/SiteCheckProcessor.cs` — touches most boundaries
- `src/Backend/Extensions/SiteCheckExtensions.cs` — contains domain logic to move
- `src/Backend/Program.cs` — composition root, all DI wiring
- `src/Scraper/ScraperService.cs` — scraping adapter boundary
- `src/Scraper/ScrapeResult.cs` — domain types to move
- `src/Scraper/Exceptions/ScraperException.cs` — domain exceptions to move
