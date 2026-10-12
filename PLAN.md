# Plan: Script isolation and hardening

## Context

Script Scrapers run unsandboxed inside the app (ADR 0004). The app has no authentication, holds every secret, owns the SQLite database and mounts the Docker socket. So anyone who can reach the app, or get a browser on the LAN to send it requests (DNS rebinding, since `AllowedHosts` is `*`), can run code that controls the host.

This work moves scripts into a **Scrape Worker** container that has no secrets, no database, no Docker access and no direct internet access. It also hardens everything around the worker:
- the app accepts only known Host headers
- an admin token gates script uploads and Test Runs
- the app reaches Docker only through an allowlisting socket proxy that permits only "inspect and restart the two VPN containers"
- each container gets only the env vars it reads
- the app container runs non-root on a read-only filesystem

A worker that is stuck in a synchronous loop exits so Docker restarts it, which removes ADR 0004's leaked-thread consequence.

## Working across sessions

- **First action:**
  1. You commit the 13 pending stage 1 changes on `scraper-architecture-stage-1`.
  2. Then create `script-isolation` from that branch.
  3. Copy this plan to `PLAN.md` at the repo root and commit it ("Add script isolation working plan").
- **Each session** starts by reading `PLAN.md` and `git log scraper-architecture-stage-1..HEAD`, then continues with the first unchecked phase.
- **After each phase commit,** tick its box, add a line about anything that deviated from the plan, and commit `PLAN.md` with that phase.
- **When stage 1 merges,** rebase onto `main` (`git rebase --onto main scraper-architecture-stage-1`) and retarget the PR.
- **Before merge,** the last commit deletes `PLAN.md`.

### Progress
- [x] 1. Host filtering and the admin token
  - Deviation: both `ADMIN_TOKEN` and `ALLOWED_HOSTS` are required outside Development (not only in Production), so Staging can't slip through.
  - Deviation: the startup check is `Program.ValidateSecuritySettings`, not an extension: two `extension` blocks in one class trip CA1708.
  - Note: authorization runs before validation, so any gated request without the token gets 401 and the prompt first, even one that would fail validation.
  - Verified in a browser against a published Production build (`ADMIN_TOKEN` and `ALLOWED_HOSTS` set, scratch database): a wrong Host gets 400, the prompt appears on Save, a wrong token shows "needs the admin token" and isn't stored, and the right token saves the Site and is remembered.
  - Found, not fixed: the existing `authInterceptor` (SignalR connection ID header) was never registered, so `EntityChangesService` never skips the sender. Harmless today (stores also update locally); worth a separate fix.
- [x] 2. VPN without container creation
  - `ListLocationsAsync` stays on `IPiaContainers` (the seam the harness fakes); `DockerPiaContainers` delegates it to `PiaServerList`, which owns the US filter and shuffle now.
  - Deviation: a region whose `servers.wg` is an empty array is skipped. `wg-gen.sh`'s jq treats `[]` as truthy, but such a region can't connect. None exist in today's list (204 regions, all with WireGuard).
  - Container names default to `site-checker-vpn` and `site-checker-browserless-vpn` without the leading `/`, which is still trimmed from configured values.
  - Not yet checked against real containers (none running locally); phase 3's `docker compose up` covers it.
- [x] 3. Docker socket proxy, env split and app container hardening
  - Deviation: the proxy runs as `65534:${DOCKER_GID:-0}`. On Docker Desktop the socket is `root:root 660` and the proxy exits with "permission denied" as plain 65534; group 0 fixes it, and Linux sets `DOCKER_GID` to the docker group.
  - Deviation: the allowlist regexes make the API version prefix optional (`(/v1\.[0-9]+)?`); Docker.DotNet sends `/v1.47/`.
  - Found and fixed: the final image had no `curl`, so the existing `HEALTHCHECK` never passed. It's installed in the final stage now (phase 8's worker needs a working healthcheck too).
  - Verified with `docker compose up` plus a scratch override (stand-in alpine containers under the real VPN and Browserless container names, scratch volumes; no `.env` here, so no real PIA VPN):
    - the app is healthy as UID 1654 on a read-only root, with writable `/tmp` and `/app/data`
    - no PIA variables in the app's environment
    - through the proxy, listing containers, creating one, inspecting the VPN container and restarting Browserless all get 403, and inspecting Browserless VPN gets 200
    - another container can't resolve the proxy
    - `AllLocations` returns 54 US locations from the live list, and `ChangeLocation` writes `loc.txt` and restarts both VPN containers by name
  - Checked in `thrnz/docker-wireguard-pia`'s `run`: an empty `LOCAL_NETWORK` is treated as unset (`[ -n ... ]`).
  - Not checked: the real PIA VPN container with the env split (needs PIA credentials).
- [x] 4. A serializable scrape contract
  - Deviation: `GetBrowserType` stays on `IScraperService` (both implementations delegate to the new `BrowserSelector`), so the runner, `RunTestAsync` and the harness's `FakeScraperService.BrowserType` don't change. `BrowserProvider` only connects now.
  - Deviation: the captured logs are `ScrapeResult.Logs` (`ScraperLogEntry`, `ScraperLog`), not `ScriptLogs`: the pipeline creates the log and hands it to every executor through `ExecutorContext.Log`, so it isn't Script-specific, and the pipeline attaches it to every result, including timeouts and scripts that throw. Debug and above are recorded; Trace is only passed on.
  - The failure dumps' `.log` now includes the Scraper log, and the runner writes the dumps after releasing the scrape lock.
  - `CLAUDE.md`'s pipeline bullet was rewritten here, since it described the pipeline writing dumps.
- [x] 5. Abandoned runs
  - Deviation: `ExitingAbandonedRunMonitor` takes `stopApplication` and `failFast` callbacks instead of `IHostApplicationLifetime`, so `src/Scraper` gains no hosting dependency; the worker wires them up. Both monitors expose `HasAbandonedRuns`, on the interface.
  - `Scraper.UnitTests` gains `Microsoft.Extensions.TimeProvider.Testing` (lock file updated).
- [x] 6. The Scrape Worker
  - Deviation: the worker logs to the console only; `OpenTelemetryExtensions` stays in Backend. On its internal network the worker couldn't reach a collector anyway, and Scraper logs reach the app's OpenTelemetry pipeline through `ScrapeResult.Logs`.
  - Service registration is in `ScrapeWorkerServices` and the endpoints in `ScrapeWorkerEndpoints` (two `extension` blocks in one class trip CA1708). It loads `.env` like the app, for local runs, and listens on `http://localhost:5280` from `launchSettings.json`.
  - When the exiting monitor stops the worker, it sets exit code 1 first.
  - Gotcha: in the test project, `WebApplicationFactory<Program>` resolved to `Microsoft.Playwright.Program`, because `using` directives inside the namespace win over the enclosing namespace. Use `SiteChecker.ScrapeWorker.Program`.
  - Smoke-tested `dotnet run --project src/ScrapeWorker`: `/healthz` is Healthy and `DELETE /scripts/1` is 204.
- [x] 7. RemoteScraperService
  - The waits are in `RemoteScraperOptions` (readiness 120 s, polled every 1 s; response allowance `ArtifactBudget` + 25 s), so tests use short ones. It uses a named `HttpClient` from `IHttpClientFactory` with an infinite client timeout; each call sets its own deadline.
  - Outcome logging moved to a shared `LogScrapeOutcome` extension, so the app logs the same "Scraped ..." lines for remote scrapes. Re-logged Scraper entries use `ScriptExecutor.LoggerCategory` and the same Site and Site Check scope.
  - Tests: fake-worker cases in `Backend.UnitTests` (healthy, waits for health, never healthy, dropped connection, 503, no result in time, cancellation, eviction), and real-worker round trips in `Backend.IntegrationTests` (which now references `ScrapeWorker`).
  - Verified for real: published app (Production, `SCRAPE_WORKER_URL` set) and the worker as separate processes, with the worker on the already-running local Playwright server (`USE_LOCAL_BROWSER`).
    - A queued check of the Bot Detection demo Site ran through the worker against the live page and was recorded Succeeded ("Test Results:Robot").
    - A Site whose script throws was recorded as an Unexpected Failure with `EXCEPTION_TYPE` in its metadata. The app wrote `{check}_{site}.log` (with the script's stack trace at `Throwing.cs:line 6` and the Scraper log) and `.html`, and re-logged the script's entry under `SiteChecker.Script`.
- [x] 8. Worker container and networks
  - The Dockerfile has a shared `runtime` stage (curl, `USER $APP_UID`, healthcheck) and two targets, `worker` and `app`. `app` is last, so a plain `docker build` still produces the app.
  - Resolved risk: `thrnz/docker-wireguard-pia`'s firewall loops over every interface present at startup and accepts each one's own subnet, so the VPN container on both `default` and `scrape` accepts the worker. Checked in its `run` script; the real VPN wasn't run (no PIA credentials here).
  - Verified with `docker compose up` (real Browserless; VPN containers as stand-ins; scratch volumes; `SEED_DEMO_DATA=true`):
    - a Bot Detection check ran through the worker and real Browserless and Succeeded
    - inside the worker: UID 1654, read-only root, no `/app/data`, and only `BROWSERLESS_TOKEN` among the secrets (fake `PUSHOVER_TOKEN`, PIA and admin values set for the run didn't reach it)
    - the worker has no route to example.com and can't resolve `docker-proxy`; it reaches Browserless and `vpn`
    - it can read the app's API, and a gated write gets 401
    - a `while (true) { }` script with a 3 s timeout failed "Timed out after 3 s.", and the worker logged Critical 15 s later and stopped. Docker restarted it (RestartCount 1), the app logged "Waiting for the Scrape Worker to report healthy", and the next queued check Succeeded, 22 s after both were queued.
  - Found, not fixed: a `DISCORD_TOKEN` that isn't a valid bot token makes NetCord fail host startup. On the way down, `SiteCheckTimer` then hit an `ObjectDisposedException` on the runner's create lock (`SiteCheckRunner.cs:161`), a shutdown race that predates this work.
- [x] 9. ADR, docs and end-to-end check
  - Added ADR 0006 and marked 0004 superseded. Updated `CLAUDE.md`, `GLOSSARY.md` (Scrape Worker), `README.md` (diagram, components, security), `docs/configuration.md` (trust boundary rewritten), `docs/local-development.md` (running the worker locally) and `example.env`.
  - Deviation: Compose passes `ADMIN_TOKEN` and `ALLOWED_HOSTS` with `:-`, not `:?`. `:?` failed every Compose command, including the VS Code task that starts only the browsers (`docker compose up -d browserless`). The app's own startup check reports a missing value instead (verified: "ADMIN_TOKEN must be set ..." in `docker logs`).
  - The admin token's wording now says Sites and Test Runs "upload or run a script" rather than "run code on the host", which stopped being true with the worker.
  - Final pass on the rebuilt stack (real Browserless, VPN stand-ins): from the UI, a Test Run of the Bot Detection demo asked for the admin token, ran in the worker, and showed "Succeeded in 2.0 s", "Test Results:Normal" and the screenshot over SignalR. CI build (locked restore, Release, 0 warnings), 235 tests and frontend lint pass.
- [ ] Remove `PLAN.md`, then open the PR

## Decisions

**From you:**
- **Branching:** stacked on `scraper-architecture-stage-1` and rebased when it merges. One PR, one commit per phase, and every commit builds in Release and passes `dotnet test`.
- **Local dev:** when `SCRAPE_WORKER_URL` is unset, the app runs `ScraperService` in-process as it does today, and logs a startup warning if that happens inside a container.
- **Stuck scripts:** the worker is long-lived and keeps the compile cache. When an abandoned run hasn't ended within a grace period after its timeout, the worker exits and Docker restarts it.
- **Scope:** the worker, plus host filtering, the admin token, the Docker socket proxy, the env split and app container hardening.

**My calls (push back if any is wrong):**
- **The worker is an ASP.NET minimal API, not controllers.** Its endpoints are internal, and keeping them out of `SiteChecker.Backend.Controllers` keeps them out of `model.ts`.
- **The app keeps compiling scripts for validation** (`SiteValidator` → `IScriptCompiler.Validate`). Compiling never runs the script, so it's safe, and it keeps compile errors on save synchronous.
- **The app keeps choosing the browser.** `GetBrowserType` is pure config, and the runner needs it before scraping, to rotate the VPN under the scrape lock. It moves into a small `BrowserSelector` that both sides use. Only the worker gets `BROWSERLESS_TOKEN`.
- **Failure dumps are written by the app, not the worker.** The pipeline returns the page HTML (`ScrapeResult.PageHtml`), and `SiteCheckRunner.RecordOutcomeAsync` writes the dumps alongside the screenshot. The worker needs no volumes.
- **`ScrapeResult.Exception` becomes strings** (`ExceptionType` and `ExceptionDetail`), the same in-process and remote. An `Exception` doesn't serialize, and only the exception-type metadata, the dumps and the logs use it.
- **Script logs travel back with the result.** The worker captures the script's log entries (capped at 200 entries or 64 KB) in `ScrapeResult.ScriptLogs`, and the app re-logs them under `SiteChecker.Script` with today's scope. That way they still reach the app's OpenTelemetry pipeline even though the worker has no egress. Showing them in the Test Run UI is out of scope.
- **The admin token:**
  - It's set with `ADMIN_TOKEN`.
  - It gates `SiteController`'s create, update and test-run actions: everything that uploads or runs a script. Reads, deletes, Site Checks and the VPN API stay open.
  - It's required in Production, so startup fails without it, and optional in Development (gate off when unset).
  - Checked with `CryptographicOperations.FixedTimeEquals`.
  - The frontend keeps it in `localStorage`, sends it as `Authorization: Bearer`, and asks for it in a dialog on a 401.
- **`ALLOWED_HOSTS`:**
  - A semicolon-separated list that becomes `AllowedHosts`.
  - `localhost` is always added, so the container healthcheck keeps working.
  - It's required in Production, so startup fails while it's unset or `*`.
- **Docker access goes through `wollomatic/socket-proxy`, not a custom sidecar.** Once the throwaway container is gone (phase 2), all the app needs is:
  - `GET /containers/{name}/json` for the Browserless VPN container
  - `POST /containers/{name}/restart` for the two VPN containers

  Both are addressed **by name**. Addressing by ID would make the allowlist cover every container.
- **The PIA location list comes from `https://serverlist.piaservers.net/vpninfo/servers/v6`.** The first line of the response is JSON, and we keep the regions whose `servers.wg` is present. That's exactly what `wg-gen.sh -a` does, and it needs no credentials. The signature isn't verified, which matches today, since `-k` isn't passed.
- **The worker refuses new scrapes while it has an abandoned run.** It answers 503 and reports unhealthy, and the app waits for it to be healthy before each scrape, so a check doesn't land on a worker that's about to exit.

## New and changed projects

| Project | Notes |
| --- | --- |
| `src/ScrapeWorker` (new) | `Microsoft.NET.Sdk.Web`, root namespace `SiteChecker.ScrapeWorker`. References `Scraper`. Minimal API, console logging (plus OTLP when configured). |
| `src/Scraper` | Gains `ScrapeJson` (the shared `JsonSerializerOptions`), `BrowserSelector`, `IAbandonedRunMonitor`, and the serializable `ScrapeResult` fields. |
| `src/Backend` | Gains `RemoteScraperService`, admin token auth, host filtering, `PiaServerList`. Loses `Docker.DotNet`'s container create/logs usage. |
| `test/ScrapeWorker.IntegrationTests` (new) | `WebApplicationFactory` over the worker with a fake `IBrowserProvider` and real script compilation. |

Add both new projects to `SiteChecker.slnx`, and commit every new `packages.lock.json`.

## Phases

### 1. Host filtering and the admin token
**Backend:**
- Read `ALLOWED_HOSTS` and fold it into `AllowedHosts` with `localhost` added. Fail startup in Production when it's unset or `*`.
- `AdminTokenAuthenticationHandler`, a bearer scheme compared against `ADMIN_TOKEN` in constant time, plus an `Admin` policy.
- `[Authorize(Policy = "Admin")]` on `CreateSite`, `UpdateSite` and `StartTestRun`. Add `app.UseAuthentication()` before `UseAuthorization()`.
- In Production, startup fails without `ADMIN_TOKEN`. In Development, the policy allows everyone when it's unset, and logs that once.

**Frontend:**
- An `adminTokenInterceptor` next to `auth.interceptor.ts` adds the header when a token is stored.
- A 401 opens a small token dialog, stores the token and retries the request once.

**Tests:**
- In `SiteApiTests`, `SiteApiFactory` sets a token, and a test client without the header gets 401 on each gated action and 200 on the open ones.
- A wrong `Host` header gets 400.
- Production startup fails without `ADMIN_TOKEN` or `ALLOWED_HOSTS`.

**Docs:** add both variables to `docs/configuration.md` and `example.env`.

### 2. VPN without container creation
- `PiaServerList` (a typed `HttpClient`) fetches and parses the server list into `PiaLocation`s, then applies the US filter and shuffle. `DockerPiaContainers.ListLocationsAsync` delegates to it, or it moves out of `IPiaContainers` entirely, whichever reads better once it's done.
- `IsVpnRunningAsync` inspects the container by name, and `SetLocationAndRestartAsync` restarts by name. Delete `GetContainerAsync` and the container listing.
- `DockerPiaContainers` builds its client from `DOCKER_HOST` when set (e.g. `tcp://docker-proxy:2375`), and from the default socket otherwise.
- **Tests (`Backend.UnitTests`, with a fake HTTP handler like `PushoverChannelTests`):** parsing a captured server-list fixture (JSON line plus signature), regions without WireGuard, and a non-200 response.

### 3. Docker socket proxy, env split and app container hardening
**`docker-compose.yml`:**
- A `docker-proxy` service (`wollomatic/socket-proxy`, pinned version) on a new `docker` network with `internal: true`:
  - `-listenip=0.0.0.0`, `-allowfrom=site-checker`
  - `-allowGET=/v1\.[0-9]+/containers/site-checker-browserless-vpn/json`
  - `-allowPOST=/v1\.[0-9]+/containers/site-checker-(vpn|browserless-vpn)/restart`
  - `-watchdoginterval=3600 -stoponwatchdog`
  - `read_only`, `cap_drop: [ALL]`, `no-new-privileges`, `mem_limit: 64M`
- **App:**
  - Drop the socket mount. Set `DOCKER_HOST=tcp://docker-proxy:2375` and join the `docker` network.
  - Replace `env_file: .env` with an explicit `environment:` list of the variables the app reads (interpolated from `.env`). PIA credentials and `BROWSERLESS_TOKEN` (until phase 8, which moves it to the worker) stay off the app.
  - `vpn` gets `USER`/`PASS`/`LOCAL_NETWORK` explicitly instead of the whole `.env`.
- **App hardening:**
  - Set `USER $APP_UID` in the Dockerfile's final stage.
  - In Compose: `read_only: true`, a `tmpfs` at `/tmp`, `cap_drop: [ALL]`, `security_opt: [no-new-privileges:true]`.
  - Data, logs and `/pia` stay writable bind mounts.

**Verify (manual, recorded in the Progress notes):**
- `docker compose up` on a clean checkout.
- VPN rotation works through the proxy.
- `curl` from inside the app container can't list or create containers.
- Optional notifier variables passed as empty strings still read as "not configured". If they don't, use `${VAR:-}` and treat empty as unset.

**Docs (`docs/configuration.md`):** an upgrade note for the bind mounts. Host directories must be writable by UID 1654, the image's `app` user (`chown -R 1654 site-checker/`), or set `user:` to your own UID.

**Risk:** on Docker Desktop (macOS) the proxy's documented non-root `user: "65534:<docker gid>"` may not get access to the socket. If so, run it as root locally and note the difference in `docs/local-development.md`.

### 4. A serializable scrape contract
All in `src/Scraper`, still in-process, with no behavior change.

**`ScrapeResult`:**
- Replace `Exception` with `ExceptionType` and `ExceptionDetail` (strings), filled by `ScrapeResult.Unexpected(Exception)`.
- Add `PageHtml`. The pipeline captures it under the artifact budget for a Site Check's Unexpected Failure.
- Add `ScriptLogs`, a list of `ScriptLogEntry(Level, Message, Exception?)`.

**Pipeline and serialization:**
- `ScraperSpec`: `[JsonPolymorphic]`, with `[JsonDerivedType(typeof(ScriptSpec), "script")]`.
- `ScrapeJson.Options`: string enums, `Uri`, and polymorphism. A round-trip test covers each outcome.
- `FailureArtifacts.WriteAsync(ScrapeRequest, ScrapeResult)` no longer takes a page. `ScraperService` stops calling it, and `SiteCheckRunner.RecordOutcomeAsync` calls it.
- `ScriptExecutor` gives the script a logger that both forwards to the real logger and records into `ScriptLogs`, with the cap.

**`IScraperService`:**
- Gains `EvictScriptAsync(int siteId, CancellationToken)`. `SiteController` calls it instead of `ScriptCache.Evict`, and the in-process implementation calls the cache.
- `GetBrowserType` moves to `BrowserSelector`. `IScraperService` drops it, and the runner and `RunTestAsync` use the selector.

**Elsewhere:**
- `SiteCheckExtensions.Update` reads `ExceptionType`.
- Fix `TestRunResult.From` and the fakes (`FakeScraperService`, `RunnerHarness`).

**Tests:**
- Update `ScraperServiceTests` (the dump assertions move to a runner test), `ScriptExecutorTests` (log capture and cap) and `ScrapeResult` round trips.

### 5. Abandoned runs
- `IAbandonedRunMonitor.Track(Task run, ScrapeRequest request)` is called by `ScraperService` where `ObserveAbandoned` is today.
- The default `LoggingAbandonedRunMonitor` keeps today's behavior: it logs when the run ends. It's registered by `AddScraperServices()` with `TryAdd`, so the worker can replace it.
- `ExitingAbandonedRunMonitor` lives in `src/Scraper` so it's testable without the worker:
  - It reports `HasAbandonedRuns` while one is pending.
  - If a run hasn't ended within `AbandonedRunGrace` (15 s, on `TimeProvider`), it logs Critical and calls `IHostApplicationLifetime.StopApplication()`.
  - It arms `Environment.FailFast` as a backstop 10 s later. The backstop is behind an injectable `Action`, so tests don't kill the test host.
- **Tests (`Scraper.UnitTests`, `FakeTimeProvider`):**
  - a run ending within the grace period doesn't stop the app
  - a run that doesn't end does stop it
  - the backstop fires if stopping hangs

### 6. The Scrape Worker
`src/ScrapeWorker/Program.cs`:
- Registers `AddScraperServices()` and replaces the monitor with the exiting one.
- **`POST /scrape`:** takes a `ScrapeRequest` and returns a `ScrapeResult`, using `ScrapeJson.Options` and `HttpContext.RequestAborted`. It returns 503 while `HasAbandonedRuns`.
- **`DELETE /scripts/{siteId}`:** evicts that Site's script from the cache.
- **`GET /healthz`:** unhealthy while `HasAbandonedRuns`.
- Startup validation is the same as the app's `ValidateScraperServices`: timeouts plus compiler references.
- Logging: the console, plus OTLP if `OpenTelemetry:OtlpEndpoint` is set. Move `OpenTelemetryExtensions` from Backend to `Utilities` (or a shared hosting project, if that's cleaner) so both hosts use it.

**Tests (`test/ScrapeWorker.IntegrationTests`, with `WebApplicationFactory<SiteChecker.ScrapeWorker.Program>`, a fake `IBrowserProvider` and page, and real compilation of a small script):**
- success
- Known Failure with Requested Actions
- compile errors returning `Diagnostics`
- timeout
- 503 and an unhealthy health check while a run is abandoned
- eviction

### 7. RemoteScraperService
`RemoteScraperService : IScraperService` in Backend:
- A typed `HttpClient` with `BaseAddress = SCRAPE_WORKER_URL`.
- **Before each scrape:** poll `/healthz` until healthy, for up to 120 s (enough for a restart after an abandoned run). If it never becomes healthy, return the Unexpected Failure "The Scrape Worker isn't available".
- **The scrape call:** its timeout is the resolved Site timeout plus `ArtifactBudget` plus 25 s for closing the browser and transfer.
  - A timeout, a dropped connection or a 5xx becomes an Unexpected Failure ("The Scrape Worker stopped responding").
  - Cancelling the token aborts the request.
  - It never throws except `OperationCanceledException`, like the in-process contract.
- Re-logs `ScriptLogs` under `SiteChecker.Script` with the Site and Site Check scope.
- `EvictScriptAsync` is best effort: it logs a failure and doesn't throw.
- `AddScraperServices()` stays as-is. Backend's `Program` registers `RemoteScraperService` over it when `SCRAPE_WORKER_URL` is set. Otherwise it warns when `EnvironmentUtils.IsDockerContainer()`.

**Tests (`Backend.IntegrationTests`):** `RemoteScraperService` against the real worker from phase 6, through `WebApplicationFactory`'s handler (`CreateDefaultClient`), covering:
- each outcome round-tripping
- script logs re-logged
- worker unavailable, then available
- worker 503 until healthy
- the HTTP timeout
- cancellation

### 8. Worker container and networks
**Dockerfile:**
- One build stage, two final stages, `app` and `worker`. Both use `aspnet:10.0` with `USER $APP_UID`.
- The worker publishes `src/ScrapeWorker`, including the Playwright driver that `Microsoft.Playwright` ships.

**Compose:** a `scrape-worker` service with `build.target: worker`, and `app` with `build.target: app`. Its settings:
- no volumes, `read_only`, `tmpfs /tmp`, `cap_drop: [ALL]`, `no-new-privileges`
- `mem_limit: 1g`, `pids_limit: 256`, `cpus: 2`
- only `BROWSERLESS_URL`, `BROWSERLESS_URL_VPN`, `BROWSERLESS_TOKEN`, `SCRAPE_TIMEOUT` and `BROWSERLESS_TIMEOUT`
- a healthcheck on `/healthz` and `restart: unless-stopped`

**Networks:**

| Network | Members | Internet |
| --- | --- | --- |
| `default` | app, browserless, vpn | yes |
| `scrape` (`internal: true`) | app, scrape-worker, browserless, vpn | no |
| `docker` (`internal: true`) | app, docker-proxy | no |

- The app gets `SCRAPE_WORKER_URL=http://scrape-worker:8080` and loses `BROWSERLESS_TOKEN`.
- `browserless-vpn` shares `vpn`'s network namespace, so it joins `scrape` through `vpn`.

**Verify (manual):**
- Both demo Sites check successfully, with and without the VPN.
- A Test Run works.
- A script with `while (true) { }` times out, the worker restarts, and the next check succeeds.
- From inside the worker:
  - there's no route to the internet or the proxy
  - `env` shows no Pushover, Discord or PIA secrets
  - `/app/data` doesn't exist

**Risk:** the PIA container's firewall may drop traffic arriving on the second (`scrape`) interface. If it does:
- check `thrnz/docker-wireguard-pia`'s firewall settings for allowing the Docker network
- as a fallback, give the worker the `default` network as well, and record the lost egress restriction in the ADR

### 9. ADR, docs and end-to-end check
- **ADR 0006, "Scripts run in an isolated Scrape Worker":** supersedes 0004. Mark 0004 `Superseded by 0006`. It records:
  - what's isolated and what isn't (a script can still drive Browserless to reach the LAN and the app's API, which is why the admin token matters)
  - the exit-on-abandoned-run policy
  - the in-process development fallback
- **`CLAUDE.md`:** the project list, Scrapers and Sites (the worker, `RemoteScraperService`, dumps written by the runner), the trust boundary, Configuration, and Testing (the new test project).
- **`docs/configuration.md`:**
  - `SCRAPE_WORKER_URL`, `DOCKER_HOST`, `ADMIN_TOKEN`, `ALLOWED_HOSTS`
  - a rewritten trust boundary section
  - upgrade steps: chown, the new variables, `docker compose build`
- **`docs/local-development.md`:** in-process by default; how to run the worker locally (`dotnet run --project src/ScrapeWorker`, set `SCRAPE_WORKER_URL`).
- **`GLOSSARY.md`:** add **Scrape Worker**.
- **`example.env`:** the new variables.
- Final end-to-end pass on the full stack, recorded in Progress.
