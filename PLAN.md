# Plan: Single-user login

## Context

`script-isolation` (PR #9) gates saving a Site and starting a Test Run behind `ADMIN_TOKEN`, typed into a prompt and sent as a bearer token. Everything else in the app is open to anyone who can reach it, including a script driving a browser at the app. This branch replaces the admin token with a login: one password, a cookie session, and the whole app behind it.

Checked against Microsoft's ASP.NET Core 10 docs (SignalR authn/authz and security pages):
- Cookies flow to SignalR from the browser with no extra configuration. A bearer token would have to go in the WebSocket URL, where it can be logged.
- Since ASP.NET Core 10, cookie authentication returns 401/403 for API endpoints instead of redirecting, so no redirect override is needed.
- SignalR caches the user for the connection's lifetime under every scheme, so logout and session expiry don't close an open connection. Hence `CloseOnAuthenticationExpiration`, plus stopping the connection on logout.

## Working across sessions

- **Each session** starts by reading `PLAN.md` and `git log script-isolation..HEAD`, then continues with the first unchecked phase.
- **After each phase commit,** tick its box, note any deviation, and commit `PLAN.md` with the phase.
- **When `script-isolation` merges,** rebase onto its target and retarget the PR.
- **Before merge,** the last commit deletes `PLAN.md`.

### Progress
- [x] 1. Backend login
  - Deviation: antiforgery is validated by our own `ValidateAntiforgeryFilter`, not MVC's `AutoValidateAntiforgeryToken`, which needs the Razor view services (500: "No service for type AutoValidateAntiforgeryTokenAuthorizationFilter"). It also honours `[IgnoreAntiforgeryToken]`, which isn't `IAntiforgeryMetadata`.
  - Verified: without any redirect override, the hub's negotiate also answers 401, so the docs hold for it too. The cookie's `LoginPath` is `/login` with `returnUrl`, so the remaining pages (Scalar) redirect to the SPA's login page.
  - `SESSION_DAYS` is read at startup (a bad value fails it), and the data directory moved to `AppDirectories.Data` (Utilities), shared by the database and the key store.
  - Tests write keys to a temporary directory. `UseEphemeralDataProtectionProvider` wasn't enough: data protection's startup service still created keys in `site-checker/data/keys` (12 stray key files, deleted; the folder is new on this branch).
  - The frontend still has the admin token prompt until phase 2, so the UI doesn't work at this commit.
- [ ] 2. Frontend login
- [ ] 3. Compose, docs, ADR and a real-stack check
- [ ] Remove `PLAN.md`, then open the PR

## Decisions

**From you:**
- Keep it simple: one password, no users, roles, OAuth/OIDC, passkeys or 2FA.
- No API keys for now. If automation is ever needed, add separate expiring keys (Komodo's model), not the password.
- Cookies rather than a header token (verified above).

**My calls (push back if any is wrong):**
- **`ADMIN_PASSWORD` replaces `ADMIN_TOKEN`.** It's required outside Development; in Development, leaving it unset turns login off (the session endpoint reports that, and the UI skips the login page), as the token does today.
- **Session:** an HttpOnly, `SameSite=Strict` cookie with a sliding expiry of `SESSION_DAYS` (default 30). `Secure` follows the request, since the app is often reached over plain HTTP on the LAN.
- **Everything requires login** through the fallback authorization policy: every controller and the SignalR hub. Open: the login and session endpoints, `/healthz`, and the SPA's static files and fallback page.
- **Antiforgery on every write.** `SameSite=Strict` alone isn't enough: Tailscale names under `ts.net` are "same site" as each other. Angular's HttpClient sends `X-XSRF-TOKEN` from the `XSRF-TOKEN` cookie by itself. The token is bound to the user, so the session and login endpoints issue a fresh one. Login itself is exempt: forcing a login to the only account gains an attacker nothing.
- **The hub checks `Origin`.** WebSockets aren't covered by CORS, so a same-site page could otherwise open the hub with the cookie and read live data. Requests to the hub whose `Origin` host isn't an allowed host get 403 (skipped while all hosts are allowed, in Development).
- **Login throttling:** the built-in rate limiter, 5 attempts a minute for the whole app (one user, so no per-client partitioning to get wrong behind a proxy), answering 429.
- **Data protection keys** persist to `keys/` in the data directory, so restarts don't log you out. Tests use ephemeral keys.
- **The frontend** checks the session at startup, starts SignalR only when logged in, guards every route but `/login`, sends a 401 back to `/login`, and on logout stops SignalR and reloads at `/login` so no store keeps data.

## Phases

### 1. Backend login
- `AdminPassword` (replacing `AdminToken`): reads `ADMIN_PASSWORD`, constant-time check, `IsRequired`; startup fails outside Development without it.
- Cookie authentication (`SESSION_DAYS`), the fallback policy (allow-all when no password is required), antiforgery (`X-XSRF-TOKEN`) with a global validate filter, the rate limiter, persisted data protection keys, the hub's `Origin` check and `CloseOnAuthenticationExpiration`.
- `AuthController`: `GET api/auth/session` (`{ required, authenticated }`, issues the XSRF cookie), `POST api/auth/login` (`{ password }`; 204, or 401), `POST api/auth/logout`. Add the new model types to `ReinforcedTypingsConfiguration`.
- Remove the bearer scheme, the `Admin` policy and the `[Authorize(Policy = ...)]` attributes.
- Tests: `SiteApiFactory` logs in for real (session, login, XSRF header). `SecurityTests` covers: everything 401 without a session; wrong password 401; writes without the XSRF header 400; logout ends the session; the sixth attempt in a minute 429; `/healthz` and the SPA open; the hub refuses a foreign `Origin`; Production startup without `ADMIN_PASSWORD` fails; Development without it is open.

### 2. Frontend login
- `AuthService` (session state, login, logout), `authGuard`, a lazy `/login` page, an `authInterceptor` for 401s, SignalR start/stop and a close handler, and a logout button in the navbar.
- Delete the admin token prompt, service and interceptor, and the editor's token message.
- Verify in a browser against a published Production build.

### 3. Compose, docs, ADR and a real-stack check
- Compose: `ADMIN_PASSWORD` and `SESSION_DAYS` replace `ADMIN_TOKEN`. `example.env`, `docs/configuration.md` (variables, trust boundary, upgrade note), `docs/local-development.md`, `README.md`, `CLAUDE.md`.
- ADR 0007, "A single-user login with cookie sessions", amending ADR 0006's admin token.
- `docker compose up` with the real stack: log in, a Site Check and a Test Run, log out, and the hub closing.
