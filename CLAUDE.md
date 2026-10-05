# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

SiteChecker is a self-hosted ASP.NET Core (.NET 10 / C# 14) + Angular 21 app that periodically scrapes websites with Playwright (via Browserless containers, optionally routed through a PIA WireGuard VPN), records each result, and notifies via Pushover or Discord when content changes or a check fails.

Domain vocabulary lives in `CONTEXT.md`; use those terms (Site, Site Check, Queued, Known Failure, ...). Architectural decisions live in `docs/adr/` — don't re-litigate them without reason.

## Commands

```bash
# Build everything (what CI does). Warnings are errors, including NuGet vulnerability audits.
dotnet restore --locked-mode
dotnet build --configuration Release --no-restore

# Backend tests (MSTest v4 on Microsoft.Testing.Platform)
dotnet test
dotnet test --project test/Backend.Test/Backend.Test.csproj
dotnet test --project test/Backend.Test/Backend.Test.csproj --filter "FullyQualifiedName~RunNext"

# Run the backend (needs Browserless containers or local Playwright; see docs/local-development.md)
cd src/Backend && dotnet run

# Frontend (src/Frontend)
npm install
npm start                       # ng serve over HTTPS using the ASP.NET dev cert
npm run lint
npx ng test --watch=false
npx ng test --watch=false --include src/app/components/site-list/site-list.spec.ts

# EF Core migrations (run from src/Database; dotnet-ef is in the local tool manifest)
dotnet tool restore
dotnet ef migrations add <Name>

# Full stack
cp example.env .env && docker compose up
```

Build notes:
- Building `src/Backend` also builds the Angular app (an `AfterTargets="Build"` step in `Backend.csproj`) and regenerates `src/Frontend/src/app/generated/model.ts`. Node/npm must be available.
- Package versions are central in `Directory.Packages.props`, and every project has a `packages.lock.json`. CI restores with `--locked-mode`, so commit lock file changes along with version bumps.
- The frontend specs are untouched Angular scaffolding and currently fail (missing providers). CI only runs the backend tests.

## Architecture

Projects: `src/Backend` (ASP.NET Core host: controllers, background services, VPN, notifiers, code generators), `src/Database` (EF Core models, `SiteCheckerDbContext`, migrations, change interceptor), `src/Scraper` (Playwright scraping library), `src/Utilities`, `src/Frontend` (Angular SPA), and `src/LocalPlaywright` (a local Playwright server for development without Docker). Tests live in `test/`.

### Site Check lifecycle
- `Services/CheckQueue/SiteCheckRunner` owns every Site Check status change: deciding which Sites are due by Schedule, accepting requests (`RequestCheckAsync` returns the Site's open check if one exists), claiming and running checks (Queued → Checking → Done/Failed), and re-queuing orphaned `Checking` checks (after a restart or a failed save) each time it looks for work.
- **The database is the queue** (ADR 0001): pending work is every Site Check with status `Queued`. The in-memory channel only wakes the runner and holds no state.
- `SiteCheckTimer` and `SiteCheckQueueProcessor` are thin `BackgroundService` loops that call the runner. Keep logic out of them, and create or run Site Checks through the runner, never by writing `SiteCheck` rows directly.
- Checks run one at a time, because rotating the VPN Location restarts the shared Browserless VPN container. Orphan recovery depends on this: any `Checking` row found between checks is assumed abandoned.

### Save interceptor fan-out
`Database/ChangesInterceptor` runs on every `SaveChanges` and passes the created, updated and deleted entities to each registered `IEntityChangeService`:
- `EntityChangesService` broadcasts them to SignalR clients. **Never push SignalR entity events manually after a save.** Note that `ExecuteUpdate`/`ExecuteDelete` skip the interceptor, so they broadcast nothing.
- `NotifierService` reacts to a Site Check becoming Done or Failed. It notifies only when content differs from the previous Done check, or on a failure that isn't a Known Failure. It calls `PushoverService` and `DiscordService` directly; there is no notifier interface yet. Keep it the single notification dispatch path.

### Scrapers and Sites
- A scraper is a class deriving `ScraperBase` in `src/Scraper/Scrapers/` (override `Id`, `Url`, `DoScrapeAsync`), registered with `AddScraper<T>()` in `AddScraperServices()` (`ScraperService.cs`). `Site.ScraperId` selects it at runtime.
- **Sites are defined in code.** `Backend/Services/DataSeeder.cs` seeds one Site per scraper at startup and **deletes any Site whose `ScraperId` isn't in its list**, so a new scraper needs a seeded Site there. (The scraper example in `README.md` predates the current `ScraperBase` API.)
- `ScraperBase` turns exceptions into `FailureScrapeResult`s, takes screenshots on failure (or always, per Site), and writes HTML and exception dumps to `site-checker/logs`. `KnownScraperException` subclasses (access denied, blank page) mark Known Failures.
- `ScraperService.GetBrowserType` picks the browser: `USE_LOCAL_BROWSER` → local Playwright, otherwise Browserless or Browserless VPN, depending on `Site.UseVpn` and which URLs are configured.

### Type bridge (C# → TypeScript)
- `Backend/Generators/ReinforcedTypingsConfiguration.cs` generates `src/Frontend/src/app/generated/model.ts` at build time. It contains Zod schemas plus inferred TypeScript types for models and enums, an injectable Angular client class per API controller (every `ControllerBase` in `SiteChecker.Backend.Controllers`, so the frontend calls e.g. `SiteCheckController.createSiteCheck()`), and `SignalRConstants`.
- **Never hand-edit `model.ts`.** A new property on an exported model appears after a rebuild, but a **new model or enum type must be added to the lists in `ReinforcedTypingsConfiguration`**. XML doc comments on controller actions are copied into the generated client.
- Enums are serialized as strings (`JsonStringEnumConverter`). `CheckStatus` values are pinned because they're stored as integers.

### Frontend
- Root-provided NgRx Signals stores in `src/app/services/` are built on `withCrudEntities` (`base.store.ts`), which subscribes to the SignalR entity events and upserts or removes entities whose payload passes the model's Zod schema. Stores update reactively, so **don't poll**.
- All components are standalone (no `NgModule`). Use `inject()` instead of constructor injection, and lazy-load non-dashboard routes with `loadComponent`.
- Validate external data (SignalR payloads, API responses) with the generated Zod schemas, not hand-written type guards.

## Conventions

### Backend
- Primary constructor DI on services and controllers. Mark classes `sealed` unless they're meant to be inherited, and use `record` for immutable DTOs.
- Prefer C# 14 `extension(...)` blocks over classic static extension methods. DI registration helpers follow this pattern, e.g. `AddSiteCheckRunner()`.
- Controllers use `SiteCheckerDbContext` directly; **don't add a repository layer**. Get the cancellation token from `HttpContext.RequestAborted` through a private `CancellationToken` property (see `SiteController`), not from an action parameter, return `this.OkOrNotFound(entity)` for single lookups, and use `ToPagedResponseAsync(pageNumber, pageSize)` for paged lists.
- Nested config (`SiteSchedule`, `PushoverConfig`, `DiscordConfig`) is stored in JSON columns via `ComplexProperty(...).ToJson()` in `OnModelCreating`.
- Migrations run with `MigrateAsync()` at startup; never use `EnsureCreated`. Don't hand-edit generated migration snapshots.
- `SiteCheckerDbContext` uses the injected `DbContextOptions` when a provider is configured, and otherwise falls back to the SQLite file in `site-checker/data/` (or `/app/data` in Docker).

### Testing
- Use MSTest v4 only (no xUnit, NUnit, or Jest). The MSTest analyzers run in `Recommended` mode with warnings as errors, so use the specific asserts (`Assert.HasCount`, `Assert.ContainsSingle`, `Assert.IsEmpty`) and pass `TestContext.CancellationToken`.
- `test/Backend.Test` tests `SiteCheckRunner` only through its public methods, using `RunnerHarness`: real DI, migrated in-memory SQLite, `FakeTimeProvider`, and a fake `IScraperService`. `harness.Broadcasts` records what the save interceptor would send to clients, and `harness.SaveFaults` fails a chosen save. Extend the harness rather than mocking EF.

### Configuration
Environment variables are documented in `docs/configuration.md`. Locally, `.env` is loaded by dotenv.net at startup. VPN rotation is controlled by `VPN_CHANGE_INTERVAL` (minutes, default 15 in code), and container networking troubleshooting is in `docs/local-development.md`.

## Microsoft documentation

The `microsoft_docs_search`, `microsoft_docs_fetch` and `microsoft_code_sample_search` MCP tools return current official docs. Use them for specific questions about C#, ASP.NET Core, EF Core, MSTest, Microsoft.Extensions, NuGet or the `dotnet` CLI, where they may be newer than training data.
