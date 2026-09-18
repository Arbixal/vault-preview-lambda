namespace SeasonConfigCli;

public sealed record SchedulerScheduleRequest(
    string GroupName,
    string ScheduleName,
    DateTimeOffset ActivationAt,
    string ActivationFunctionArn,
    string SchedulerRoleArn,
    string DeadLetterQueueArn,
    string Input,
    int MaximumEventAgeInSeconds,
    int MaximumRetryAttempts);
