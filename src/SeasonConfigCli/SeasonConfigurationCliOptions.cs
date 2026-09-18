namespace SeasonConfigCli;

public sealed record SeasonConfigurationCliOptions(
    string? ActivationFunctionName = null,
    string? ActivationFunctionArn = null,
    string? SchedulerRoleArn = null,
    string? SchedulerGroupName = null,
    string? SchedulerDeadLetterQueueArn = null);
