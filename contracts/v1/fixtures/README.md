# Vault Preview v1 Fixtures

These fixtures are API-owned examples for the `contracts/v1/openapi.yaml` contract. They are versioned with the contract and are intended to be consumed by both the API and frontend repositories.

## Consumption

- Use the fixture directory that matches the API contract version.
- Pin consumers to a repository commit or release tag; do not consume an unpinned branch in CI.
- Treat fixture files as read-only inputs. Changes to a fixture are contract changes and must be reviewed with the API schema.
- Use `manifest.json` to discover fixture names, response types, and intended coverage.
- API tests should load the files from the checked-out repository or a pinned artifact.
- Frontend tests should consume the same files through a pinned repository snapshot or synchronized contract artifact rather than maintaining a second copy.

The fixtures intentionally include an unknown activity kind and additive response data. Consumers must validate known fields and ignore unknown optional properties.
