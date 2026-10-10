# Configuration

Configuration is managed through `appsettings.json`, `.env` files, and Docker environment variables.

## Environment Variables

| Variable                      | Required | Description                                                                               |
| ----------------------------- | -------- | ----------------------------------------------------------------------------------------- |
| `BROWSERLESS_TOKEN`           | Yes      | Authentication token for Browserless (any value works when self-hosting)                  |
| `ADMIN_PASSWORD`              | Yes      | The password for logging in to the app; everything but the login page needs a login. Use a long random value (`openssl rand -base64 24`). Required outside Development; when it's unset in Development, login is off |
| `SESSION_DAYS`                | No       | How many days a login lasts (default: 30, at most 36500). A visit in the second half of that renews it for the full period, so an unused login ends between half and all of it after the last visit. Restarting the app doesn't end it |
| `ALLOWED_HOSTS`               | Yes      | The host names the app is reached by, separated by semicolons, such as `sitechecker.lan;sitechecker.tailnet.ts.net`. Requests with any other `Host` header are rejected, so a web page can't reach the app by pointing its own domain at the app's address (DNS rebinding). `localhost` is always allowed. Required outside Development, and can't include `*`, `0.0.0.0` or `[::]`, which ASP.NET Core treats as "any host" |
| `TRUSTED_PROXIES`             | No       | The reverse proxies in front of the app, as IP addresses or CIDR ranges separated by semicolons, such as `172.18.0.0/16`. The app believes the client address and scheme they forward (`X-Forwarded-For`, `X-Forwarded-Proto`), so the login cookie is `Secure` when the proxy serves HTTPS, and login attempts are counted per client. List the address the proxy connects to the app from: a proxy container's address or its Docker network's range, or that network's gateway for a proxy on the Docker host (`docker network inspect`). When unset, forwarded headers are ignored |
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
| `SCRAPE_WORKER_SECRET`        | Yes      | Shared by the app and the Scrape Worker. The worker runs the script in each request, so it only takes requests carrying this, which keeps anything else that reaches it (such as a page loaded in Browserless) from running code there. Use a long random value (`openssl rand -hex 32`), different from `ADMIN_PASSWORD`. Required outside Development, on both sides |
| `DOCKER_HOST`                 | No       | The Docker API the app restarts the VPN containers through. Docker Compose sets it to the socket proxy (`tcp://docker-proxy:2375`); when unset, the local Docker socket is used |
| `DOCKER_GID`                  | No       | Docker Compose only: the group that owns the Docker socket, which the socket proxy runs as. Default `0`, right for Docker Desktop; on Linux it's usually the `docker` group (`getent group docker \| cut -d: -f3`) |
| `OpenTelemetry__OtlpEndpoint` | No       | OpenTelemetry collector endpoint                                                          |

`BROWSERLESS_URL` and `BROWSERLESS_URL_VPN` are set automatically when running via Docker Compose. They only need to be specified for local development.

## Trust boundary

Saving a Site or starting a Test Run uploads C# source that runs unsandboxed. With Docker Compose it
runs in the **Scrape Worker** container, not in the app (see
[ADR 0006](adr/0006-scripts-run-in-an-isolated-scrape-worker.md)). The worker has no secrets but the
Browserless token and its own secret, no volumes, no access to Docker, and no route to the internet except through the
browsers. So a script can't read the database, the notification tokens or the PIA credentials, and
can't start containers.

The worker only takes requests carrying `SCRAPE_WORKER_SECRET` and addressed to its own host name, so
a page loaded in Browserless can't run code there. Browserless's ports are published only on the
Docker host (`127.0.0.1`), not to the LAN.

What's still exposed:

- **The password is the key to running code.** Anyone who logs in can run code in the worker. Use a
  long random password. Login attempts are limited to 5 a minute from each address and 30 a minute
  from all of them, so a host that changes its address (easy with IPv6) can still keep everyone
  locked out, though not guess faster. Behind a reverse proxy, list it in `TRUSTED_PROXIES`, or all
  clients share the proxy's 5.
- **The session cookie is as good as the password** until it expires (`SESSION_DAYS`) or the
  password changes. Logging out only removes it from that browser, so a copy of it keeps working;
  change `ADMIN_PASSWORD` to end every session. It's HttpOnly and `SameSite=Strict`, but it's only
  `Secure` when you reach the app over HTTPS, directly or through a proxy in `TRUSTED_PROXIES`. Over
  plain HTTP, someone who can watch your network traffic could copy it.
- **The browsers can reach your LAN.** A script drives Browserless, which can load any address the
  Docker host can.
- **Without `SCRAPE_WORKER_URL`, scripts run inside the app**, with its secrets and database. That's
  meant for local development; the app warns at startup if it happens in a container.

Everything except the login page, `/healthz` and the app's static files needs a login, including the
live-update connection, which a logout closes. Writes also need an antiforgery token, and the
live-update connection refuses pages on other hosts. `ALLOWED_HOSTS` stops web pages from reaching
the app through DNS rebinding.

Even so, keep the app on networks you trust, such as your home LAN or a private VPN like Tailscale.
The login is one shared password with no second factor, so don't expose the app to the internet
unless a reverse proxy in front of it adds HTTPS and its own login.

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

1. Add `ADMIN_PASSWORD`, `ALLOWED_HOSTS` and `SCRAPE_WORKER_SECRET` to `.env`. Without them the
   app (or the worker) stops at startup, and its log (`docker compose logs app scrape-worker`) says
   which is missing. `ADMIN_TOKEN` is no longer used: the app asks for `ADMIN_PASSWORD` on a login
   page instead.
2. Make the bind-mounted directories writable by UID 1654: `sudo chown -R 1654 site-checker/`.
   Docker Desktop doesn't need this.
3. On Linux, set `DOCKER_GID` in `.env` to the Docker socket's group, so the socket proxy can reach it.
4. `docker compose up --build`.

The VPN container now gets only `PIA_USERNAME`, `PIA_PASSWORD` and `LOCAL_NETWORK` from `.env`. If you
set other `thrnz/docker-wireguard-pia` options there, add them to its `environment` in
`docker-compose.yml`. The same goes for the app: it no longer reads `.env` wholesale, so any other
setting you kept there for it (such as `PIA_CONTAINER_NAME`, `ASPNETCORE_ENVIRONMENT` or a
`Logging__...` override) has to be added to the app's `environment`.

## Docker Services

| Service         | Container                    | Port      | Description                       |
| --------------- | ---------------------------- | --------- | --------------------------------- |
| app             | site-checker                 | 8080      | Backend API + Angular frontend    |
| browserless     | site-checker-browserless     | 3000 (host only) | Standard headless Chrome instance |
| browserless-vpn | site-checker-browserless-vpn | (see vpn) | VPN-routed headless Chrome        |
| vpn             | site-checker-vpn             | 3001 (host only) | WireGuard VPN client (PIA)        |
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
