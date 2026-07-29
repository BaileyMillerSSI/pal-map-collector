# Palmap.Protocol

`Palmap.Protocol` contains the public, versioned JSON contract shared by Pal-Map
collectors and compatible ingest services. It contains DTOs, strict serializer
settings, validation, JSON Schema, and synthetic fixtures only.

`PalmapIngress.DefaultBaseUrl` exposes the standard hosted ingress base URL,
`https://pal-map.com`, so compatible collectors and hosted provisioning tools
share the same production default.

Use `SnapshotContractV1.Deserialize` at trust boundaries. Version 1 rejects
unknown JSON properties, non-finite numbers, invalid retained-state semantics,
invalid location variants, and values outside the documented limits.
