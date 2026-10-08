# Sites and their Scrapers are data, not compiled code

> Accepted in [design 0001](../design/0001-scraper-architecture.md); lands with its stage 1. Until then, Scrapers are still `ScraperBase` subclasses.

Each Site stores its Scraper in a JSON column on `Site`: a flat `ScraperDefinition` with a `Kind` and one nullable payload per kind (Script, then Steps, then Prompt). One shared pipeline runs every kind. We chose this over keeping compiled `ScraperBase` subclasses, because adding a Site took a code change, rebuild and redeploy, and runtime C# scripts use the same Playwright API without one. Keeping compiled Scrapers as an extra kind was rejected because every Site would still go through one of two paths.

## Considered Options

- **Scrapers as files in a mounted folder.** Editable in an IDE and versionable in git, but Sites are created and edited through the API and UI, so a second source of truth would need file watching and conflict handling. SQLite stays the only source of truth.
- **A polymorphic class hierarchy behind a value converter.** Gives each kind its own type, but EF can't query or track changes inside a converted value. EF Core 10 complex types don't support inheritance, so the flat type with a `Kind` is the shape that keeps both. Saving rejects a definition whose payload doesn't match its `Kind`.

## Consequences

- `DataSeeder` only seeds demo Sites into an empty database and never deletes. Sites created at runtime have to survive restarts.
- A script's source lives in its own `SiteScript` table, not on `Site`, so it stays out of Site lists and out of the save interceptor's broadcasts (which send old and new values).
- Site Checks don't record which version of a Scraper produced them.
