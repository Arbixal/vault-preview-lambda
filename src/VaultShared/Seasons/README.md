# Season Configuration Domain

This directory contains the API-owned season configuration model, validation rules, revision hashing, and lifecycle abstraction shared by the Lambda applications.

`InMemorySeasonConfigurationStore` is the deterministic reference implementation used by T03 tests. It is not a durable production store. A persistent implementation must satisfy `ISeasonConfigurationStore` before T08 wires the configuration into runtime endpoints or scheduled refreshes.

Revision hashes are deterministic lowercase SHA-256 values over the serialized `SeasonConfiguration`. Drafts are validated before storage; scheduled activation, immediate activation, and rollback all replace the active revision atomically within the store implementation.
