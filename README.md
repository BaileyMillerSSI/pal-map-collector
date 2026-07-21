# Palmap Collector

Palmap Collector is the public, outbound-only companion for a Palworld dedicated
server. It polls Palworld's authenticated REST API on the private container or
LAN network, converts responses into a strict privacy-safe snapshot, and sends
only that allowlisted snapshot to a configured Palmap ingest endpoint.

The repository also owns `Palmap.Protocol`, the public v1 wire contract shared
with compatible ingest services. The hosted Palmap product is intentionally not
part of this repository.

## Security model

- The collector opens no listener and publishes no port.
- Palworld REST credentials stay local and are never included in snapshots.
- Player/account/platform identifiers, IP addresses, world GUIDs, RCON/REST
  configuration, upstream error bodies, and unknown fields are excluded.
- Player, guild, and base identifiers are HMAC-derived with an operator-owned
  privacy key. Use a unique random key for each server and keep it stable.
- HTTPS ingest is mandatory. Plain HTTP requires both the Development
  environment and the explicit `PALMAP_ALLOW_INSECURE_INGEST=true` opt-in.

Never expose Palworld's REST port publicly. Its credentials grant administrative
access to the game server.

## Configuration

| Variable | Required | Purpose |
| --- | --- | --- |
| `PALWORLD_API_URL` | yes | Private HTTP(S) origin of the Palworld REST API |
| `PALWORLD_ADMIN_USERNAME` | yes | Palworld REST Basic-auth username |
| `PALWORLD_ADMIN_PASSWORD` or `_FILE` | yes | Palworld REST password |
| `PALMAP_INGEST_URL` | yes | Complete snapshot ingest URL; no production domain is compiled in |
| `PALMAP_CLIENT_ID` | yes | Operator-visible Server/Client ID |
| `PALMAP_CLIENT_SECRET` or `_FILE` | yes | Collector credential secret |
| `PALMAP_PRIVACY_KEY` or `_FILE` | yes | Base64 value containing exactly 256 random bits |
| `PALMAP_ALLOW_INSECURE_INGEST` | no | Development-only opt-in for an HTTP ingest URL; default `false` |

Only one of a secret's direct variable and `_FILE` variant may be set. Secret
files are read at startup and trailing line endings are removed.

For local Aspire development from a remote Palworld machine, set:

```env
DOTNET_ENVIRONMENT=Development
PALMAP_INGEST_URL=http://192.0.2.10:5080/api/ingest/v1/snapshots
PALMAP_ALLOW_INSECURE_INGEST=true
```

`192.0.2.10` is a documentation address; replace it with the Aspire machine's
private LAN address. Never use the insecure flag outside an isolated development
network.

## Run with Compose

Copy the synthetic templates, replace every `replace-with-...` value, and keep
the resulting files untracked:

```powershell
Get-ChildItem ./config/*.env.example | ForEach-Object {
    Copy-Item $_.FullName ($_.FullName -replace '\.example$', '')
}
docker compose up -d --build --wait
docker compose ps
docker compose logs -f collector
```

The Compose topology publishes only Palworld's player-facing game ports. The
REST API stays on the private Compose network and the collector has no port.

## Delivery behavior

Player, world/metrics, and settings polling are independent. Each section keeps
its last good value and reports explicit healthy, delayed, or unavailable source
state. The delivery queue has capacity one: during an outage it retains only the
latest pending snapshot. Each attempted envelope is serialized once and retried
with bounded exponential backoff, jitter, `Retry-After` support, and a timeout.
Authentication (`401`) and compatibility (`426`) failures stop the worker so an
operator can correct configuration or upgrade it.

## Build and test

Use the .NET 10 SDK:

```powershell
dotnet restore Palmap.slnx
dotnet format Palmap.slnx --verify-no-changes --no-restore
dotnet build Palmap.slnx --configuration Release --no-restore --warnaserror
dotnet test Palmap.slnx --configuration Release --no-build --no-restore
dotnet pack Palmap.Protocol --configuration Release --no-build --no-restore
pwsh ./scripts/Test-Secrets.ps1
```

The protocol package includes the v1 JSON Schema and a synthetic fixture. The
planned initial public compatibility release is `Palmap.Protocol 1.0.0-rc.1`;
publishing is deliberately separate from normal CI.

## Releases

Container builds target `linux/amd64` and `linux/arm64`. Published images use an
immutable `sha-<commit>` tag. Version tags create matching semantic-version image
tags, and `latest` moves only for a stable version tag (never for `main` or a
prerelease). Public release builds attach SBOM/provenance and artifact
attestations. This repository does not publish from pull requests.

## License

Licensed under the [Apache License 2.0](LICENSE). See [NOTICE](NOTICE) for
attribution and the Palworld trademark disclaimer. See [SECURITY.md](SECURITY.md)
for private vulnerability reporting guidance.
