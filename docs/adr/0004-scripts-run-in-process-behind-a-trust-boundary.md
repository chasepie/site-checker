# Script Scrapers run in-process and unsandboxed, behind a network trust boundary

> Accepted in [design 0001](../design/0001-scraper-architecture.md); lands with its stage 1.

Script Scrapers are C# files compiled with `CSharpCompilation` and loaded into a collectible `AssemblyLoadContext` inside the app. They aren't sandboxed and the app has no authentication, so anyone who can reach the API can run code on the host: the app mounts the Docker socket for VPN rotation, and `:ro` doesn't restrict API calls on a socket. We accepted this because SiteChecker is single-user and self-hosted, so the script author is the person running the app. The app must only be reachable from networks where everyone is trusted (today the home LAN and a private Tailscale VPN), and never from the internet, even behind a reverse proxy, unless that proxy requires a login.

## Considered Options

- **Roslyn scripting (`CSharpScript`).** Less ceremony, but it loads every compiled script into the default load context, which never unloads, so each edit would leak an assembly until restart.
- **A separate, killable process per script.** Would let a timeout stop a script stuck in a synchronous loop. Rejected as too much machinery for stage 1.

## Consequences

- .NET can't kill a thread. A script stuck in a synchronous loop keeps a thread busy, and its load context loaded, until the app restarts. Other checks still run, because the pipeline enforces the timeout with `WaitAsync` and closes the browser.
- Before any wider exposure, revisit this: gate script upload and Test Runs behind an admin token, or add a login for the whole app.
