# Contributing

Thank you for helping improve Palmap Collector. Please open an issue before a
large change so that protocol compatibility, privacy, and operator impact can
be agreed before implementation.

## Development

Use the .NET 10 SDK. Before submitting a pull request, run:

```powershell
dotnet format Palmap.slnx --verify-no-changes
dotnet build Palmap.slnx --configuration Release --warnaserror
dotnet test Palmap.slnx --configuration Release --no-build
dotnet pack Palmap.Protocol --configuration Release --no-build
pwsh ./scripts/Test-Secrets.ps1
git diff --check
```

Never use real server responses, credentials, player identifiers, IP addresses,
or save data in tests and examples. Protocol changes must be additive and need
serialization, validation, schema, and synthetic-fixture coverage.

By contributing, you agree that your contribution is licensed under the
Apache License, Version 2.0.
