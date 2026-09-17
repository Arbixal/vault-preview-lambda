# Season Activation Infrastructure

The activation Lambda is deployed by the `season-activation` SAM stack. The stack owns persistent infrastructure; T19's CLI integration creates one-time schedules through the EventBridge Scheduler API.

## Stack Outputs

The stack exports these values for the configuration CLI and GitHub Actions workflow:

| Output | Consumer |
| --- | --- |
| `ActivationFunctionArn` | EventBridge Scheduler target and CLI activation invocations |
| `SchedulerGroupName` | CLI `CreateSchedule`, `UpdateSchedule`, and `DeleteSchedule` requests |
| `SchedulerGroupArn` | IAM and operational discovery |
| `SchedulerRoleArn` | EventBridge Scheduler `Target.RoleArn` |
| `SchedulerDeadLetterQueueArn` | EventBridge Scheduler `Target.DeadLetterConfig` |

## Dynamic Schedule Shape

The CLI should create a one-time schedule with the following semantics:

```json
{
  "Name": "vault-preview-activate-midnight-s2-midnight-s2-r2",
  "GroupName": "vault-preview-season-config",
  "ScheduleExpression": "at(2026-10-06T15:00:00)",
  "ScheduleExpressionTimezone": "UTC",
  "FlexibleTimeWindow": {
    "Mode": "OFF"
  },
  "ActionAfterCompletion": "DELETE",
  "Target": {
    "Arn": "<ActivationFunctionArn>",
    "RoleArn": "<SchedulerRoleArn>",
    "Input": "{\"operation\":\"activate\",\"seasonId\":\"midnight-s2\",\"revisionId\":\"midnight-s2-r2\",\"revisionHash\":\"sha256:...\",\"activationAt\":\"2026-10-06T15:00:00Z\"}",
    "DeadLetterConfig": {
      "Arn": "<SchedulerDeadLetterQueueArn>"
    },
    "RetryPolicy": {
      "MaximumEventAgeInSeconds": 86400,
      "MaximumRetryAttempts": 3
    }
  }
}
```

The schedule name must be deterministic and no longer than the EventBridge Scheduler limit. Repeating the same schedule request should update the same schedule rather than create an untracked duplicate. The activation Lambda remains the only component allowed to replace `active.json`.

## IAM Boundary

The activation Lambda role can read, write, and delete objects only below `season-config/*`. The scheduler execution role can invoke only the activation Lambda and send messages to the activation dead-letter queue. The GitHub configuration-delivery role must receive explicit permission to create, update, inspect, and delete schedules in the named scheduler group, invoke the activation Lambda for immediate operations, and pass only the scheduler execution role.

The deployment identity also needs `iam:PassRole` for the activation Lambda execution role and the scheduler execution role when CloudFormation creates or updates the stack. These permissions should be resource-scoped and reviewed separately from the runtime roles.
