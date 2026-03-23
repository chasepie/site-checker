# Site Checker

A web application that monitors website availability and content changes using headless browser automation. Supports VPN-routed scraping for location-specific content, real-time notifications, and a live WebSocket dashboard.

Built with ASP.NET Core, Angular 21, and Playwright.

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

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 24+](https://nodejs.org/) and npm
- [Docker](https://www.docker.com/) and Docker Compose
- [Private Internet Access VPN](https://www.privateinternetaccess.com/) account (for VPN features)

## Quick Start

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

Two VS Code launch configurations are available for local development:

- **Launch App with Containers** — Automatically starts the Browserless and VPN Docker containers, then launches the backend. Use this when developing against containerized browser services. Set the following in your `.env` file to point at the locally running containers:
  ```
  BROWSERLESS_URL=ws://localhost:3000
  BROWSERLESS_URL_VPN=ws://localhost:3001
  ```
- **Launch App and Playwright** — Starts a local Playwright server alongside the backend. Use this for a fully local setup without Docker.

Both configurations will launch the Angular dev server and open the app in Chrome automatically.

#### Connecting to the VPN Container Locally

When debugging locally with the **Launch App with Containers** configuration, connections to the VPN-routed Browserless instance (`localhost:3001`) will fail with a `ECONNRESET` error by default. This happens because the `thrnz/docker-wireguard-pia` container's firewall drops all inbound traffic that doesn't originate from a whitelisted network.

To fix this, set `LOCAL_NETWORK` in your `.env` file to the CIDR of the network your host traffic appears to come from **inside the container**. This varies by platform:

| Platform                 | Typical value                                   | Why                                                   |
| ------------------------ | ----------------------------------------------- | ----------------------------------------------------- |
| macOS (Docker Desktop)   | `192.168.65.0/24`                               | Traffic is proxied through the Docker VM gateway      |
| Windows (Docker Desktop) | `192.168.65.0/24`                               | Same VM-based architecture as macOS                   |
| Linux (native Docker)    | The Compose bridge subnet, e.g. `172.19.0.0/16` | No VM; traffic comes from the Docker network directly |

To determine the exact value for your machine:

- **Docker Desktop (macOS/Windows):** Open **Docker Desktop → Settings → Resources → Network** and note the Docker subnet, or run:
  ```bash
  docker run --rm alpine sh -c "getent hosts host.docker.internal" | awk '{print $1}'
  ```
  Use the `/24` subnet of the returned IP (e.g. `192.168.65.254` → `192.168.65.0/24`).

- **Linux (native Docker):** Check the Compose network subnet:
  ```bash
  docker network inspect $(docker network ls --filter name=site-checker -q) --format '{{range .IPAM.Config}}{{.Subnet}}{{end}}'
  ```

Add the value to your `.env`:
```
LOCAL_NETWORK=192.168.65.0/24
```

Then recreate the VPN container to apply:
```bash
docker compose up -d --force-recreate vpn
```

#### Backend (manual)

```bash
# Build the solution
dotnet build

# Run the backend (requires Docker services or local Playwright running)
cd src/Backend
dotnet run
```

#### Frontend (manual)

```bash
cd src/Frontend

# Install dependencies
npm install

# Run development server
npm start
```

## Configuration

Configuration is managed through `appsettings.json`, `.env` files, and Docker environment variables.

### Environment Variables

| Variable                      | Required | Description                                                                               |
| ----------------------------- | -------- | ----------------------------------------------------------------------------------------- |
| `BROWSERLESS_TOKEN`           | Yes      | Authentication token for Browserless (any value works when self-hosting)                  |
| `PIA_USERNAME`                | Yes      | Private Internet Access VPN username                                                      |
| `PIA_PASSWORD`                | Yes      | Private Internet Access VPN password                                                      |
| `BROWSERLESS_URL`             | Docker   | WebSocket URL for the standard Browserless instance                                       |
| `BROWSERLESS_URL_VPN`         | Docker   | WebSocket URL for the VPN-routed Browserless instance                                     |
| `VPN_CHANGE_INTERVAL`         | No       | How often to rotate the VPN location in minutes (default: 10) — helps avoid bot detection |
| `PUSHOVER_TOKEN`              | No       | Pushover app token for notifications                                                      |
| `PUSHOVER_USER`               | No       | Pushover user key                                                                         |
| `DISCORD_TOKEN`               | No       | Discord bot token for notifications                                                       |
| `HEALTHCHECKS_URL`            | No       | Healthchecks.io ping URL for uptime monitoring                                            |
| `OpenTelemetry__OtlpEndpoint` | No       | OpenTelemetry collector endpoint                                                          |

`BROWSERLESS_URL` and `BROWSERLESS_URL_VPN` are set automatically when running via Docker Compose. They only need to be specified for local development.

## Docker Services

| Service         | Container                    | Port      | Description                       |
| --------------- | ---------------------------- | --------- | --------------------------------- |
| app             | site-checker                 | 8080      | Backend API + Angular frontend    |
| browserless     | site-checker-browserless     | 3000      | Standard headless Chrome instance |
| browserless-vpn | site-checker-browserless-vpn | (see vpn) | VPN-routed headless Chrome        |
| vpn             | site-checker-vpn             | 3001      | WireGuard VPN client (PIA)        |

```bash
# Start all services
docker compose up

# Rebuild and start
docker compose up --build

# Stop all services
docker compose down
```

## Database

The application uses **SQLite** with Entity Framework Core. Migrations run automatically on startup.

```bash
# Create a new migration (run from src/Database/)
cd src/Database
dotnet tool run dotnet-ef migrations add MigrationName

# Apply migrations manually
dotnet tool run dotnet-ef database update
```

Database files are stored in `site-checker/data/` and mounted as a Docker volume.

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
- .NET 10 / C# 14
- ASP.NET Core
- Entity Framework Core + SQLite
- OpenTelemetry

> The backend uses the C# 14 [extension members](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14#extension-members) feature, which replaces the traditional `static class` extension method pattern with a first-class `extension` block syntax.

> Discord notifications use [NetCord](https://github.com/NetCord/NetCord), currently in pre-release (`1.0.0-alpha`). The library is stable in practice but the version number reflects its pre-1.0 API status.

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
