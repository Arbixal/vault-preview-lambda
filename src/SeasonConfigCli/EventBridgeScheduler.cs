using System;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Lambda;
using Amazon.Lambda.Model;
using Amazon.Scheduler;
using Amazon.Scheduler.Model;

namespace SeasonConfigCli;

public sealed class EventBridgeScheduler : IScheduler
{
    private readonly IAmazonScheduler _schedulerClient;
    private readonly IAmazonLambda _lambdaClient;
    private readonly string _functionName;
    private readonly string _roleArn;
    private readonly string _groupName;

    public EventBridgeScheduler(
        IAmazonScheduler schedulerClient,
        IAmazonLambda lambdaClient,
        string functionName,
        string roleArn,
        string groupName)
    {
        _schedulerClient = schedulerClient;
        _lambdaClient = lambdaClient;
        _functionName = functionName;
        _roleArn = roleArn;
        _groupName = groupName;
    }

    public async Task CreateScheduleAsync(
        string groupName,
        string scheduleName,
        DateTimeOffset activationAt,
        string lambdaFunctionArn,
        string roleArn,
        CancellationToken cancellationToken = default)
    {
        string functionArn = string.IsNullOrEmpty(lambdaFunctionArn)
            ? await GetFunctionArn(cancellationToken)
            : lambdaFunctionArn;

        string scheduleExpression = $"at({activationAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)})";

        CreateScheduleRequest request = new()
        {
            Name = scheduleName,
            GroupName = groupName,
            ScheduleExpression = scheduleExpression,
            ActionAfterCompletion = ActionAfterCompletion.DELETE,
            Target = new Target
            {
                Arn = functionArn,
                RoleArn = roleArn,
                DeadLetterConfig = new Amazon.Scheduler.Model.DeadLetterConfig
                {
                    Arn = GetDeadLetterArn()
                }
            }
        };

        try
        {
            await _schedulerClient.CreateScheduleAsync(request, cancellationToken);
        }
        catch (AmazonSchedulerException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            UpdateScheduleRequest updateRequest = new()
            {
                Name = scheduleName,
                GroupName = groupName,
                ScheduleExpression = scheduleExpression,
                ActionAfterCompletion = ActionAfterCompletion.DELETE,
                Target = new Target
                {
                    Arn = functionArn,
                    RoleArn = roleArn,
                    DeadLetterConfig = new Amazon.Scheduler.Model.DeadLetterConfig
                    {
                        Arn = GetDeadLetterArn()
                    }
                }
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

    private async Task<string> GetFunctionArn(CancellationToken cancellationToken)
    {
        GetFunctionResponse response = await _lambdaClient.GetFunctionAsync(
            new GetFunctionRequest { FunctionName = _functionName },
            cancellationToken);
        return response.Configuration?.FunctionArn ?? _functionName;
    }

    private string GetDeadLetterArn()
    {
        return System.Environment.GetEnvironmentVariable("VAULT_PREVIEW_SCHEDULER_DEAD_LETTER_QUEUE_ARN") ?? string.Empty;
    }
}
