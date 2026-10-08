# Configuration

Configuration is managed through `appsettings.json`, `.env` files, and Docker environment variables.

## Environment Variables

| Variable                      | Required | Description                                                                               |
| ----------------------------- | -------- | ----------------------------------------------------------------------------------------- |
| `BROWSERLESS_TOKEN`           | Yes      | Authentication token for Browserless (any value works when self-hosting)                  |
| `ADMIN_PASSWORD`              | Yes      | The password for logging in to the app; everything but the login page needs a login. Use a long random value (`openssl rand -base64 24`). Required outside Development; when it's unset in Development, login is off |
| `SESSION_DAYS`                | No       | How many days a login lasts (default: 30, at most 36500). A visit in the second half of that renews it for the full period, so an unused login ends between half and all of it after the last visit. Restarting the app doesn't end it |
| `ALLOWED_HOSTS`               | Yes      | The host names the app is reached by, separated by semicolons, such as `sitechecker.lan;sitechecker.tailnet.ts.net`. Requests with any other `Host` header are rejected, so a web page can't reach the app by pointing its own domain at the app's address (DNS rebinding). `localhost`, `127.0.0.1` and `[::1]` are always allowed. Required outside Development, and can't include `*`, `0.0.0.0` or `[::]`, which ASP.NET Core treats as "any host" |
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
| `OpenTelemetry__OtlpEndpoint` | No       | OpenTelemetry collector endpoint                                                          |

`BROWSERLESS_URL` and `BROWSERLESS_URL_VPN` are set automatically when running via Docker Compose. They only need to be specified for local development.

## Login

Everything except the login page, `/healthz` and the app's static files needs a login
([ADR 0006](adr/0006-a-single-user-login-with-cookie-sessions.md)), including the live-update
connection, which a logout closes. Writes also need an antiforgery token, sent in a header, and the
live-update connection refuses pages from other origins, including other apps on the same host at
another port. `ALLOWED_HOSTS` stops web pages from reaching the app through DNS rebinding.

What's still exposed:

- **The password guards everything.** Use a long random one. Login attempts are limited to 5 a
  minute from each address and 30 a minute from all of them, so a host that changes its address
  (easy with IPv6) can still keep everyone locked out, though not guess faster. Behind a reverse
  proxy, list it in `TRUSTED_PROXIES`, or all clients share the proxy's 5.
- **The session cookie is as good as the password** until it expires (`SESSION_DAYS`) or the
  password changes. Logging out only removes it from that browser, so a copy of it keeps working;
  change `ADMIN_PASSWORD` to end every session. It's HttpOnly and `SameSite=Strict`, but it's only
  `Secure` when you reach the app over HTTPS, directly or through a proxy in `TRUSTED_PROXIES`. Over
  plain HTTP, someone who can watch your network traffic could copy it.
- **Other apps on the same host name get the session cookie.** Cookies ignore ports, and
  `SameSite=Strict` treats another port as the same site, so your browser sends the cookie to any
  app it visits at this host name on another port. The antiforgery header and the hub's origin
  check stop such an app's pages from using the API, but not its server from reading the cookie
  and using it. Only run apps you trust under the host names in `ALLOWED_HOSTS`.

Even so, keep the app on networks you trust, such as your home LAN or a private VPN like Tailscale.
The login is one shared password with no second factor, so don't expose the app to the internet
unless a reverse proxy in front of it adds HTTPS and its own login.

### Upgrading

Add `ADMIN_PASSWORD` and `ALLOWED_HOSTS` to `.env` before upgrading. Without them the app stops at
startup, and its log (`docker compose logs app`) says which is missing. Anything that called the API
outside the browser now needs a login.

## Trust boundary

**Anyone who can log in to Site Checker can run code on its host.** Saving a Site or starting a
Test Run accepts C# source that runs inside the app, unsandboxed (see
[ADR 0004](adr/0004-scripts-run-in-process-behind-a-trust-boundary.md)). The app container mounts
`/var/run/docker.sock` for VPN rotation, and `:ro` doesn't restrict API calls on a socket, so a script
can start a privileged container and take over the host. It can also read the database file and the
environment variables.

So the password is the key to running code: use a long random one, and keep the app on networks you
trust (see [Login](#login)).

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

Only the app gets all of `.env`. The vpn container gets `PIA_USERNAME`, `PIA_PASSWORD` and
`LOCAL_NETWORK` from it, so the app's secrets stay out of a third-party image. To pass it other
[docker-wireguard-pia](https://github.com/thrnz/docker-wireguard-pia) settings, add them to its
`environment` in `docker-compose.yml`.

```bash
# Start all services
docker compose up

# Rebuild and start
docker compose up --build

# Stop all services
docker compose down
```
