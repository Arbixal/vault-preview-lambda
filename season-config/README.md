# Season Configuration Delivery

This directory contains the reviewable source definitions used by the future season configuration CLI.

## Source Layout

Each immutable revision is committed at:

```text
season-config/definitions/{seasonId}/{revisionId}.json
```

For example:

```text
season-config/definitions/midnight-s2/midnight-s2-r2.json
```

The directory name, file name, and `configuration.id` must identify the same season. The revision ID is derived from the file name and must use a lowercase kebab-case slug with a numeric revision suffix, such as `midnight-s2-r2`.

The file contains only a serialized `SeasonConfiguration` object. It must not contain `revisionHash`, lifecycle status, activation timestamps, S3 keys, or deployment metadata. The CLI creates the immutable `SeasonRevision` from this source and computes the hash with `SeasonRevisionHasher.Compute`.

## Source Shape

```json
{
  "id": "future-season",
  "displayName": "Future Season",
  "shortLabel": "Future",
  "expansion": "Future Expansion",
  "sourceSeasonId": null,
  "activities": [
    {
      "id": "future-activity",
      "kind": "future-kind",
      "title": "Future Activity",
      "subtitle": "Configured activity",
      "order": 0,
      "slots": [
        {
          "id": "future-slot",
          "unit": "activities",
          "required": 3,
          "label": "3 activities",
          "displayItemCount": 1,
          "fallbackReward": {
            "itemLevel": 500,
            "rarity": "epic"
          }
        }
      ],
      "sourceIds": [],
      "progressRules": []
    }
  ]
}
```

`sourceIds` for Journal-backed raid activities use typed values such as `wow:journal-instance:1320`. `progressRules` with a `dimension` value are used for dimension-based evidence such as raid difficulties. Value-based rules such as Mythic+ levels and Delve tiers use `"dimension": null`.

The shared `SeasonConfigurationValidator` remains the canonical validator. It checks required metadata, activity and slot uniqueness, ordering, source IDs, thresholds, reward mappings, evidence rules, and lowercase supported rarities.

## Standard Activity Kinds

The source format deliberately keeps `kind` open-ended, but most normal seasons use these three API-supported evidence-driven kinds:

| Kind | Evidence source | Standard progress model | Typical slot requirements |
| --- | --- | --- | --- |
| `raid` | Blizzard Journal metadata selected by `sourceIds`, combined with character raid encounters | One progress item per Journal encounter with nested difficulty dimensions such as `lfr`, `normal`, `heroic`, and `mythic` | 2, 4, and 6 bosses |
| `mythic-plus` | Raider.IO weekly highest-level runs | One progress item per selected weekly run with a numeric Mythic+ value | 1, 4, and 8 runs |
| `delves` | Blizzard character statistics plus the season-aware S3 baseline | Count-based progress items keyed by configured Delve tier | 2, 4, and 8 Delves |

The API owns the rules behind these kinds. The source definition supplies the activity title, ordering, slots, source IDs, and reward rules; the calculation layer supplies the upstream evidence and returns the normalized sections and slots.

### Raid Definitions

Raid activities normally contain:

- `sourceIds` using values such as `wow:journal-instance:1320` in the intended display order.
- Three slots with `unit: "bosses"` and requirements such as 2, 4, and 6.
- `progressRules` with `dimension` values for the supported difficulty dimensions.
- Fallback rewards for cases where a slot is complete but no evidence-specific rule is available.

The source does not list individual bosses. Journal metadata supplies the complete encounter list, including bosses the character has not killed.

### Mythic+ Definitions

Mythic+ activities normally contain:

- No Journal `sourceIds`; an empty array is expected.
- Three slots with `unit: "runs"` and requirements such as 1, 4, and 8.
- `progressRules` with `dimension: null` and `minimumValue` thresholds for Mythic+ levels.
- Fallback rewards for completed slots without a more specific run-level rule.

The calculator selects and sorts the weekly runs. The source definition does not contain dungeon names, run identities, or frontend display assumptions.

### Delve Definitions

Delve activities normally contain:

- No Journal `sourceIds`; an empty array is expected.
- Three slots with `unit: "delves"` and requirements such as 2, 4, and 8.
- `progressRules` with `dimension: null` and `minimumValue` thresholds for configured Delve tiers.
- Fallback rewards for completed slots without a more specific tier rule.

The calculator subtracts a matching season/revision/hash baseline from current statistics. A revision change creates a fresh baseline and returns zero initial progress rather than subtracting across seasons.

Unknown future kinds are allowed by the format and are returned through the generic unsupported-section path until a calculation provider is implemented.

## Representative Midnight Season 1 Example

`definitions/midnight-s1/midnight-s1-r1.json` is a representative standard-season definition. It uses:

- Public season ID `midnight-s1`.
- Diagnostic Blizzard source season ID `17`.
- The `The Venomous Abyss` Journal instance source `wow:journal-instance:1320`.
- Raid, Mythic+, and Delve activities.
- Standard 2/4/6 raid, 1/4/8 Mythic+, and 2/4/8 Delve slot shapes.
- Dimension-based raid rewards and value-based Mythic+/Delve rewards.

The item levels in this example are illustrative configuration values showing the complete shape. They must be replaced or approved by the API content owner before publishing a production revision.

## Generated Revision Artifact

The CLI converts a source file into:

```text
season-config/v1/revisions/{seasonId}/{revisionId}.json
```

```json
{
  "id": "future-season-r1",
  "configuration": {
    "id": "future-season"
  },
  "revisionHash": "sha256:<64 lowercase hexadecimal characters>"
}
```

The example above abbreviates `configuration`; the stored artifact contains the complete source configuration. The hash is the deterministic lowercase SHA-256 value calculated over the canonical camelCase serialization of the complete `SeasonConfiguration`, not over the wrapper document.

The revision object is immutable. Publishing an existing revision ID succeeds only when the existing document is the same immutable content; a different configuration or hash must fail rather than overwrite it.

## Lifecycle Boundary

GitHub Actions and the CLI own authoring and requests for lifecycle operations. They do not write active state directly.

1. Pull request validation loads the committed source file, validates it, computes the revision hash, and runs calculation checks without modifying AWS state.
2. `publish` validates the source and writes the immutable revision document to S3 with no-overwrite semantics.
3. `activate` and `rollback` invoke the dedicated activation Lambda with a season ID and revision ID.
4. `schedule` validates the immutable revision and creates a deterministic one-time EventBridge Scheduler schedule targeting the activation Lambda.
5. The activation Lambda re-reads and validates the revision, conditionally replaces `active.json`, and clears pending schedule state.
6. `cancel` removes the named EventBridge schedule and clears pending schedule intent safely if it exists.
7. API and scheduled-refresh readers use only `active.json`; `scheduled.json` is intent, not an implicit active source.

Repeated operations must be safe. Publishing the same revision is idempotent, activation of the already-active revision is successful, cancellation of a missing schedule is a no-op, and an activation retry can complete cleanup after a previous attempt already replaced active state.

## Runtime Objects

The durable runtime objects are:

```text
season-config/v1/revisions/{seasonId}/{revisionId}.json
season-config/v1/active.json
season-config/v1/scheduled.json
```

`active.json` contains the globally active `seasonId`, `revision`, `revisionHash`, and `activationAt`. `scheduled.json` contains the same pointer shape for pending intent and is removed by activation or cancellation. A failed cleanup must be retryable and must not make the pending pointer active to readers.

## Delivery Inputs

The CLI and GitHub Actions workflow should accept these inputs rather than embedding deployment values in source definitions:

| Input | Purpose |
| --- | --- |
| `AWS_REGION` | Region containing the S3 bucket, Lambda, and scheduler resources. |
| `VAULT_PREVIEW_DATA_BUCKET` | Existing durable data bucket; the current deployment default is `vault-preview-data`. |
| `VAULT_PREVIEW_ACTIVATION_FUNCTION_NAME` | Dedicated Lambda invoked for immediate and scheduled activation. |
| `VAULT_PREVIEW_SCHEDULER_ROLE_ARN` | EventBridge Scheduler execution role allowed to invoke the activation Lambda. |
| `VAULT_PREVIEW_SCHEDULER_GROUP` | Optional scheduler group used to isolate season schedules. |
| `VAULT_PREVIEW_CONFIG_ENVIRONMENT` | Logical environment name used by protected GitHub environments and workflow output. |
| `AWS_ROLE_TO_ASSUME` | GitHub OIDC deployment role; no long-lived AWS credentials are stored in the repository. |

The GitHub Actions role may publish revision objects and manage named schedules, but active-state writes remain restricted to the activation Lambda role.

## Retention

Immutable revision documents remain available for rollback. A later lifecycle policy may archive very old retired revisions, but activation and rollback must not require deleting prior revision documents. One-time EventBridge schedules are configured for automatic deletion after completion.
