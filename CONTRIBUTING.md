# Contributing

These guidelines apply to new work and to existing files being substantially modified. They are intentionally stricter than the current legacy layout so the codebase becomes easier to navigate as it grows.

## File Organization

- One top-level production type per file: one class, interface, record, enum, or delegate.
- Name the file after the type, for example `IJournalMetadataCache.cs`, `S3JournalMetadataCache.cs`, and `SeasonRevision.cs`.
- Keep interfaces in their own files rather than beside their implementation.
- Keep public records and DTOs in their own files, even when they are closely related.
- Use one namespace per file.
- Use private nested types only for small implementation details that have no independent reuse or test value.
- Do not add new types to an existing unrelated file to avoid creating a file.
- Existing files containing multiple types are legacy code. Do not copy that pattern; split types when the file is next changed substantially, avoiding unrelated formatting churn.

## Project Boundaries

- Put shared domain contracts and dependency-light abstractions in `VaultShared`.
- Put Blizzard-specific behavior and models in `Infrastructure.Blizzard`.
- Put Raider.IO-specific behavior and models in `Infrastructure.Raiderio`.
- Put S3 cache implementations in `Infrastructure.VaultCache`.
- Put Lambda orchestration in the Lambda project that owns the handler.
- Put calculation and API response models in `VaultPreviewLambda` unless they are explicitly shared contracts.
- Keep implementations behind interfaces when they cross a project or infrastructure boundary.
- Avoid circular project references; dependencies should point toward contracts, not toward higher-level handlers.

## API Contracts

- Treat `contracts/v1/openapi.yaml` as the canonical API wire contract.
- Add or update contract fixtures in the versioned fixture directory when response semantics change.
- Keep legacy response adapters separate from normalized v1 models.
- Never send HTML, CSS classes, or executable content in API data.
- Preserve forward compatibility for additive fields and unknown activity values.
- Document breaking changes with a schema or endpoint version change.

## Configuration And State

- Model configuration as immutable revisions with deterministic content hashes.
- Validate configuration before activation and test activation, scheduling, and rollback.
- Use durable storage for production state and caches. In-memory state may optimize a warm process but must not be the source of truth.
- Include cache schema/version, timestamps, and stale/fallback semantics in durable cache envelopes.
- Treat old persisted objects as an explicit migration case rather than assuming they contain current fields.

## Tests

- Each production project has a corresponding test project: `VaultPreviewLambda.Tests`, `CharacterDataLambda.Tests`, `VaultShared.Tests`, `Infrastructure.Blizzard.Tests`, and `Infrastructure.VaultCache.Tests`.
- A test belongs in the project that owns the production behavior it verifies.
- Keep one primary test class per file and name it after the production type or behavior under test.
- Do not place API calculator tests in `CharacterDataLambda.Tests` merely because that test project already exists.
- Keep cross-project fakes in the test project that uses them; promote reusable fakes to a shared test utility only when there is a demonstrated second consumer.
- Use contract fixtures rather than duplicating large JSON payloads in test source.
- Add regression coverage for migration, unknown values, unavailable upstream data, stale caches, and legacy compatibility when those behaviors are changed.

## Reliability And Errors

- Preserve caller cancellation. Do not convert requested cancellation into a successful fallback response.
- Handle expected upstream statuses explicitly and distinguish not-found, unavailable, stale, unsupported, and invalid states.
- Do not swallow permission, configuration, or programming errors as empty data.
- Log enough context to diagnose failures without logging credentials or unnecessary character data.

## CSharp Style

- Keep nullable reference types enabled and avoid suppressing warnings without a concrete invariant.
- Use explicit access modifiers on top-level types and members.
- Prefer immutable records and read-only collections for configuration and response snapshots.
- Keep methods focused; extract a helper when it represents a named domain operation or removes repeated complexity.
- Follow the repository's established explicit-type style and naming conventions.
- Keep comments focused on non-obvious domain rules, not line-by-line narration.
- Preserve existing line endings and avoid unrelated formatting changes in a task.

## GitHub Workflow

- Work from a task issue and branch named with its task ID.
- Keep one coherent task per pull request.
- Stage only files belonging to the task; leave unrelated worktree changes untouched.
- Link pull requests to their issue and update the shared plan when scope, dependencies, or contract decisions change.
- Run the relevant project tests and the full solution build before requesting review.
- Keep the GitHub Project status and dependency state synchronized with the shared plan.
