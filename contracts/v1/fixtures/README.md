# Vault Preview v1 Fixtures

These fixtures are API-owned examples for the `contracts/v1/openapi.yaml` contract. They are versioned with the contract and are intended to be consumed by both the API and frontend repositories.

## Consumption

- Use the fixture directory that matches the API contract version.
- Pin consumers to a repository commit or release tag; do not consume an unpinned branch in CI.
- Treat fixture files as read-only inputs. Changes to a fixture are contract changes and must be reviewed with the API schema.
- Use `manifest.json` to discover fixture names, response types, and intended coverage.
- API tests should load the files from the checked-out repository or a pinned artifact.
- Frontend tests should consume the same files through the pinned fetch command below rather than maintaining a second copy.

## Pinned Fetch

The canonical consumption mechanism is a commit-pinned archive download from this repository. The frontend repository should pin the API commit in its CI configuration and run:

```bash
API_CONTRACT_REF="<api-commit-sha>"
curl --fail --silent --show-error --location \
  "https://raw.githubusercontent.com/Arbixal/vault-preview-lambda/${API_CONTRACT_REF}/contracts/v1/fixtures/fetch.sh" \
  | bash -s -- "${API_CONTRACT_REF}" "test/fixtures/vault-preview-v1"
```

The fetch script copies the complete fixture set, including `manifest.json`, into the destination. The commit pin must be updated deliberately when the API schema or fixture set changes.

The fixtures intentionally include an unknown activity kind, unknown status/freshness/rarity values, and additive response data. Consumers must validate known fields and ignore unknown optional properties.
