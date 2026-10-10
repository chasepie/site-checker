# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

SiteChecker is a self-hosted ASP.NET Core (.NET 10 / C# 14) + Angular 21 app that periodically scrapes websites with Playwright (via Browserless containers, optionally routed through a PIA WireGuard VPN), records each result, and notifies via Pushover or Discord when content changes, a Site starts failing, or it recovers.

Domain vocabulary lives in `GLOSSARY.md`; use those terms (Site, Site Check, Queued, Known Failure, ...). Architectural decisions live in `docs/adr/` — don't re-litigate them without reason.

## Commands

```bash
# Build everything (what CI does). Warnings are errors, including NuGet vulnerability audits.
dotnet restore --locked-mode
dotnet build --configuration Release --no-restore

# Backend tests (MSTest v4 on Microsoft.Testing.Platform)
dotnet test
dotnet test --project test/Backend.IntegrationTests/Backend.IntegrationTests.csproj
dotnet test --project test/Backend.IntegrationTests/Backend.IntegrationTests.csproj --filter "FullyQualifiedName~RunNext"

# Run the backend (needs Browserless containers or local Playwright; see docs/local-development.md).
# Without SCRAPE_WORKER_URL it scrapes in-process; to use the worker, run it too and set
# SCRAPE_WORKER_URL=http://localhost:5280.
cd src/Backend && dotnet run
dotnet run --project src/ScrapeWorker

# Frontend (src/Frontend)
npm install
npm start                       # ng serve over HTTPS using the ASP.NET dev cert
npm run lint
npx ng test --watch=false
npx ng test --watch=false --include src/app/components/site-list/site-list.spec.ts

# The script contract package (published by .github/workflows/release.yml on a v* tag)
dotnet pack src/Scripting -c Release -p:Version=0.1.0

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
- `samples/DemoScrapers` builds with the solution, so CI compiles the demo scripts against the contract. The Backend embeds those same files for `DemoDataSeeder`.

## Architecture

Projects: `src/Backend` (ASP.NET Core host: controllers, background services, VPN, notifiers, code generators), `src/Database` (EF Core models, `SiteCheckerDbContext`, migrations, change interceptor), `src/Scraper` (Playwright scraping library), `src/ScrapeWorker` (the Scrape Worker: a minimal ASP.NET host that runs the scrape pipeline for the app), `src/Utilities`, `src/Frontend` (Angular SPA), and `src/LocalPlaywright` (a local Playwright server for development without Docker). Tests live in `test/`.

### Site Check lifecycle
- `Services/CheckQueue/SiteCheckRunner` owns every Site Check status change: deciding which Sites are due by Schedule, accepting requests (`RequestCheckAsync` returns the Site's open check if one exists), claiming and running checks (Queued → Checking → Succeeded/Failed), and re-queuing orphaned `Checking` checks (after a restart or a failed save) each time it looks for work.
- **The database is the queue** (ADR 0001): pending work is every Site Check with status `Queued`. The in-memory channel only wakes the runner and holds no state.
- `SiteCheckTimer` and `SiteCheckQueueProcessor` are thin `BackgroundService` loops that call the runner. Keep logic out of them, and create or run Site Checks through the runner, never by writing `SiteCheck` rows directly.
- Checks run one at a time, because rotating the VPN Location restarts the shared Browserless VPN container. Orphan recovery depends on this: any `Checking` row found between checks is assumed abandoned.
- The runner's **scrape lock** covers everything that uses the shared browser and VPN containers: resolving and rotating the VPN Location, scraping, Test Runs (`RunTestAsync`), and manual location changes (`ChangeVpnLocationAsync`, which `VpnController` calls). Don't call `PiaService.ChangeLocationAsync` directly.
- **Requested Actions** of a Known Failure are carried out by the runner when it records the outcome, and only the ones carried out are recorded in `SiteCheck.RequestedActions`. Retry queues a new check in the same save, once per Failing Run. Change VPN Location (VPN-routed checks only) excludes the failed location in `PiaService` and rotates before the next VPN-routed check if that location is still current.

### Save interceptor and SignalR
`Database/ChangesInterceptor` runs on every `SaveChanges` and passes the created, updated and deleted entities to each registered `IEntityChangeService`. Today that's only `EntityChangesService`, which broadcasts them to SignalR clients. **Never push SignalR entity events manually after a save.** Note that `ExecuteUpdate`/`ExecuteDelete` skip the interceptor, so they broadcast nothing.

### Notifications
- `SiteCheckRunner` calls `NotifierService.NotifyAsync(siteCheckId)` after saving each outcome (ADR 0002); notifications do **not** hang off the save interceptor. `NotifierService` is the single dispatch path and never throws for a notification problem.
- The policy (terms in `GLOSSARY.md`): a Succeeded check notifies **Updated** when its content differs from the Baseline (a Site's first Succeeded check only sets the Baseline). A **Failing Run** is reported once, on its first Unexpected Failure or when its Known Failures reach the Site's `KnownFailuresThreshold`. A Succeeded check ending a reported run notifies **Recovered** (or **Recovered and Updated**). A run counts as reported only once its Failing notification reached a channel (`SiteCheck.ReportedAt`); until then each failure retries. History is ordered by completion (`CompletedDate`, then `Id`). A Baseline Reset never notifies but ends a Failing Run.
- Channels implement `Notifiers/INotificationChannel`: the notifier decides *whether* and which of the Site's settings apply (`Notification.Settings`: Recoveries use failure settings, Recovered and Updated falls back to success), and each channel decides *how*. `SendAsync` returns whether it delivered (`false` when the Site has it off) and must throw on delivery failure. `PushoverChannel` and `DiscordChannel` are registered only when configured. Pushover Emergency alerts carry `retry`/`expire`; Emergency Recoveries are sent at High.

### Scrapers and Sites
- **Sites and their Scrapers are data** (ADR 0003). `Site.Scraper` is a flat `ScraperDefinition` (a `Kind` plus one nullable payload per kind) in a JSON column. Stage 1 has only Script Scrapers: an uploaded C# file whose metadata is in `Scraper.Script` and whose source is in its own `SiteScript` row. `SiteScript` deliberately isn't an `IEntityWithId`, so the save interceptor never broadcasts it.
- **One pipeline runs every Scraper** (`src/Scraper/ScraperService.cs`). It opens the browser (`Browsers/BrowserProvider`; `Browsers/BrowserSelector` picks the `BrowserType` from config), navigates to `Site.Url`, and passes the navigation result to the `IScrapeExecutor` for the request's `ScraperSpec` type, under the Site's timeout (`ScrapeTimeouts`). Navigation errors reach the Scraper rather than failing the scrape (ADR 0005). The pipeline also takes the screenshot (on failure, or always per Site) and, for a Site Check's Unexpected Failure, the page's HTML, under a separate 10 s budget, and converts every exception into an Unexpected Failure. `ScrapeResult` is Succeeded, a Known Failure (with Requested Actions), or an Unexpected Failure.
- **The pipeline writes nothing; the result carries everything**, so it can cross a process boundary (`ScrapeJson` is the wire format): the exception as strings (`ExceptionType`, `ExceptionDetail`), `PageHtml`, and the Scraper's own log entries (`ScraperLog`, capped), recorded through `ExecutorContext.Log`. The runner writes the failure dumps (`FailureArtifacts`) from the result. Evict a Site's compiled script through `IScraperService.EvictScriptAsync`, not `ScriptCache`.
- **Scripts run in the Scrape Worker** (ADR 0006). With `SCRAPE_WORKER_URL` set (Docker Compose sets it), the app's `IScraperService` is `Services/Scraping/RemoteScraperService`, which waits for the worker's `/healthz`, posts the `ScrapeRequest` to `/scrape`, and turns every failure to reach it into an Unexpected Failure. It re-logs the result's Scraper log under `SiteChecker.Script`. Without it, the app runs `ScraperService` in-process (local development). The app keeps choosing the browser (`BrowserSelector`) and validating scripts (`IScriptCompiler.Validate` compiles without running). In the worker, `ExitingAbandonedRunMonitor` stops the process when a run abandoned at its timeout is still going 15 s later, so Docker restarts it; until then `/scrape` answers 503 and `/healthz` is unhealthy.
- **Scripts** compile against the `SiteChecker.Scripting` contract (`src/Scripting`: `IScript`, `ScriptContext`, `ScriptOutcome`, `RequestedAction`) with `ScriptCompiler` (Roslyn, fixed global usings, into a collectible load context), cached per Site by source hash in `ScriptCache`. The runtime compiler is the source of truth: `samples/DemoScrapers` mirrors its settings, and `Scraper.IntegrationTests` compiles every demo script with it.
- `DemoDataSeeder` seeds the two demo Sites (scripts embedded from `samples/DemoScrapers`) only into a database with no Sites, when `SEED_DEMO_DATA` is on (default: Development only). It never updates or deletes.

### Type bridge (C# → TypeScript)
- `Backend/Generators/ReinforcedTypingsConfiguration.cs` generates `src/Frontend/src/app/generated/model.ts` at build time. It contains Zod schemas plus inferred TypeScript types for models and enums, an injectable Angular client class per API controller (every `ControllerBase` in `SiteChecker.Backend.Controllers`, so the frontend calls e.g. `SiteCheckController.createSiteCheck()`), and `SignalRConstants`.
- **Never hand-edit `model.ts`.** A new property on an exported model appears after a rebuild, but a **new model or enum type must be added to the lists in `ReinforcedTypingsConfiguration`**. XML doc comments on controller actions are copied into the generated client. Responses are validated with the return type's Zod schema, except for void actions (`ActionResult`), whose empty body isn't parsed.
- Enums are serialized as strings (`JsonStringEnumConverter`). `CheckStatus` values are pinned because they're stored as integers.

### Frontend
- Root-provided NgRx Signals stores in `src/app/services/` are built on `withCrudEntities` (`base.store.ts`), which subscribes to the SignalR entity events and upserts or removes entities whose payload passes the model's Zod schema. Stores update reactively, so **don't poll**.
- All components are standalone (no `NgModule`). Use `inject()` instead of constructor injection, and lazy-load non-dashboard routes with `loadComponent`.
- Validate external data (SignalR payloads, API responses) with the generated Zod schemas, not hand-written type guards.
- `AuthService` owns the session and the SignalR connection's lifetime: it reads the session at startup, connects the hub only when logged in, and on logout stops it and reloads at `/login`. Every route but `/login` sits under `authGuard`, and `sessionInterceptor` sends a 401 back to `/login`. Don't start the hub anywhere else.

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
- Test projects are split by category, and the name says which: `*.UnitTests` (fast, no database or Roslyn) and `*.IntegrationTests` (real SQLite, real compilation). Both run in CI.
- `test/Backend.IntegrationTests` tests `SiteCheckRunner` only through its public methods, using `RunnerHarness`: real DI, migrated in-memory SQLite, `FakeTimeProvider`, and a fake `IScraperService`. `harness.Broadcasts` records what the save interceptor would send to clients, `harness.SaveFaults` fails a chosen save, and `harness.Notifications` records every notification sent (`harness.OtherChannel` can be made to fail). Notification behavior is tested through the runner in `NotificationTests`; `PushoverChannelTests` (in `test/Backend.UnitTests`) cover the Pushover adapter against a fake HTTP handler. Extend the harness rather than mocking EF. `harness.Vpn` fakes the VPN containers under the real `PiaService`.
- API tests use `SiteApiFactory` (`WebApplicationFactory<Program>`): the real app over in-memory SQLite, with demo data off, the app's background services removed, and a fake `IScraperService`. Its password is `SiteApiFactory.Password`: `CreateLoggedInClientAsync` logs in for real and sends the antiforgery header, and `CreateClient()` is logged out. Its cookie keys go to a temporary directory, never `site-checker/data/keys`. A request carrying `SiteApiFactory.RemoteAddressHeader` comes from that client address, since TestServer gives none.
- `test/ScrapeWorker.IntegrationTests` covers the worker's API through `WebApplicationFactory<SiteChecker.ScrapeWorker.Program>` (qualify it: a bare `Program` resolves to `Microsoft.Playwright.Program`) with a substituted browser and real compilation. `RemoteScraperService` is tested against a fake worker in `Backend.UnitTests` and against the real one in `Backend.IntegrationTests`.

### Configuration
Environment variables are documented in `docs/configuration.md`. Locally, `.env` is loaded by dotenv.net at startup. VPN rotation is controlled by `VPN_CHANGE_INTERVAL` (minutes, default 15 in code), and container networking troubleshooting is in `docs/local-development.md`.
- `SCRAPE_TIMEOUT` (seconds, default 120) is the default Site timeout. `BROWSERLESS_TIMEOUT` (ms, default 180000) is passed to Browserless as `TIMEOUT` too, and every timeout must leave 10 s under it (`ScrapeTimeouts`; startup fails otherwise). `SEED_DEMO_DATA` turns the demo Sites on or off (default: Development only).
- **Login** (ADR 0007): `ADMIN_PASSWORD` (`AdminPassword`) is the only credential; `AuthController` logs in to an HttpOnly, `SameSite=Strict` cookie lasting `SESSION_DAYS`. A fallback authorization policy puts **every endpoint behind the login by default**, so mark new public endpoints `[AllowAnonymous]` deliberately. Every controller write needs the antiforgery token (`ValidateAntiforgeryFilter`; Angular's HttpClient sends `X-XSRF-TOKEN` from the `XSRF-TOKEN` cookie that the session and login endpoints issue; the token is never read from a form field). Login attempts are limited per client and in all by `LoginThrottle`, which the login action calls (not the rate limiting middleware, so requests that can't log in don't count), the hub refuses other origins (`HubOriginCheck`: `Sec-Fetch-Site`, else the `Origin` host), and a logout closes every hub connection (`HubConnections`). `ALLOWED_HOSTS` restricts Host headers, and `TRUSTED_PROXIES` lists the reverse proxies whose `X-Forwarded-For`/`-Proto` are believed (none by default). Both `ADMIN_PASSWORD` and `ALLOWED_HOSTS` are required outside Development; without the password in Development, login is off. `SCRAPE_WORKER_URL` points the app at the Scrape Worker, and `DOCKER_HOST` at the socket proxy. `SCRAPE_WORKER_SECRET` (`ScrapeWorkerSecret`) is the bearer secret the app's named client sends and the worker requires on `/scrape` and `/scripts` (not `/healthz`); it's required on both sides outside Development, and must differ from `ADMIN_PASSWORD`.
- **Trust boundary** (ADR 0006): scripts run unsandboxed, but in the Scrape Worker, which has no secrets except the Browserless token and its own secret, no volumes, no Docker access and no route out except through the browsers. The app reaches Docker only through the socket proxy in `docker-compose.yml`, whose allowlist must cover any new Docker call. Compose passes each container only the variables it reads. Keep the app on trusted networks: the login is one shared password.

## Microsoft documentation

The `microsoft_docs_search`, `microsoft_docs_fetch` and `microsoft_code_sample_search` MCP tools return current official docs. Use them for specific questions about C#, ASP.NET Core, EF Core, MSTest, Microsoft.Extensions, NuGet or the `dotnet` CLI, where they may be newer than training data.
