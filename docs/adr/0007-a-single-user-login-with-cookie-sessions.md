# A single-user login with cookie sessions

> Amends [ADR 0006](0006-scripts-run-in-an-isolated-scrape-worker.md): the login replaces its admin token.

The whole app is behind a login with one password (`ADMIN_PASSWORD`) and no users. Logging in sets an HttpOnly, `SameSite=Strict` session cookie with a sliding lifetime of `SESSION_DAYS` (default 30). Its keys persist in the data directory, so restarts don't log you out. A fallback authorization policy requires the login on every endpoint, including the SignalR hub. The only exceptions are the login and session endpoints, `/healthz` and the SPA's static files.

Around it:
- Every controller write needs an antiforgery token. Angular's HttpClient sends it from the `XSRF-TOKEN` cookie. `SameSite=Strict` alone isn't enough, because hosts under a shared suffix such as `ts.net` count as the same site.
- Login attempts are limited to 5 a minute from each address and 30 from all of them, so another host can't lock you out by itself, and changing addresses doesn't buy more guesses. Only requests that can log in count, so a cross-site form post can't use them up. Behind a reverse proxy, the client's address comes from `X-Forwarded-For` only if the proxy is in `TRUSTED_PROXIES`.
- The hub refuses pages on other hosts, since CORS doesn't cover WebSockets.
- A logout closes every hub connection, because SignalR keeps the login a connection started with.

In Development without a password, login is off.

We chose this because ADR 0006's admin token gated only uploading and running scripts. Everything else stayed open to anyone who could reach the app, including a script driving a browser at it.

## Considered Options

- **Users, roles, OAuth or OIDC, passkeys, two-factor** (as in Komodo). Built for many users and an admin; SiteChecker has one. Single sign-on, if ever wanted, fits better as a login proxy (Authentik, Tailscale) in front of the app.
- **A token in browser storage, sent as a header** (also Komodo's approach). It needs no antiforgery. But browsers can't set headers on WebSockets, so SignalR would send the token in the URL, where servers log it, and page scripts could read it. Microsoft recommends cookies when the browser is the only client.
- **Keeping `ADMIN_TOKEN` as an API key.** Nothing calls the API outside the browser. If automation is ever needed, it should get separate, expiring keys, not the login password.

## Consequences

- One password guards everything, with no second factor. Keep the app on trusted networks, and put HTTPS and a proxy's own login in front of it before any internet exposure.
- Over plain HTTP, the session cookie isn't `Secure`, so someone watching the network could copy it. Sessions aren't stored on the server, so logging out doesn't revoke a copy; only its expiry or a password change does.
- New endpoints are private unless marked `[AllowAnonymous]`, and every controller write needs the antiforgery token.
- Losing the data directory's `keys/` folder logs everyone out, which is harmless.
