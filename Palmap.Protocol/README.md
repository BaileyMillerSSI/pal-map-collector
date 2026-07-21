# Palmap.Protocol

`Palmap.Protocol` contains the public, versioned JSON contract shared by Palmap
collectors and compatible ingest services. It contains DTOs, strict serializer
settings, validation, JSON Schema, and synthetic fixtures only.

Use `SnapshotContractV1.Deserialize` at trust boundaries. Version 1 rejects
unknown JSON properties, non-finite numbers, invalid retained-state semantics,
invalid location variants, and values outside the documented limits.
