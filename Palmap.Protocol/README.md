# Palmap.Protocol

`Palmap.Protocol` contains the public, versioned JSON contract shared by the
Palmap collector and hosted ingest API. It contains DTOs, strict serializer
settings, validation, JSON Schema, and synthetic fixtures only.

Use `SnapshotContractV1.Deserialize` at trust boundaries. Version 1 rejects
unknown JSON properties, non-finite numbers, malformed section state, invalid
location variants, and values outside the documented limits.
