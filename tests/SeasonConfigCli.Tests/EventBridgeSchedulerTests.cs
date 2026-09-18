using System.Net;
using Amazon;
using Amazon.Runtime;
using Amazon.Scheduler;
using Amazon.Scheduler.Model;
using Xunit;

namespace SeasonConfigCli.Tests;

public sealed class EventBridgeSchedulerTests
{
    [Fact]
    public async Task CreateOrUpdateSchedule_UsesUtcPayloadAndT19DeliverySettings()
    {
        FakeSchedulerClient client = new();
        EventBridgeScheduler scheduler = new(client);
        DateTimeOffset activationAt = new(2026, 10, 6, 8, 0, 0, TimeSpan.FromHours(-7));

        await scheduler.CreateOrUpdateScheduleAsync(
            new SchedulerScheduleRequest(
                "vault-preview-season-config",
                "vault-preview-activate-midnight-s2-midnight-s2-r2",
                activationAt,
                "arn:aws:lambda:us-east-1:123456789012:function:activation",
                "arn:aws:iam::123456789012:role/scheduler",
                "arn:aws:sqs:us-east-1:123456789012:activation-dlq",
                "{\"operation\":\"activate\"}",
                86400,
                3));

        Assert.NotNull(client.CreatedRequest);
        Assert.Equal("at(2026-10-06T15:00:00)", client.CreatedRequest!.ScheduleExpression);
        Assert.Equal("UTC", client.CreatedRequest.ScheduleExpressionTimezone);
        Assert.Equal(FlexibleTimeWindowMode.OFF, client.CreatedRequest.FlexibleTimeWindow.Mode);
        Assert.Equal(ActionAfterCompletion.DELETE, client.CreatedRequest.ActionAfterCompletion);
        Assert.Equal(
            "arn:aws:lambda:us-east-1:123456789012:function:activation",
            client.CreatedRequest.Target.Arn);
        Assert.Equal(
            "arn:aws:iam::123456789012:role/scheduler",
            client.CreatedRequest.Target.RoleArn);
        Assert.Equal("{\"operation\":\"activate\"}", client.CreatedRequest.Target.Input);
        Assert.Equal(
            "arn:aws:sqs:us-east-1:123456789012:activation-dlq",
            client.CreatedRequest.Target.DeadLetterConfig.Arn);
        Assert.Equal(86400, client.CreatedRequest.Target.RetryPolicy.MaximumEventAgeInSeconds);
        Assert.Equal(3, client.CreatedRequest.Target.RetryPolicy.MaximumRetryAttempts);
    }

    [Fact]
    public async Task CreateOrUpdateSchedule_UpdatesAnExistingSchedule()
    {
        FakeSchedulerClient client = new() { ThrowConflict = true };
        EventBridgeScheduler scheduler = new(client);

        await scheduler.CreateOrUpdateScheduleAsync(_createRequest());

        Assert.NotNull(client.UpdatedRequest);
        Assert.Null(client.CreatedRequest);
    }

    private static SchedulerScheduleRequest _createRequest() => new(
        "group",
        "schedule",
        DateTimeOffset.UtcNow.AddHours(1),
        "function-arn",
        "role-arn",
        "dlq-arn",
        "{}",
        86400,
        3);

    private sealed class FakeSchedulerClient : AmazonSchedulerClient
    {
        public FakeSchedulerClient()
            : base(new AnonymousAWSCredentials(), RegionEndpoint.USEast1)
        {
        }

        public CreateScheduleRequest? CreatedRequest { get; private set; }
        public UpdateScheduleRequest? UpdatedRequest { get; private set; }
        public bool ThrowConflict { get; init; }

        public override Task<CreateScheduleResponse> CreateScheduleAsync(
            CreateScheduleRequest request,
            CancellationToken cancellationToken)
        {
            if (ThrowConflict)
            {
                throw new AmazonSchedulerException("conflict")
                {
                    StatusCode = HttpStatusCode.Conflict
                };
            }

            CreatedRequest = request;
            return Task.FromResult(new CreateScheduleResponse());
        }

        public override Task<UpdateScheduleResponse> UpdateScheduleAsync(
            UpdateScheduleRequest request,
            CancellationToken cancellationToken)
        {
            UpdatedRequest = request;
            return Task.FromResult(new UpdateScheduleResponse());
        }
    }
}
