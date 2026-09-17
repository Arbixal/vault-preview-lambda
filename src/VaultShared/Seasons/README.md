# Season Configuration Domain

This directory contains the API-owned season configuration model, validation rules, revision hashing, and lifecycle abstraction shared by the Lambda applications.

`InMemorySeasonConfigurationStore` is the deterministic reference implementation used by T03 tests. It is not a durable production store. Runtime Lambdas use the S3-backed `ISeasonRevisionProvider` implementation from `Infrastructure.VaultCache`, which reads immutable revision documents and an active pointer from the shared cache bucket.

Revision hashes are deterministic lowercase SHA-256 values over the serialized `SeasonConfiguration`. Drafts are validated before storage; scheduled activation, immediate activation, and rollback all replace the active revision atomically within the store implementation.
