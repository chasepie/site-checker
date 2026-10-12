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
| `VPN_CHANGE_INTERVAL`         | No       | How often to rotate the VPN location in minutes (default: 10) — helps avoid bot detection |
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

Even so, keep the app on networks you trust, such as your home LAN or a private VPN like Tailscale.
The login is one shared password with no second factor, so don't expose the app to the internet
unless a reverse proxy in front of it adds HTTPS and its own login.

### Upgrading

Add `ADMIN_PASSWORD` and `ALLOWED_HOSTS` to `.env` before upgrading. Without them the app stops at
startup, and its log (`docker compose logs app`) says which is missing. Anything that called the API
outside the browser now needs a login.

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
