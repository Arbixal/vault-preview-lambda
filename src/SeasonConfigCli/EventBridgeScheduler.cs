using System;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Scheduler;
using Amazon.Scheduler.Model;

namespace SeasonConfigCli;

public sealed class EventBridgeScheduler : IScheduler
{
    private readonly IAmazonScheduler _schedulerClient;

    public EventBridgeScheduler(IAmazonScheduler schedulerClient)
    {
        _schedulerClient = schedulerClient;
    }

    public async Task CreateOrUpdateScheduleAsync(
        SchedulerScheduleRequest scheduleRequest,
        CancellationToken cancellationToken = default)
    {
        _validateRequest(scheduleRequest);
        DateTimeOffset activationAt = scheduleRequest.ActivationAt.ToUniversalTime();
        string scheduleExpression = $"at({activationAt.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)})";

        CreateScheduleRequest request = new()
        {
            Name = scheduleRequest.ScheduleName,
            GroupName = scheduleRequest.GroupName,
            ScheduleExpression = scheduleExpression,
            ScheduleExpressionTimezone = "UTC",
            FlexibleTimeWindow = new FlexibleTimeWindow
            {
                Mode = FlexibleTimeWindowMode.OFF
            },
            ActionAfterCompletion = ActionAfterCompletion.DELETE,
            Target = _createTarget(scheduleRequest)
        };

        try
        {
            await _schedulerClient.CreateScheduleAsync(request, cancellationToken);
        }
        catch (AmazonSchedulerException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            UpdateScheduleRequest updateRequest = new()
            {
                Name = scheduleRequest.ScheduleName,
                GroupName = scheduleRequest.GroupName,
                ScheduleExpression = scheduleExpression,
                ScheduleExpressionTimezone = "UTC",
                FlexibleTimeWindow = new FlexibleTimeWindow
                {
                    Mode = FlexibleTimeWindowMode.OFF
                },
                ActionAfterCompletion = ActionAfterCompletion.DELETE,
                Target = _createTarget(scheduleRequest)
            };
            await _schedulerClient.UpdateScheduleAsync(updateRequest, cancellationToken);
        }
    }

    public async Task DeleteScheduleAsync(
        string groupName,
        string scheduleName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _schedulerClient.DeleteScheduleAsync(
                new DeleteScheduleRequest
                {
                    Name = scheduleName,
                    GroupName = groupName
                },
                cancellationToken);
        }
        catch (AmazonSchedulerException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
        }
    }

    private static Target _createTarget(SchedulerScheduleRequest request)
    {
        return new Target
        {
            Arn = request.ActivationFunctionArn,
            RoleArn = request.SchedulerRoleArn,
            Input = request.Input,
            DeadLetterConfig = new Amazon.Scheduler.Model.DeadLetterConfig
            {
                Arn = request.DeadLetterQueueArn
            },
            RetryPolicy = new RetryPolicy
            {
                MaximumEventAgeInSeconds = request.MaximumEventAgeInSeconds,
                MaximumRetryAttempts = request.MaximumRetryAttempts
            }
        };
    }

    private static void _validateRequest(SchedulerScheduleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.GroupName) ||
            string.IsNullOrWhiteSpace(request.ScheduleName) ||
            string.IsNullOrWhiteSpace(request.ActivationFunctionArn) ||
            string.IsNullOrWhiteSpace(request.SchedulerRoleArn) ||
            string.IsNullOrWhiteSpace(request.DeadLetterQueueArn) ||
            string.IsNullOrWhiteSpace(request.Input))
        {
            throw new ArgumentException("Scheduler group, name, target ARN, role ARN, dead-letter ARN, and input are required.");
        }
    }
}
