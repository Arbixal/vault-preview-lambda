# Season Configuration Operations Runbook

This runbook describes the approved path from a committed season definition to
an active production configuration. It deliberately uses GitHub Actions and
the activation Lambda for mutations. Do not edit season objects in S3 by hand.

## Preconditions

Before a live operation, confirm:

- The source definition is merged into `master` and has passed the Season Configuration Validation workflow.
- The `season-activation` SAM stack is deployed in the configured AWS region.
- The protected GitHub `production` environment has the configuration-delivery role and T19 stack outputs documented in [`README.md`](README.md).
- The API deployment has completed before production smoke checks are attempted.
- A known, valid character is available for the progress smoke check.
- Node.js 18 or newer is available for the production smoke script.
- Any existing pending schedule is understood before publishing a new schedule.

The live workflows require a production-environment approval and reject dispatches
from branches other than `master`. All five mutating workflows share the
`production-season-configuration` concurrency group.

## Validate A Definition

Pull-request validation performs this check automatically. The equivalent local
command is:

```bash
dotnet run \
  --project src/SeasonConfigCli/SeasonConfigCli.csproj \
  --configuration Release \
  -- validate season-config/definitions/midnight-s2/midnight-s2-r1.json
```

The command must print a valid configuration ID and a canonical lowercase
`sha256:` revision hash. Record that hash with the workflow run when an
operation is approved.

## Publish A Revision

Publishing writes only the immutable revision object. It does not change active
state.

The GitHub UI workflow is `Season Configuration Publish`. The equivalent CLI
dispatch is:

```bash
gh workflow run season-config-publish.yml \
  --ref master \
  -f source_file=season-config/definitions/midnight-s2/midnight-s2-r1.json \
  -f season_id=midnight-s2 \
  -f revision_id=midnight-s2-r1
```

The workflow summary reports the revision ID and hash. Publishing the same
revision with identical content is idempotent. Publishing a different content
hash under an existing revision ID is rejected; create a new numeric revision
ID instead.

Wait for the workflow to finish before starting activation:

```bash
gh run list --workflow season-config-publish.yml --branch master --limit 1
gh run view <run-id> --log-failed
```

## Activate Immediately

Use the `Season Configuration Activate` workflow after the revision is
published:

```bash
gh workflow run season-config-activate.yml \
  --ref master \
  -f season_id=midnight-s2 \
  -f revision_id=midnight-s2-r1
```

The activation Lambda re-reads and validates the immutable revision, replaces
the active pointer conditionally, and removes any pending schedule pointer.
Repeating the same activation is safe.

The optional `activation_at` input is only for a timestamp at or before the
invocation time. Use the schedule workflow for a future timestamp.

## Schedule Activation

Use a future UTC timestamp with the `Season Configuration Schedule` workflow:

```bash
gh workflow run season-config-schedule.yml \
  --ref master \
  -f season_id=midnight-s2 \
  -f revision_id=midnight-s2-r1 \
  -f activation_at=2026-10-06T15:00:00Z
```

The CLI writes the pending pointer and creates a deterministic one-time
EventBridge Scheduler schedule. The schedule uses UTC, retries activation up
to three times for up to 24 hours, sends failed invocations to the configured
dead-letter queue, and deletes itself after completion.

Do not schedule a second revision while a different pending revision exists.
Cancel the existing schedule first, or wait for its activation to complete.

## Cancel A Schedule

Use the `Season Configuration Cancel` workflow:

```bash
gh workflow run season-config-cancel.yml --ref master
```

Cancellation deletes the deterministic Scheduler entry and clears the pending
pointer. A missing Scheduler entry is treated as already cancelled, so this is
safe to retry.

## Roll Back

Rollback is a configuration-only operation. It does not require a frontend
deployment.

Use the `Season Configuration Rollback` workflow with a previously published
revision:

```bash
gh workflow run season-config-rollback.yml \
  --ref master \
  -f season_id=midnight-s2 \
  -f revision_id=midnight-s2-r1
```

Confirm the selected revision from the original publish workflow before
approval. The activation Lambda validates its content hash again before
promoting it.

## Production Smoke Check

After immediate activation, scheduled activation, or rollback, run the smoke
check against the deployed API. It verifies:

- `/v1/app-config` returns schema version 1 and a valid active season revision.
- The app-config ETag matches the active revision hash.
- Conditional app-config revalidation returns `304 Not Modified`.
- The character endpoint returns schema version 1.
- The character response season ID, revision, and revision hash match app-config.

Set the API base URL without a trailing slash. The realm and character must be
valid production values:

```bash
export VAULT_PREVIEW_API_BASE_URL="https://<api-host>"
export VAULT_PREVIEW_REGION="us"
export VAULT_PREVIEW_REALM="<realm>"
export VAULT_PREVIEW_CHARACTER="<character>"
export VAULT_PREVIEW_EXPECTED_SEASON_ID="midnight-s2"
export VAULT_PREVIEW_EXPECTED_REVISION_ID="midnight-s2-r1"
export VAULT_PREVIEW_EXPECTED_REVISION_HASH="sha256:<hash-from-publish-run>"

node season-config/smoke-check.mjs
```

The script uses only Node's built-in `fetch` and does not print character
payloads or credentials. A non-zero exit code is a rollout failure.

## Failure Recovery

### Publish failure

If the existing revision has a different hash, do not overwrite it. Review the
source and publish it under the next revision ID. Existing immutable revisions
remain available for rollback.

### Scheduler creation failure

The CLI writes pending state only after Scheduler creation succeeds. If the
pending-state write fails after Scheduler creation, the CLI attempts to restore
the previous schedule or delete the new schedule. Inspect the workflow result
and run `cancel` only after confirming which schedule exists.

### Activation failure

Inspect the activation Lambda logs for the structured `season_activation` event
and its `status`, `operation`, season, revision, and hash fields. If the
Scheduler dead-letter alarm fired, inspect the dead-letter message and retry
the same activation after correcting the underlying permission or data issue.

An activation may replace `active.json` before pending-pointer cleanup fails.
Retrying the same activation is the recovery path; it revalidates the revision
and retries cleanup without requiring manual S3 edits.

### Stale pending state

Run the cancel workflow. It safely treats a missing Scheduler entry as a
no-op and clears the pending pointer. If the pending pointer is malformed,
stop and investigate rather than editing it manually.

### Rollback after a failed activation

Choose the last known-good immutable revision, run the rollback workflow, and
repeat the production smoke check. Record both workflow run URLs and the final
active revision hash in the incident notes.

## Approval And Evidence

For each production change, retain:

- The merged source-definition commit and pull-request approval.
- The validation workflow run and computed revision hash.
- The publish workflow run and revision output.
- The activation, schedule, cancel, or rollback workflow run as applicable.
- The smoke-check output showing matching app-config and character revision metadata.
- Any activation log event, Scheduler failure, or dead-letter message involved.

## Retention And Monitoring

Immutable revision documents must remain available for rollback. Do not delete
prior revision objects as part of normal activation or rollback. One-time
Scheduler entries are configured for automatic deletion after completion.

The activation stack raises an alarm when its dead-letter queue contains a
message. The `AlertsTopicArn` output must be subscribed to the environment's
operational alert destination. Activation logs should be reviewed for failed
operations, unexpected revisions, hash mismatches, and repeated retries.
