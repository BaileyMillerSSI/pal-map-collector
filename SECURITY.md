# Security Policy

## Reporting a vulnerability

Do not disclose vulnerabilities, credentials, tenant data, or player data in a
public issue. Use GitHub's private security-advisory reporting for this
repository. Include only the minimum synthetic reproduction needed.

Maintainers will acknowledge a report as soon as practical, investigate its
impact, and coordinate remediation and disclosure with the reporter.

## Supported versions

Security fixes are provided for the newest stable collector release. During
the release-candidate period, only the newest release candidate is supported.

## Security boundary

The collector reads Palworld's administrative REST API only on the operator's
private network and sends an allowlisted snapshot outbound to Palmap. Never
publish the Palworld REST port. Production ingest must use HTTPS. Palworld
credentials, raw account/platform identifiers, IP addresses, and upstream
error bodies must never be transmitted or logged.
