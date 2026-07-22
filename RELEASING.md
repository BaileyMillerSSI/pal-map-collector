# Releasing Palmap Collector

The collector image is built for both `linux/amd64` and `linux/arm64`. Every pull request and every push to `main` builds and runs the liveness endpoint under both architectures; the ARM64 smoke uses QEMU on the GitHub-hosted AMD64 runner.

## Image tags

- A successful push to `main` publishes only `sha-<full-git-sha>`. This tag is the immutable deployment input and never moves.
- A collector tag matching `vMAJOR.MINOR.PATCH[-prerelease]` publishes the corresponding `MAJOR.MINOR.PATCH[-prerelease]` multi-architecture image tag.
- `latest` moves only for a stable `vMAJOR.MINOR.PATCH` collector release. A prerelease never changes it.
- Pull requests, protocol tags, and manually dispatched workflows verify images locally and never publish them.

The existing legacy `latest` tag predates multi-architecture verification and must not be recommended, deleted, or repointed as part of an ordinary `main` build. Use a verified immutable SHA tag. Moving `latest` requires a separately authorized stable collector release after both architecture checks pass.

Published manifests include BuildKit SBOM and maximum-mode provenance attestations. The workflow also publishes a GitHub artifact attestation for the registry digest.

## Protocol package

Collector images and `Palmap.Protocol` have independent release versions. `vMAJOR.MINOR.PATCH[-prerelease]` is collector-only; `protocol-vMAJOR.MINOR.PATCH[-prerelease]` is package-only and never publishes or retags a collector image.

Until `Palmap.Protocol/Palmap.Protocol.csproj` exists, the protocol job reports a deliberate skip. Once it exists, every workflow run packs and validates the package, including its compiled assembly, v1 JSON Schema, and synthetic fixture.

A protocol package is published to NuGet.org only from an explicit `protocol-vMAJOR.MINOR.PATCH[-prerelease]` tag. Configure the `NUGET_API_KEY` Actions secret before creating that tag. A protocol-tagged run fails instead of silently omitting the package when the project exists but the key is unavailable.

## Collector release checklist

1. Confirm `main` is green and its immutable SHA image passed both architecture smoke tests.
2. Confirm the collector version is compatible with the currently deployed hosted ingest API.
3. Obtain explicit authorization before moving the legacy `latest` tag.
4. Create and push an annotated `vMAJOR.MINOR.PATCH` or `vMAJOR.MINOR.PATCH-prerelease` tag.
5. Verify the published image manifest lists both `linux/amd64` and `linux/arm64`, then verify its attestations.

For a protocol release, separately verify the package contract and create a `protocol-vMAJOR.MINOR.PATCH[-prerelease]` tag after confirming `NUGET_API_KEY` is configured.
