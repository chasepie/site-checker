# Site Checker

Site Checker is a self-hostable web app that monitors websites for availability and content changes using headless browser automation, custom scraping logic, and VPN routing to bypass bot detection. It provides real-time notifications via Pushover and Discord, and a live dashboard built with Angular using SignalR for real-time updates.

I built it because I wanted to know the moment a product was back in stock, a hotel room opened up, or a price dropped, but existing apps/tools weren't dynamic enough to handle sites where the content I needed wasn't directly bookmarkable (think navigating to a specific date on a hotel search, scrolling through a page, clicking through to a product, etc.). Bots and scrapers have become a real problem over the last few years, so I deliberately kept this tool passive — it only checks at a set interval and notifies me of changes, leaving the actual purchase to me.

I've been able to use this tool to purchase a GPU during the 2020 chip shortage, snag hard-to-get hotel rooms for future conventions, and monitor price drops on products I want to buy (hopefully RAM prices some day 🫠). It's also been a fun opportunity to experiment with new technologies like Playwright, Angular Signals, and the latest C#/.NET features. Hopefully it's useful to others too!

**Dashboard**

<img src="./docs/screenshots/homepage.png" alt="Homepage screenshot" width="800" />

**Details View**

<img src="./docs/screenshots/details.png" alt="Details screenshot" width="800" />

## Features

- **Automated Web Scraping** — Monitor websites for availability and content changes via Playwright browser automation
- **VPN-Routed Scraping** — Scrape location-specific content through Private Internet Access (PIA) VPN integration with automatic location rotation to reduce bot detection
- **Real-time Notifications** — Alerts via Pushover and Discord when changes are detected
- **Live Dashboard** — WebSocket-based real-time updates using SignalR
- **Observability** — OpenTelemetry integration for structured logging, tracing, and health monitoring

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Frontend (Angular 21)                │
│                  SPA for monitoring & management        │
└──────────────────────┬──────────────────────────────────┘
                       │ HTTP/SignalR
┌──────────────────────▼──────────────────────────────────┐
│              Backend (ASP.NET Core)                     │
│  API Server, Scraping Orchestration, VPN Management     │
└─┬────────────┬───────────────────────┬──────────────────┘
  │            │                       │
  ▼            ▼                       ▼
┌────────┐  ┌─────────────┐   ┌──────────────────────┐
│Database│  │  Notifiers  │   │   Browserless        │
│(SQLite)│  │Push/Discord │   │Standard & VPN-routed │
└────────┘  └─────────────┘   └──────────────────────┘
```

### Components

- **Backend** — ASP.NET Core API server with controllers, services, and VPN management
- **Database** — EF Core models, migrations, and services using SQLite
- **Frontend** — Angular 21 SPA for monitoring and managing site checks
- **Scraper** — Reusable Playwright-based scraping library
- **Notifiers** — Pushover and Discord notification implementations

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 24+](https://nodejs.org/) and npm
- [Docker](https://www.docker.com/) and Docker Compose
- [Private Internet Access VPN](https://www.privateinternetaccess.com/) account (for VPN features)

### Using Docker (Recommended)

1. **Clone the repository**
   ```bash
   git clone https://github.com/chasepie/site-checker.git
   cd site-checker
   ```

2. **Configure environment variables**
   ```bash
   cp example.env .env
   # Edit .env with your configuration
   ```

3. **Start all services**
   ```bash
   docker compose up
   ```

4. **Access the application**
   - Application: http://localhost:8080
   - API docs: http://localhost:8080/scalar

### Local Development

Two VS Code launch configurations are available: **Launch App with Containers** (uses Browserless + VPN Docker containers) and **Launch App and Playwright** (fully local, no Docker required). Both will start the Angular dev server and open the app in Chrome automatically.

See [docs/local-development.md](docs/local-development.md) for full setup instructions, manual backend/frontend steps, and VPN container networking troubleshooting.

## Configuration

Configuration is managed through `appsettings.json`, `.env` files, and Docker environment variables. See [docs/configuration.md](docs/configuration.md) for the full environment variables reference and Docker services table.

## Development

### Creating a New Scraper

1. Create a class inheriting from `ScraperBase`:

```csharp
public class ExampleScraper(ILogger<ExampleScraper> logger)
    : ScraperBase(logger, ExampleScraper.ScraperId, ExampleScraper.DefaultUrl)
{
    public const string ScraperId = "example-scraper";
    public const string DefaultUrl = "https://example.com";

    protected override async Task<ScrapeResult> DoScrapeAsync(IPage page, ScrapeRequest request)
    {
        request.LogInfo(_logger, "Starting scrape");

        var locator = await page.WaitForFirstLocatorAsync([
            page.Locator("selector1"),
            page.Locator("selector2")
        ]);

        var text = await locator.TextContentAsync();

        return new ScrapeResult { IsSuccess = true, Content = text };
    }
}
```

2. Register it by calling `services.AddScraper<ExampleScraper>()` inside `AddScraperServices()` in `src/Scraper/ScraperService.cs`.

3. Add an entry in `DataSeeder.cs` to seed the initial site record.

## Technology Stack

### Backend
- .NET 10 / C# 14 (uses first-class [extension members](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14#extension-members) over the traditional `static class` pattern)
- ASP.NET Core
- Entity Framework Core + SQLite
- OpenTelemetry
- [Reinforced.Typings](https://github.com/reinforced/Reinforced.Typings) — generates TypeScript interfaces and Zod schemas from C# models at build time
- [NetCord](https://github.com/NetCord/NetCord) for Discord notifications (pre-release `1.0.0-alpha`, stable in practice)

### Frontend
- Angular 21
- TypeScript 5.9
- NgRx Signals + RxJS + SignalR Client
- Zod (runtime validation)
- AG Grid + ng-bootstrap

### Infrastructure
- Docker & Docker Compose
- Playwright + Browserless Chrome
- WireGuard VPN (PIA)

## Acknowledgments

- Browser automation by [Playwright](https://playwright.dev/)
- VPN integration via [thrnz/docker-wireguard-pia](https://github.com/thrnz/docker-wireguard-pia)
- Notifications via [Pushover](https://pushover.net/) and [Discord](https://discord.com/)
