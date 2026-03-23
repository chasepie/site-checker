# Local Development

## VS Code Launch Configurations

Two VS Code launch configurations are available:

- **Launch App with Containers** — Automatically starts the Browserless and VPN Docker containers, then launches the backend. Use this when developing against containerized browser services. Set the following in your `.env` file to point at the locally running containers:
  ```
  BROWSERLESS_URL=ws://localhost:3000
  BROWSERLESS_URL_VPN=ws://localhost:3001
  ```
- **Launch App and Playwright** — Starts a local Playwright server alongside the backend. Use this for a fully local setup without Docker.

Both configurations will launch the Angular dev server and open the app in Chrome automatically.

## Connecting to the VPN Container Locally

When debugging locally with the **Launch App with Containers** configuration, connections to the VPN-routed Browserless instance (`localhost:3001`) will fail with a `ECONNRESET` error by default. This happens because the `thrnz/docker-wireguard-pia` container's firewall drops all inbound traffic that doesn't originate from a whitelisted network.

To fix this, set `LOCAL_NETWORK` in your `.env` file to the CIDR of the network your host traffic appears to come from **inside the container**. This varies by platform:

| Platform                 | Typical value                                   | Why                                                   |
| ------------------------ | ----------------------------------------------- | ----------------------------------------------------- |
| macOS (Docker Desktop)   | `192.168.65.0/24`                               | Traffic is proxied through the Docker VM gateway      |
| Windows (Docker Desktop) | `192.168.65.0/24`                               | Same VM-based architecture as macOS                   |
| Linux (native Docker)    | The Compose bridge subnet, e.g. `172.19.0.0/16` | No VM; traffic comes from the Docker network directly |

To determine the exact value for your machine:

- **Docker Desktop (macOS/Windows):** Open **Docker Desktop → Settings → Resources → Network** and note the Docker subnet, or run:
  ```bash
  docker run --rm alpine sh -c "getent hosts host.docker.internal" | awk '{print $1}'
  ```
  Use the `/24` subnet of the returned IP (e.g. `192.168.65.254` → `192.168.65.0/24`).

- **Linux (native Docker):** Check the Compose network subnet:
  ```bash
  docker network inspect $(docker network ls --filter name=site-checker -q) --format '{{range .IPAM.Config}}{{.Subnet}}{{end}}'
  ```

Add the value to your `.env`:
```
LOCAL_NETWORK=192.168.65.0/24
```

Then recreate the VPN container to apply:
```bash
docker compose up -d --force-recreate vpn
```

## Running Manually

### Backend

```bash
# Build the solution
dotnet build

# Run the backend (requires Docker services or local Playwright running)
cd src/Backend
dotnet run
```

### Frontend

```bash
cd src/Frontend

# Install dependencies
npm install

# Run development server
npm start
```

## Database

The application uses **SQLite** with Entity Framework Core. Migrations run automatically on startup.

```bash
# Create a new migration (run from src/Database/)
cd src/Database
dotnet tool run dotnet-ef migrations add MigrationName

# Apply migrations manually
dotnet tool run dotnet-ef database update
```

Database files are stored in `site-checker/data/` and mounted as a Docker volume.
