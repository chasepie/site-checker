# Configuration

Configuration is managed through `appsettings.json`, `.env` files, and Docker environment variables.

## Environment Variables

| Variable                      | Required | Description                                                                               |
| ----------------------------- | -------- | ----------------------------------------------------------------------------------------- |
| `BROWSERLESS_TOKEN`           | Yes      | Authentication token for Browserless (any value works when self-hosting)                  |
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
| `OpenTelemetry__OtlpEndpoint` | No       | OpenTelemetry collector endpoint                                                          |

`BROWSERLESS_URL` and `BROWSERLESS_URL_VPN` are set automatically when running via Docker Compose. They only need to be specified for local development.

## Trust boundary

**Anyone who can reach Site Checker can run code on its host.** The app has no authentication, and
saving a Site or starting a Test Run accepts C# source that runs inside the app, unsandboxed (see
[ADR 0004](adr/0004-scripts-run-in-process-behind-a-trust-boundary.md)). The app container mounts
`/var/run/docker.sock` for VPN rotation, and `:ro` doesn't restrict API calls on a socket, so a script
can start a privileged container and take over the host. It can also read the database file and the
environment variables.

Only make the app reachable from networks where everyone is trusted, such as your home LAN or a
private VPN like Tailscale. Never expose it to the internet, even through a reverse proxy, unless the
proxy requires a login.

## Upgrading to Sites as data

Sites are no longer defined in code, and the database starts fresh: the migration history was
replaced. Before upgrading an existing deployment, stop it and delete the old database file
(`site-checker/data/SiteChecker.db`, or the file in the `/app/data` volume). Started against an old
database, the app fails with "table Sites already exists". Then re-create your Sites in the UI,
uploading their scripts, or set `SEED_DEMO_DATA=true` once to get the two demo Sites back.

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
