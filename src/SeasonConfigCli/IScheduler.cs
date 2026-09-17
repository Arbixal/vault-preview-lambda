using Amazon.Scheduler;
using Amazon.Scheduler.Model;

namespace SeasonConfigCli;

public interface IScheduler
{
    Task CreateOrUpdateScheduleAsync(
        SchedulerScheduleRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteScheduleAsync(
        string groupName,
        string scheduleName,
        CancellationToken cancellationToken = default);
}
