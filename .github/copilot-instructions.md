# SiteChecker – Copilot Instructions

## What this project is

SiteChecker is a self-hosted ASP.NET Core + Angular application that periodically scrapes websites using Playwright-controlled headless Chromium, detects content changes, and sends notifications via Discord or Pushover. Scraping can be optionally routed through a PIA WireGuard VPN to avoid bot detection.

## Tech Stack

- **Backend**: .NET 10, ASP.NET Core, EF Core 10 with SQLite, SignalR, Playwright
- **Frontend**: Angular 21 (standalone components), NgRx Signals, Bootstrap 5, AG Grid, Zod
- **Observability**: OpenTelemetry / Grafana OTLP
- **API docs**: Scalar UI at `/scalar`, OpenAPI at `/openapi`
- **Infrastructure**: Docker Compose; Browserless Chromium containers, PIA WireGuard VPN container

## Architecture

```
Angular SPA ↔ REST + SignalR ↔ ASP.NET Core ↔ SQLite (EF Core)
                                      ↓
                          Background queue processor
                                      ↓
                    Playwright → Browserless (plain or VPN)
```

- `SiteCheckTimer` (BackgroundService) queues sites when their schedule is due.
- `SiteCheckQueueProcessor` (BackgroundService) dequeues, runs the scraper, persists results.
- `ChangesInterceptor` hooks EF Core `SaveChangesAsync` and pushes SignalR events (`CreatedEntityChange`, `UpdatedEntityChange`, `DeletedEntityChange`) to all connected clients automatically — **do not manually push SignalR events after save**.
- The Angular stores (`SiteStore`, `SiteCheckStore`, etc.) subscribe to SignalR on init via `withCrudEntities` and update themselves reactively — **do not poll from the frontend**.

## Backend Conventions

### General
- Use **primary constructor DI** on all services and controllers.
- Controllers are intentionally thin — they call `DbContext` directly (no repository layer).
- Pull `CancellationToken` from `HttpContext.RequestAborted` via the existing base property, not from the action signature.
- Return `this.OkOrNotFound(entity)` for single-entity lookups (see `ControllerExtensions`).
- Use `ToPagedResponseAsync(pageNumber, pageSize)` on `IQueryable` for all paged list endpoints.

### EF Core
- JSON columns are used for nested config (`SiteSchedule`, `PushoverConfig`, `DiscordConfig`) — configure them with `ComplexProperty.ToJson()` in `OnModelCreating`.
- Always use `MigrateAsync()` via the `IServiceScope` at startup; never call `EnsureCreated`.
- Run `dotnet ef migrations add <Name>` from `src/Database/` (the `dotnet-ef` tool is in the local tool manifest).

### C# Style
- Prefer C# 14 `extension` blocks over static extension methods where applicable (see `SiteCheckExtensions`).
- Use `record` types for immutable DTOs and value objects.
- Use `sealed` on classes that are not intended for inheritance.

### Adding a new scraper
1. Create a class in `src/Scraper/Scrapers/` implementing `ScraperBase`.
2. Register it by calling `services.AddScraper<T>()` inside `AddScraperServices()` in `src/Scraper/ScraperService.cs`.
3. The `Site.ScraperId` string selects the scraper at runtime.

### Notifications
- `NotifierService` dispatches after a check based on `Site` config. Add new notifiers by implementing the notifier interface under `src/Backend/Notifiers/`.

## Frontend Conventions

### Components
- All components are **standalone** — never add `NgModule`.
- Use `inject()` for DI inside components and services; do not use constructor injection.
- Lazy-load routes with `loadComponent` for non-dashboard views.

### State management (NgRx Signals)
- All stores use the `withCrudEntities` reusable feature for common CRUD + SignalR wiring.
- Add new stores in `src/Frontend/src/app/` following the existing store pattern.
- Don't directly mutate signal state — use store methods.

### Type safety bridge
- **TypeScript interfaces and Zod schemas are auto-generated from C# models** by `Reinforced.Typings` at build time into `src/Frontend/src/app/generated/model.ts`.
- **Do not hand-edit `model.ts`** — changes will be overwritten.
- When adding a new model property, add it to the C# class and rebuild; the TypeScript side updates automatically.
- The SignalR service uses the generated Zod schemas to validate incoming messages at runtime.

### Validation
- Use the generated Zod schemas for parsing external data (SignalR payloads, API responses where needed).
- Do not write manual type guards for models that already have Zod schemas.

## Testing

- **Backend**: `Microsoft.Testing.Platform` + **MSTest v4** (via `MSTest.Sdk` 4.1.0) is configured. Test projects live under `test/` (e.g. `test/Scraper.Test/`).
  - MSTest v4 includes newer APIs such as `Assert.ThrowsExactlyAsync`, `Assert.IsInstanceOfType<T>()`, `[TestInitialize]`/`[TestCleanup]` async support, and test-class-level parallelism — use these freely.
  - Existing tests cover `ScraperBase`, `ScraperService`, `ScrapeResult`, `ScraperException`, and `TryResult`.
  - When adding new backend tests, follow the same project structure and use MSTest — do not add NUnit or xUnit.
- **Frontend**: Vitest via Angular CLI (`ng test`). Co-locate spec files with components.
- Do not add Jest or other test runners — use the existing infrastructure for both backend and frontend.

## Environment & Docker

- Copy `example.env` to `.env` and fill in secrets before running via Docker Compose.
- `BROWSERLESS_URL` and `BROWSERLESS_URL_VPN` point to the respective Browserless containers.
- In development, the Angular SPA runs via `ng serve` with a proxy; in production it's bundled into the .NET output.
- Use the VS Code tasks defined in `.vscode/tasks.json` for common operations (build, migrations, Docker).

## What to avoid

- Do not add a repository abstraction layer — controllers use `DbContext` directly by design.
- Do not add NgModules to the Angular project.
- Do not hand-edit generated files (`model.ts`, migration snapshots generated by EF).
- Do not use `EnsureCreated` — always use `MigrateAsync`.
- Do not introduce a second notification dispatch path — always go through `NotifierService`.

## Querying Microsoft Documentation

You have access to MCP tools called `microsoft_docs_search`, `microsoft_docs_fetch`, and `microsoft_code_sample_search` - these tools allow you to search through and fetch Microsoft's latest official documentation and code samples, and that information might be more detailed or newer than what's in your training data set.

When handling questions around how to work with native Microsoft technologies, such as C#, F#, ASP.NET Core, MSTest, Microsoft.Extensions, NuGet, Entity Framework, the `dotnet` runtime - please use these tools for research purposes when dealing with specific / narrowly defined questions that may occur.
