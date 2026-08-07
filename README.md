# Pal-Map Collector

Pal-Map Collector polls the authenticated REST API exposed by a Palworld dedicated server. It reads player locations, world actor snapshots, and server settings on independent schedules, sanitizes them into the public Pal-Map snapshot v1 contract, and delivers the latest snapshot to a configured Pal-Map ingest endpoint.

The service targets .NET 10, emits structured console logs, and exposes separate liveness and Palworld-dependent readiness checks. Product data flows outbound only; the health listener should remain private or loopback-bound.

## Brand and technical identifiers

**Pal-Map** is the customer-facing product name. Existing technical identifiers
retain their compatibility spelling, including `Palmap.*` projects and
namespaces, the `Palmap.Protocol` package, `PalmapIngest` configuration keys,
`PALMAP_*` environment variables, schema filenames, and container repository
names. These identifiers should not be used as visible brand copy.

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

Compose reads the ignored `config/*.env` copies, while only `*.env.example` templates are tracked. The templates deliberately contain local-only demonstration credentials, a documentation-only LAN ingest override, and a public, non-secret privacy-key placeholder. Change the Palworld password, provision a Pal-Map client pair, and generate a unique privacy key before adapting the sample for a real server. Production collectors use `https://pal-map.com/api/ingest/v1/snapshots` automatically; only local Development testing should retain an endpoint override.

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

### Use the published collector image

For the simplest installation, use the rolling multi-architecture image:

```yaml
services:
  collector:
    image: ghcr.io/baileymillerssi/pal-map-collector:latest
```

The same image reference supports both `linux/amd64` and `linux/arm64`; Docker selects the matching architecture automatically. During pre-beta, `latest` follows the newest successful `main` build and can include breaking changes. Pin the corresponding immutable `sha-<full-git-sha>` tag when reproducible deployments and deliberate upgrades are more important than automatically receiving the newest build.

## Local .NET development

Start a Palworld server with its REST API enabled, then override the checked-in development values through environment variables or user secrets:

```powershell
$env:PalworldApi__BaseUrl = "http://127.0.0.1:8212"
$env:PalworldApi__Admin__Username = "admin"
$env:PalworldApi__Admin__Password = "your-admin-password"
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
| `LogLevel` | `Information` | Console verbosity: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, or `None` |
| `PalworldApi:BaseUrl` | `http://localhost:8212` | Palworld REST origin, including TCP port 8212 |
| `PalworldApi:Admin:Username` | `admin` | Palworld's REST Basic-auth username |
| `PalworldApi:Admin:Password` | none | REST admin password; required at startup |
| `PalmapIngest:Endpoint` | `https://pal-map.com/api/ingest/v1/snapshots` | Hosted Pal-Map snapshot v1 ingest URL; override only for explicit local Development testing |
| `PalmapIngest:ClientId` | none | Issued 20-to-64-character Server/Client ID: `pmc_` plus 16 to 60 base64url characters |
| `PalmapIngest:ClientSecret` | none | Issued base64url client secret used for HTTP Basic authentication |
| `PalmapIngest:PrivacyKey` | none | Unique 32-byte key encoded as base64; used only to derive opaque identifiers |
| `PalmapIngest:AllowInsecureHttp` | `false` | Permit an endpoint override only when the process also runs in `Development` |
| `PalmapIngest:RequestTimeoutMs` | `20000` | Timeout for one ingest request |
| `PalmapIngest:MaximumDeliveryAttempts` | `5` | Bounded attempts for one stable snapshot body |
| `PalmapIngest:MaximumRetryDelayMs` | `60000` | Maximum retry and server-requested backoff delay |
| `PalmapIngest:SuppressIdleSnapshots` | `true` | After one accepted complete healthy-empty snapshot, suppress equivalent routine reports while local polling continues; set `false` only to opt out |
| `PalmapIngest:IdleSnapshotHeartbeatIntervalMs` | `21600000` | Send a new healthy-empty baseline at least every six hours while idle suppression is enabled |
| `Collector:PlayerLocationUpdateIntervalMs` | `5000` | Player polling period |
| `Collector:GameDataUpdateIntervalMs` | `30000` | World actor snapshot polling period |
| `Collector:StageRefreshDistance` | `50000` | Movement distance that triggers an immediate world refresh to re-check instanced locations |
| `Collector:ServerSettingsUpdateIntervalMs` | `3600000` | Server settings polling period |
| `Collector:FailureRetryIntervalMs` | `5000` | Retry period after an unavailable server or failed report |
| `Collector:PalworldHealthCacheDurationMs` | `5000` | Shared health-probe cache duration |

Endpoint overrides must be absolute HTTP or HTTPS URLs and cannot contain user information, a query, or a fragment. Any value other than the hosted default requires both the `Development` environment and `PalmapIngest:AllowInsecureHttp=true`, even when the override itself uses HTTPS. All intervals must be between 1 and `2147483647` milliseconds. The Palworld password, Pal-Map client secret, and privacy key have no real checked-in defaults; missing or malformed configuration stops the process during startup with an options-validation error.

Reporter loops update retained sanitized state without waiting for network delivery. The delivery worker sends one stable serialized envelope per attempt sequence, honors bounded `Retry-After` values, and retains only the latest pending snapshot during outages. Authentication and protocol-compatibility failures stop the collector; rejected payloads and exhausted transient retries move on to the latest available state. Raw player, account, platform, network, and Palworld error data are neither included in the public contract nor written to delivery logs.

Idle snapshot suppression is enabled by default for the hosted Pal-Map service. A collector first sends a complete healthy-empty baseline and waits for `202 Accepted`; it then suppresses timestamp, sequence, world-refresh, and unchanged settings churn while continuing every local poll and health check. Players, source degradation or recovery, changed public server settings, process restart, or the heartbeat deadline resume delivery immediately. A rejected, timed-out, or failed baseline never enables silence. The six-hour default trades fewer hosted wakeups for up to a six-hour quiet-versus-dead detection window. Operators using a deliberate Development endpoint that does not model an accepted healthy-empty snapshot as quiet for at least that window can opt out with `PalmapIngest__SuppressIdleSnapshots=false`.

All reporters share one singleton Palworld health gate. It coalesces and briefly caches probes, prevents reporting calls while REST is unavailable, and releases reporters immediately when the server becomes healthy. The singleton retains only health state; each probe and report uses a short-lived factory client so DNS and handler rotation continue to work. A failed HTTP report invalidates the cached state and retries after `FailureRetryIntervalMs`, rather than waiting for the report's normal interval. Teleport-scale player movement wakes the existing game-data reporter immediately; requests are coalesced, and revision checks prevent an older in-flight world response from clearing a newer location transition.

The Palworld container needs these settings for complete coverage:

```env
REST_API_ENABLED=true
REST_API_PORT=8212
ENABLE_GAMEDATA_API=true
SHOW_PLAYER_LIST=true
```

### Logging

The collector writes structured text to stdout. Set the top-level `LogLevel` in configuration or as an environment variable:

```powershell
$env:LogLevel = "Debug"
```

`Information` is the normal operator level. It shows the concise startup and shutdown messages plus important state changes, such as Palworld or hosted delivery becoming unavailable and recovering. `Debug` and `Trace` add successful polling, delivery, retry, and health-request detail for troubleshooting. `Warning` reports degraded behavior or rejected data that may make the hosted map stale. `Error` and `Critical` are reserved for operations that cannot continue without intervention. `None` disables console logging.

Environment-variable changes require a process restart. Invalid values stop the collector with a configuration error. Credentials, authorization headers, raw upstream responses, private identifiers, player data, and server addresses are never written to logs.

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

The container job builds and runs `/health/live` for both `linux/amd64` and `linux/arm64` on pull requests and pushes to `main`. ARM64 is exercised through QEMU on the GitHub-hosted runner, and pull requests never publish images.

After those checks pass on a push to `main`, the workflow publishes one multi-architecture image to `ghcr.io/<owner>/<repository>` with both the rolling `latest` tag and the immutable `sha-<full-commit-sha>` tag. A failed architecture build or liveness check prevents publication, so it cannot move `latest`. Collector release tags publish matching semantic image tags; stable releases also update `latest`, while prereleases do not. Published images include OCI metadata, SBOM/provenance data, and a GitHub artifact attestation. See [RELEASING.md](RELEASING.md) for the exact collector image and independent `Palmap.Protocol` release policies.

No registry secret is required. The workflow grants `packages: write` only to the container job. Repository or organization policy must allow GitHub Actions to create and write packages; package visibility and access can then be managed from the package settings in GitHub.

## Troubleshooting

- `docker compose up --wait` may take several minutes on first boot while Steam downloads Palworld. Follow progress with `docker compose logs -f palworld`.
- A healthy Palworld process with an unhealthy collector readiness endpoint usually indicates a URL or admin-password mismatch. The password must match in the copied `server.env` and `collector.env` files.
- A failing `/game-data` request usually means `ENABLE_GAMEDATA_API=true` was not applied before the Palworld server started.
- If port 8212 or 8080 is already occupied, change the host side of the loopback port mapping and set the corresponding integration-test URL. The collector-to-Palworld URL inside Compose remains `http://palworld:8212`.
- Repeated delivery retries usually indicate an unreachable ingest URL or an unavailable hosted API. Any endpoint override is accepted only when both `DOTNET_ENVIRONMENT=Development` and `PalmapIngest__AllowInsecureHttp=true`; production uses the hosted HTTPS default.
