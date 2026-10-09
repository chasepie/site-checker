# Configuration

Configuration is managed through `appsettings.json`, `.env` files, and Docker environment variables.

## Environment Variables

| Variable                      | Required | Description                                                                               |
| ----------------------------- | -------- | ----------------------------------------------------------------------------------------- |
| `BROWSERLESS_TOKEN`           | Yes      | Authentication token for Browserless (any value works when self-hosting)                  |
| `ADMIN_TOKEN`                 | Yes      | The token the UI asks for before saving a Site or starting a Test Run, since both run code on the host. Use a long random value (`openssl rand -hex 32`). Required outside Development; when it's unset in Development, anyone can save Sites and start Test Runs |
| `ALLOWED_HOSTS`               | Yes      | The host names the app is reached by, separated by semicolons, such as `sitechecker.lan;sitechecker.tailnet.ts.net`. Requests with any other `Host` header are rejected, so a web page can't reach the app by pointing its own domain at the app's address (DNS rebinding). `localhost` is always allowed. Required outside Development, and can't be `*` |
| `PIA_USERNAME`                | Yes      | Private Internet Access VPN username                                                      |
| `PIA_PASSWORD`                | Yes      | Private Internet Access VPN password                                                      |
| `BROWSERLESS_URL`             | Docker   | WebSocket URL for the standard Browserless instance                                       |
| `BROWSERLESS_URL_VPN`         | Docker   | WebSocket URL for the VPN-routed Browserless instance                                     |
| `VPN_CHANGE_INTERVAL`         | No       | How often to rotate the VPN location in minutes (default: 15; Docker Compose sets 10) — helps avoid bot detection |
| `SCRAPE_TIMEOUT`              | No       | How long a scrape may run, in seconds, for Sites that don't set their own timeout (default: 120). Must leave 10 s under `BROWSERLESS_TIMEOUT`, or startup fails |
| `BROWSERLESS_TIMEOUT`         | No       | When Browserless ends a session, in milliseconds (default: 180000). Docker Compose passes the same value to both Browserless containers as `TIMEOUT`, so they can't drift |
| `SEED_DEMO_DATA`              | No       | `true` seeds the two demo Sites into a database with no Sites; `false` never does. When unset, it seeds only in Development |
| `PUSHOVER_TOKEN`              | No       | Pushover app token for notifications                                                      |
| `PUSHOVER_USER`               | No       | Pushover user key                                                                         |
| `DISCORD_TOKEN`               | No       | Discord bot token for notifications                                                       |
| `HEALTHCHECKS_URL`            | No       | Healthchecks.io ping URL for uptime monitoring                                            |
| `SCRAPE_WORKER_URL`           | Docker   | Where the Scrape Worker listens. Docker Compose sets it (`http://scrape-worker:8080`), so scripts run in the worker's container rather than the app's. When unset, the app runs scrapes itself, which is meant for local development, and warns at startup if it's in a container |
| `DOCKER_HOST`                 | No       | The Docker API the app restarts the VPN containers through. Docker Compose sets it to the socket proxy (`tcp://docker-proxy:2375`); when unset, the local Docker socket is used |
| `DOCKER_GID`                  | No       | Docker Compose only: the group that owns the Docker socket, which the socket proxy runs as. Default `0`, right for Docker Desktop; on Linux it's usually the `docker` group (`getent group docker \| cut -d: -f3`) |
| `OpenTelemetry__OtlpEndpoint` | No       | OpenTelemetry collector endpoint                                                          |

`BROWSERLESS_URL` and `BROWSERLESS_URL_VPN` are set automatically when running via Docker Compose. They only need to be specified for local development.

## Trust boundary

**Anyone who can reach Site Checker can run code on its host.** The app has no authentication, and
saving a Site or starting a Test Run accepts C# source that runs inside the app, unsandboxed (see
[ADR 0004](adr/0004-scripts-run-in-process-behind-a-trust-boundary.md)). A script can read the database
file and the app's environment variables. The app reaches Docker only through a socket proxy that
allows inspecting the Browserless VPN container and restarting the two VPN containers, so a script
can't start containers, but it can restart those two.

The admin token (`ADMIN_TOKEN`) is needed to save a Site or start a Test Run, and `ALLOWED_HOSTS`
stops web pages from reaching the app through DNS rebinding. Both narrow who can upload a script,
but anyone with the token still runs code on the host.

Only make the app reachable from networks where everyone is trusted, such as your home LAN or a
private VPN like Tailscale. Never expose it to the internet, even through a reverse proxy, unless the
proxy requires a login.

## Upgrading to Sites as data

Sites are no longer defined in code, and the database starts fresh: the migration history was
replaced. Before upgrading an existing deployment, stop it and delete the old database file
(`site-checker/data/SiteChecker.db`, or the file in the `/app/data` volume). Started against an old
database, the app fails with "table Sites already exists". Then re-create your Sites in the UI,
uploading their scripts, or set `SEED_DEMO_DATA=true` once to get the two demo Sites back.

## Upgrading to the hardened containers

The app container now runs as the image's non-root user (UID 1654) on a read-only filesystem, reaches
Docker through a socket proxy, and only receives the environment variables it reads. Before upgrading
an existing deployment:

1. Add `ADMIN_TOKEN` and `ALLOWED_HOSTS` to `.env`. Docker Compose refuses to start without them.
2. Make the bind-mounted directories writable by UID 1654: `sudo chown -R 1654 site-checker/`.
   Docker Desktop doesn't need this.
3. On Linux, set `DOCKER_GID` in `.env` to the Docker socket's group, so the socket proxy can reach it.
4. `docker compose up --build`.

The VPN container now gets only `PIA_USERNAME`, `PIA_PASSWORD` and `LOCAL_NETWORK` from `.env`. If you
set other `thrnz/docker-wireguard-pia` options there, add them to its `environment` in
`docker-compose.yml`.

## Docker Services

| Service         | Container                    | Port      | Description                       |
| --------------- | ---------------------------- | --------- | --------------------------------- |
| app             | site-checker                 | 8080      | Backend API + Angular frontend    |
| browserless     | site-checker-browserless     | 3000      | Standard headless Chrome instance |
| browserless-vpn | site-checker-browserless-vpn | (see vpn) | VPN-routed headless Chrome        |
| vpn             | site-checker-vpn             | 3001      | WireGuard VPN client (PIA)        |
| docker-proxy    | site-checker-docker-proxy    | (none)    | Allowlisting Docker socket proxy  |
| scrape-worker   | site-checker-scrape-worker   | (none)    | Runs every scrape and script      |

```bash
# Start all services
docker compose up

# Rebuild and start
docker compose up --build

# Stop all services
docker compose down
```
