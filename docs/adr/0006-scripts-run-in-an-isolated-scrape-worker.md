# Scripts run in an isolated Scrape Worker

> Supersedes [ADR 0004](0004-scripts-run-in-process-behind-a-trust-boundary.md).
>
> **Amended by [ADR 0007](0007-a-single-user-login-with-cookie-sessions.md):** a login now replaces the admin token below, and covers the whole app.

Script Scrapers run in the **Scrape Worker**, a container of their own, not in the app. The app sends each scrape over HTTP (`RemoteScraperService`, `SCRAPE_WORKER_URL`) and gets the `ScrapeResult` back. The pipeline writes nothing and the result carries everything, including the page's HTML and the Scraper's log. The worker:
- holds no secrets but the Browserless token and its own secret (below)
- mounts nothing and can't reach Docker
- runs non-root on a read-only filesystem, with memory and process limits
- sits on an internal network shared only with the app and the browsers, so it reaches the internet only through them
- only takes requests carrying a secret shared with the app (`SCRAPE_WORKER_SECRET`) and addressed to its own host name, since the browsers on its network load untrusted pages

When a run abandoned at its timeout is still going 15 s later, the worker stops and Docker restarts it, which frees the stuck thread.

Several measures around the worker narrow what reaching the app is worth:
- An admin token (`ADMIN_TOKEN`) gates saving a Site and starting a Test Run, the two ways to upload or run a script.
- `ALLOWED_HOSTS` rejects other Host headers, so a web page can't reach the API by DNS rebinding.
- The app reaches Docker only through a socket proxy that allows inspecting the Browserless VPN container and restarting the two VPN containers, by name. Listing VPN Locations now reads PIA's public server list over HTTP, so nothing creates containers.
- The app runs non-root on a read-only filesystem and gets only the environment variables it reads.

We changed this because ADR 0004's only boundary was the network. Anyone who could reach the app, including a web page through DNS rebinding, could run code with the app's secrets, its database and the Docker socket, which meant root on the host.

## Considered Options

- **Restricting scripts at compile time** (banning `System.IO`, `Process` and similar). Not a boundary: reflection and native calls get around it.
- **A process per scrape.** Kills a stuck script cleanly, but loses the compile cache and pays Roslyn's cold start, several seconds, on every check. A long-lived worker that exits on a stuck run gets the same result for the rare case.
- **A general-purpose Docker socket proxy that allows whole categories of call.** It would still allow creating containers, which is as good as root on the host. Once container creation was gone, a custom sidecar API would have been equivalent to an allowlisting proxy, but more code to maintain.

## Consequences

- A script still controls a browser that can reach the LAN and the app's API. The admin token keeps it from uploading scripts. It can still use the API's open parts, such as reading Sites and Site Checks, queuing checks and changing the VPN Location.
- Anyone with the admin token runs code in the worker, not on the host.
- Without `SCRAPE_WORKER_URL`, as in local development, the app scrapes in-process exactly as under ADR 0004, and warns at startup if it's in a container.
- A worker restart drops its compile cache, so each Site's script compiles again on its next check. Checks queued during the restart wait for the worker, up to 120 s, instead of failing.
- Docker calls the app makes must also be added to the proxy's allowlist in `docker-compose.yml`.
