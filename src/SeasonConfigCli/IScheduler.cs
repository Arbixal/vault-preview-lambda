using Amazon.Scheduler;
using Amazon.Scheduler.Model;

namespace SeasonConfigCli;

public interface IScheduler
{
    Task CreateScheduleAsync(
        string groupName,
        string scheduleName,
        DateTimeOffset activationAt,
        string lambdaFunctionArn,
        string roleArn,
        CancellationToken cancellationToken = default);

    Task DeleteScheduleAsync(
        string groupName,
        string scheduleName,
        CancellationToken cancellationToken = default);
}
