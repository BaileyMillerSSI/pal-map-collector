# Palmap Collector

Palmap Collector polls the authenticated REST API exposed by a Palworld dedicated server. It reads player locations, world actor snapshots, and server settings on independent schedules, sanitizes them into the public Palmap snapshot v1 contract, and delivers the latest snapshot to a configured Palmap ingest endpoint.

The service targets .NET 10, emits structured console logs through Serilog, and exposes separate liveness and Palworld-dependent readiness checks. Product data flows outbound only; the health listener should remain private or loopback-bound.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Docker Engine with Docker Compose for the sample and integration tests
- At least 16 GB of available memory for the Palworld server; 32 GB is recommended for sustained use
- Enough storage for the Palworld server download and world data

The first Palworld container start downloads the dedicated server and can take several minutes. Its health check has a five-minute startup grace period for this reason.

## Quick start with Docker Compose

The included [compose.yaml](compose.yaml) builds the collector and starts it alongside `thijsvanloef/palworld-server-docker:latest`:

```powershell
Get-ChildItem ./config/*.env.example | ForEach-Object {
    Copy-Item $_.FullName ($_.FullName -replace '\.example$', '')
}
docker compose up -d --build --wait
docker compose ps
docker compose logs -f collector
```

Compose reads the ignored `config/*.env` copies, while only `*.env.example` templates are tracked. The templates deliberately contain local-only demonstration credentials and a public, non-secret privacy-key placeholder. Change the Palworld password, provision a Palmap client pair, and generate a unique privacy key before adapting the sample for a real server.

After both services are healthy:

```powershell
Invoke-WebRequest http://127.0.0.1:8080/health/live
Invoke-WebRequest http://127.0.0.1:8080/health/ready
```

Stop the stack with:

```powershell
docker compose down
```

World data remains in `./palworld`. REST port 8212 and the collector port are bound to host loopback only. Do not publicly expose the Palworld REST API: its credentials grant administrative access.

## Local .NET development

Start a Palworld server with its REST API enabled, then override the checked-in development values through environment variables or user secrets:

```powershell
$env:PalworldApi__BaseUrl = "http://127.0.0.1:8212"
$env:PalworldApi__Admin__Username = "admin"
$env:PalworldApi__Admin__Password = "your-admin-password"
$env:PalmapIngest__Endpoint = "https://palmap.example/api/ingest/v1/snapshots"
$env:PalmapIngest__ClientId = "your-issued-client-id"
$env:PalmapIngest__ClientSecret = "your-issued-client-secret"
$env:PalmapIngest__PrivacyKey = "your-base64-encoded-32-byte-privacy-key"
dotnet run --project Palmap.Collector
```

The default local HTTP address is listed by `dotnet run` from `launchSettings.json`. `/health/live` succeeds when the collector process is responsive; `/health/ready` succeeds only after an authenticated request to Palworld `/v1/api/info` succeeds.

## Configuration

.NET configuration hierarchy is used throughout. JSON keys use `:` and environment variables use `__`:

| Setting | Default | Purpose |
| --- | ---: | --- |
| `PalworldApi:BaseUrl` | `http://localhost:8212` | Palworld REST origin, including TCP port 8212 |
| `PalworldApi:Admin:Username` | `admin` | Palworld's REST Basic-auth username |
| `PalworldApi:Admin:Password` | none | REST admin password; required at startup |
| `PalmapIngest:Endpoint` | none | Absolute Palmap snapshot v1 ingest URL; required at startup |
| `PalmapIngest:ClientId` | none | Issued Server/Client ID used for HTTP Basic authentication |
| `PalmapIngest:ClientSecret` | none | Issued client secret used for HTTP Basic authentication |
| `PalmapIngest:PrivacyKey` | none | Unique 32-byte key encoded as base64; used only to derive opaque identifiers |
| `PalmapIngest:AllowInsecureHttp` | `false` | Permit HTTP only when the process also runs in `Development` |
| `PalmapIngest:RequestTimeoutMs` | `20000` | Timeout for one ingest request |
| `PalmapIngest:MaximumDeliveryAttempts` | `5` | Bounded attempts for one stable snapshot body |
| `PalmapIngest:MaximumRetryDelayMs` | `60000` | Maximum retry and server-requested backoff delay |
| `Collector:PlayerLocationUpdateIntervalMs` | `5000` | Player polling period |
| `Collector:GameDataUpdateIntervalMs` | `30000` | World actor snapshot polling period |
| `Collector:ServerSettingsUpdateIntervalMs` | `3600000` | Server settings polling period |
| `Collector:FailureRetryIntervalMs` | `5000` | Retry period after an unavailable server or failed report |
| `Collector:PalworldHealthCacheDurationMs` | `5000` | Shared health-probe cache duration |

URLs must be absolute HTTP or HTTPS URLs. The ingest URL must use HTTPS unless both the environment is `Development` and `PalmapIngest:AllowInsecureHttp` is `true`. All intervals must be between 1 and `2147483647` milliseconds. The Palworld password, Palmap client secret, and privacy key have no real checked-in defaults; missing or malformed configuration stops the process during startup with an options-validation error.

Reporter loops update retained sanitized state without waiting for network delivery. The delivery worker sends one stable serialized envelope per attempt sequence, honors bounded `Retry-After` values, and retains only the latest pending snapshot during outages. Authentication and protocol-compatibility failures stop the collector; rejected payloads and exhausted transient retries move on to the latest available state. Raw player, account, platform, network, and Palworld error data are neither included in the public contract nor written to delivery logs.

All reporters share one singleton Palworld health gate. It coalesces and briefly caches probes, prevents reporting calls while REST is unavailable, and releases reporters immediately when the server becomes healthy. The singleton retains only health state; each probe and report uses a short-lived factory client so DNS and handler rotation continue to work. A failed HTTP report invalidates the cached state and retries after `FailureRetryIntervalMs`, rather than waiting for the report's normal interval.

The Palworld container needs these settings for complete coverage:

```env
REST_API_ENABLED=true
REST_API_PORT=8212
ENABLE_GAMEDATA_API=true
SHOW_PLAYER_LIST=true
```

### Logging

Serilog writes structured text to stdout. Control its default or category levels through `appsettings.json` or environment variables:

```powershell
$env:Serilog__MinimumLevel__Default = "Debug"
$env:Serilog__MinimumLevel__Override__Microsoft = "Warning"
```

Supported level names include `Verbose`, `Debug`, `Information`, `Warning`, `Error`, and `Fatal`. Environment-variable changes require a process restart. Credentials and authorization headers are never written to logs.

## Build and test

Build the complete solution and run the normal test suite:

```powershell
dotnet build Palmap.slnx
dotnet test Palmap.slnx
```

Integration tests are part of the solution but skip automatically unless explicitly enabled, so the normal command does not require Docker. Collect unit-test coverage with:

```powershell
dotnet test Palmap.UnitTests --collect:"Code Coverage;Format=cobertura" --results-directory TestResults
```

### Compose integration tests

Start the sample, wait for authenticated REST readiness, and run the live suite:

```powershell
docker compose up -d --build --wait
$env:PALMAP_RUN_INTEGRATION_TESTS = "true"
$env:PALMAP_PALWORLD_ADMIN_PASSWORD = "palmap-integration"
dotnet test Palmap.IntegrationTests
docker compose down
```

Optional integration-test overrides are `PALMAP_PALWORLD_URL` (default `http://127.0.0.1:8212`) and `PALMAP_COLLECTOR_URL` (default `http://127.0.0.1:8080`).

The live suite checks server info, players, settings, world actor data, metrics, rejected credentials, and both collector health endpoints.

## Continuous integration and delivery

The `CI/CD` GitHub Actions workflow runs for pull requests targeting `main`. It verifies formatting, restores and builds the full solution in Release mode with warnings treated as errors, runs the normal test suite, uploads TRX test results, and builds the production container without publishing it. Configure both `.NET build and test` and `Container build / publish` as required branch-protection checks for `main`.

After the same checks pass on a push to `main`, the workflow publishes the image to `ghcr.io/<owner>/<repository>` using the repository's built-in `GITHUB_TOKEN`. Each image receives the tags `main`, `latest`, and `sha-<full-commit-sha>`. The SHA tag is immutable deployment input; `main` and `latest` track the newest successful main-branch build. Published images also include OCI metadata, SBOM/provenance data, and a GitHub artifact attestation.

No registry secret is required. The workflow grants `packages: write` only to the container job. Repository or organization policy must allow GitHub Actions to create and write packages; package visibility and access can then be managed from the package settings in GitHub.

## Troubleshooting

- `docker compose up --wait` may take several minutes on first boot while Steam downloads Palworld. Follow progress with `docker compose logs -f palworld`.
- A healthy Palworld process with an unhealthy collector readiness endpoint usually indicates a URL or admin-password mismatch. The password must match in the copied `server.env` and `collector.env` files.
- A failing `/game-data` request usually means `ENABLE_GAMEDATA_API=true` was not applied before the Palworld server started.
- If port 8212 or 8080 is already occupied, change the host side of the loopback port mapping and set the corresponding integration-test URL. The collector-to-Palworld URL inside Compose remains `http://palworld:8212`.
- The no-op collector sink is expected to log collection summaries without transmitting data. Implementing a remote Palmap backend requires its endpoint and payload contract.
