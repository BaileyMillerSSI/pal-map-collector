# Releasing Pal-Map Collector

The collector image is built for both `linux/amd64` and `linux/arm64`. Every pull request and every push to `main` builds and runs the liveness endpoint under both architectures; the ARM64 smoke uses QEMU on the GitHub-hosted AMD64 runner.

## Image tags

- A successful push to `main` publishes one multi-architecture manifest with both `latest` and `sha-<full-git-sha>`. The SHA tag never moves.
- A collector tag matching `vMAJOR.MINOR.PATCH[-prerelease]` publishes the corresponding `MAJOR.MINOR.PATCH[-prerelease]` multi-architecture image tag.
- `latest` follows the newest successful `main` build during pre-beta. A stable collector release also moves it; a prerelease does not.
- Pull requests, protocol tags, and manually dispatched workflows verify images locally and never publish them.

Both architecture builds and liveness checks complete before the publish step. A failed check therefore cannot move `latest` or create an immutable SHA tag. `latest` is the simplest installation reference, but it can include breaking changes during pre-beta; use the SHA tag for reproducible deployments and rollbacks.

Published manifests include BuildKit SBOM and maximum-mode provenance attestations. The workflow also publishes a GitHub artifact attestation for the registry digest.

## Protocol package

Collector images and `Palmap.Protocol` have independent release versions. `vMAJOR.MINOR.PATCH[-prerelease]` is collector-only; `protocol-vMAJOR.MINOR.PATCH[-prerelease]` is package-only and never publishes or retags a collector image.

Until `Palmap.Protocol/Palmap.Protocol.csproj` exists, the protocol job reports a deliberate skip. Once it exists, every workflow run packs and validates the package, including its compiled assembly, v1 JSON Schema, and synthetic fixture.

A protocol package is published to NuGet.org only from an explicit `protocol-vMAJOR.MINOR.PATCH[-prerelease]` tag. Publishing uses NuGet.org trusted publishing: the release job exchanges GitHub's OIDC token for a short-lived API key and no long-lived NuGet credential is stored in GitHub.

Before creating the first Protocol tag:

1. In the owning NuGet.org account, create a trusted-publishing policy for GitHub owner `BaileyMillerSSI`, repository `pal-map-collector`, and workflow file `ci.yml`. Leave the environment empty because this workflow does not use a GitHub environment.
2. Set the repository Actions variable `NUGET_USER` to the NuGet.org profile name (not an email address).

A protocol-tagged run fails instead of silently omitting the package when the variable or matching trusted-publishing policy is unavailable.

## Collector release checklist

1. Confirm `main` is green and its `latest` and immutable SHA tags resolve to the same manifest.
2. Confirm the collector version is compatible with the currently deployed hosted ingest API.
3. Create and push an annotated `vMAJOR.MINOR.PATCH` or `vMAJOR.MINOR.PATCH-prerelease` tag.
4. Verify the published image manifest lists both `linux/amd64` and `linux/arm64`, then verify its attestations.

For a protocol release, separately verify the package contract and create a `protocol-vMAJOR.MINOR.PATCH[-prerelease]` tag after confirming the NuGet.org trusted-publishing policy and `NUGET_USER` repository variable are configured.
