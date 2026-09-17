using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using Amazon;
using Amazon.Lambda;
using Amazon.Lambda.Model;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using VaultShared.Seasons;
using VaultPreview.SeasonConfigurationInfrastructure;
using SeasonConfigCli;
using SeasonConfigCli.Request;
using SeasonConfigCli.Response;
using Xunit;

namespace SeasonConfigCli.Tests;

public class SeasonConfigurationCliTests
{
    [Fact]
    public async Task Validate_ValidConfiguration_ReturnsZero()
    {
        SeasonConfiguration configuration = _createConfiguration();
        string json = Serialize(configuration);
        string filePath = WriteTempFile(json);

        SeasonConfigurationCli cli = CreateCli();

        int result = await cli.ValidateAsync(filePath);

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task Validate_InvalidConfiguration_ReturnsOne()
    {
        string json = @"{""id"":"""",""displayName"":"""",""shortLabel"":"""",""expansion"":"""",""sourceSeasonId"":null,""activities"":[]}";
        string filePath = WriteTempFile(json);

        SeasonConfigurationCli cli = CreateCli();

        int result = await cli.ValidateAsync(filePath);

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task Publish_ValidRevision_WritesToS3()
    {
        FakeS3Client s3Client = new();
        SeasonConfiguration configuration = _createConfiguration();
        SeasonRevision revision = SeasonRevision.Create("future-season-r1", configuration);

        SeasonConfigurationCli cli = CreateCli(s3Client);

        int result = await cli.PublishAsync(
            WriteTempFile(Serialize(configuration)),
            "future-season",
            "future-season-r1");

        Assert.Equal(0, result);
        Assert.True(s3Client.Contains("season-config/v1/revisions/future-season/future-season-r1.json"));
    }

    [Fact]
    public async Task Publish_SeasonIdMismatch_ReturnsOne()
    {
        SeasonConfiguration configuration = _createConfiguration();
        SeasonConfigurationCli cli = CreateCli();

        int result = await cli.PublishAsync(
            WriteTempFile(Serialize(configuration)),
            "wrong-season",
            "future-season-r1");

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task Publish_RevisionIdForDifferentSeason_ReturnsOne()
    {
        SeasonConfiguration configuration = _createConfiguration();
        SeasonConfigurationCli cli = CreateCli();

        int result = await cli.PublishAsync(
            WriteTempFile(Serialize(configuration)),
            "future-season",
            "other-season-r1");

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task Publish_Idempotent_RePublishingSameContentReturnsZero()
    {
        FakeS3Client s3Client = new();
        SeasonConfiguration configuration = _createConfiguration();
        SeasonConfigurationCli cli = CreateCli(s3Client);

        int firstResult = await cli.PublishAsync(
            WriteTempFile(Serialize(configuration)),
            "future-season",
            "future-season-r1");
        int secondResult = await cli.PublishAsync(
            WriteTempFile(Serialize(configuration)),
            "future-season",
            "future-season-r1");

        Assert.Equal(0, firstResult);
        Assert.Equal(0, secondResult);
    }

    [Fact]
    public async Task Publish_InvalidRevisionId_ReturnsOne()
    {
        SeasonConfiguration configuration = _createConfiguration();
        SeasonConfigurationCli cli = CreateCli();

        int result = await cli.PublishAsync(
            WriteTempFile(Serialize(configuration)),
            "future-season",
            "invalid-revision-id");

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task Schedule_FutureActivation_WritesScheduledPointer()
    {
        FakeS3Client s3Client = new();
        FakeScheduler scheduler = new();
        SeasonConfiguration configuration = _createConfiguration();
        SeasonConfigurationCli cli = CreateCli(s3Client, null, scheduler, "test-function", "test-role", "test-group");

        await cli.PublishAsync(
            WriteTempFile(Serialize(configuration)),
            "future-season",
            "future-season-r1");

        DateTimeOffset activationAt = DateTimeOffset.UtcNow.AddHours(1);
        int result = await cli.ScheduleAsync("future-season", "future-season-r1", activationAt);

        Assert.Equal(0, result);
        Assert.True(s3Client.Contains("season-config/v1/scheduled.json"));
        Assert.Equal("test-group", scheduler.CreatedGroupName);
        Assert.Equal("vault-preview-activate-future-season-future-season-r1", scheduler.CreatedScheduleName);
        using JsonDocument input = JsonDocument.Parse(scheduler.CreatedRequest!.Input);
        Assert.Equal("activate", input.RootElement.GetProperty("operation").GetString());
        Assert.Equal("future-season", input.RootElement.GetProperty("seasonId").GetString());
        Assert.Equal(
            activationAt.ToUniversalTime(),
            DateTimeOffset.Parse(input.RootElement.GetProperty("activationAt").GetString()!));
        Assert.Equal(TimeSpan.Zero, scheduler.CreatedRequest!.ActivationAt.Offset);
    }

    [Fact]
    public async Task Schedule_PastActivation_ReturnsOne()
    {
        SeasonConfigurationCli cli = CreateCli();

        int result = await cli.ScheduleAsync("season", "rev", DateTimeOffset.UtcNow.AddMinutes(-5));

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task Cancel_ClearsPendingSchedule()
    {
        FakeS3Client s3Client = new();
        FakeScheduler scheduler = new();
        SeasonConfiguration configuration = _createConfiguration();
        SeasonConfigurationCli cli = CreateCli(s3Client, null, scheduler, "test-function", "test-role", "test-group");

        await cli.PublishAsync(
            WriteTempFile(Serialize(configuration)),
            "future-season",
            "future-season-r1");
        await cli.ScheduleAsync("future-season", "future-season-r1", DateTimeOffset.UtcNow.AddHours(1));
        Assert.True(s3Client.Contains("season-config/v1/scheduled.json"));

        int result = await cli.CancelAsync();

        Assert.Equal(0, result);
        Assert.False(s3Client.Contains("season-config/v1/scheduled.json"));
        Assert.Equal("vault-preview-activate-future-season-future-season-r1", scheduler.DeletedScheduleName);
    }

    [Fact]
    public async Task Schedule_SchedulerFailure_DoesNotWritePendingPointer()
    {
        FakeS3Client s3Client = new();
        FakeScheduler scheduler = new() { FailCreate = true };
        SeasonConfiguration configuration = _createConfiguration();
        SeasonConfigurationCli cli = CreateCli(s3Client, null, scheduler, "test-function", "test-role", "test-group");

        await cli.PublishAsync(
            WriteTempFile(Serialize(configuration)),
            "future-season",
            "future-season-r1");

        int result = await cli.ScheduleAsync(
            "future-season",
            "future-season-r1",
            DateTimeOffset.UtcNow.AddHours(1));

        Assert.Equal(1, result);
        Assert.False(s3Client.Contains("season-config/v1/scheduled.json"));
    }

    [Fact]
    public async Task Schedule_StateWriteFailureDeletesCreatedSchedule()
    {
        FakeS3Client s3Client = new() { FailScheduledPut = true };
        FakeScheduler scheduler = new();
        SeasonConfiguration configuration = _createConfiguration();
        SeasonConfigurationCli cli = CreateCli(s3Client, null, scheduler, "test-function", "test-role", "test-group");

        await cli.PublishAsync(
            WriteTempFile(Serialize(configuration)),
            "future-season",
            "future-season-r1");

        int result = await cli.ScheduleAsync(
            "future-season",
            "future-season-r1",
            DateTimeOffset.UtcNow.AddHours(1));

        Assert.Equal(1, result);
        Assert.Equal(
            "vault-preview-activate-future-season-future-season-r1",
            scheduler.DeletedScheduleName);
        Assert.False(s3Client.Contains("season-config/v1/scheduled.json"));
    }

    [Fact]
    public async Task Schedule_DifferentPendingRevisionIsRejected()
    {
        FakeS3Client s3Client = new();
        FakeScheduler scheduler = new();
        SeasonConfiguration configuration = _createConfiguration();
        SeasonConfigurationCli cli = CreateCli(s3Client, null, scheduler, "test-function", "test-role", "test-group");

        await cli.PublishAsync(
            WriteTempFile(Serialize(configuration)),
            "future-season",
            "future-season-r1");
        await cli.PublishAsync(
            WriteTempFile(Serialize(configuration with { ShortLabel = "Future 2" })),
            "future-season",
            "future-season-r2");
        await cli.ScheduleAsync(
            "future-season",
            "future-season-r1",
            DateTimeOffset.UtcNow.AddHours(1));

        int result = await cli.ScheduleAsync(
            "future-season",
            "future-season-r2",
            DateTimeOffset.UtcNow.AddHours(2));

        Assert.Equal(1, result);
        Assert.Equal(
            "vault-preview-activate-future-season-future-season-r1",
            scheduler.CreatedScheduleName);
    }

    [Fact]
    public async Task Cancel_SchedulerFailure_PreservesPendingPointer()
    {
        FakeS3Client s3Client = new();
        FakeScheduler scheduler = new();
        SeasonConfiguration configuration = _createConfiguration();
        SeasonConfigurationCli cli = CreateCli(s3Client, null, scheduler, "test-function", "test-role", "test-group");

        await cli.PublishAsync(
            WriteTempFile(Serialize(configuration)),
            "future-season",
            "future-season-r1");
        await cli.ScheduleAsync(
            "future-season",
            "future-season-r1",
            DateTimeOffset.UtcNow.AddHours(1));
        scheduler.FailDelete = true;

        int result = await cli.CancelAsync();

        Assert.Equal(1, result);
        Assert.True(s3Client.Contains("season-config/v1/scheduled.json"));
    }

    [Fact]
    public async Task Activate_WithLambdaInvoker_InvokesLambda()
    {
        FakeS3Client s3Client = new();
        FakeLambdaInvoker lambdaInvoker = new();
        SeasonConfiguration configuration = _createConfiguration();
        SeasonConfigurationCli cli = CreateCli(s3Client, lambdaInvoker, null, "test-function");

        await cli.PublishAsync(
            WriteTempFile(Serialize(configuration)),
            "future-season",
            "future-season-r1");

        DateTimeOffset activationAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        int result = await cli.ActivateAsync("future-season", "future-season-r1", activationAt);

        Assert.Equal(0, result);
        Assert.Equal("future-season-r1", lambdaInvoker.LastRequest?.RevisionId);
        Assert.Equal("activate", lambdaInvoker.LastRequest?.Operation);
        Assert.Equal(activationAt.ToUniversalTime(), lambdaInvoker.LastRequest?.ActivationAt);
    }

    [Fact]
    public async Task Rollback_WithLambdaInvoker_InvokesLambda()
    {
        FakeS3Client s3Client = new();
        FakeLambdaInvoker lambdaInvoker = new();
        SeasonConfiguration configuration = _createConfiguration();
        SeasonConfigurationCli cli = CreateCli(s3Client, lambdaInvoker, null, "test-function");

        await cli.PublishAsync(
            WriteTempFile(Serialize(configuration)),
            "future-season",
            "future-season-r1");

        DateTimeOffset activationAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        int result = await cli.RollbackAsync("future-season", "future-season-r1", activationAt);

        Assert.Equal(0, result);
        Assert.Equal("future-season-r1", lambdaInvoker.LastRequest?.RevisionId);
        Assert.Equal("rollback", lambdaInvoker.LastRequest?.Operation);
        Assert.Equal(activationAt.ToUniversalTime(), lambdaInvoker.LastRequest?.ActivationAt);
    }

    [Fact]
    public async Task Activate_WithoutLambdaInvoker_FailsClosed()
    {
        FakeS3Client s3Client = new();
        SeasonConfiguration configuration = _createConfiguration();
        SeasonConfigurationCli cli = CreateCli(s3Client, null, null, null);

        await cli.PublishAsync(
            WriteTempFile(Serialize(configuration)),
            "future-season",
            "future-season-r1");

        int result = await cli.ActivateAsync("future-season", "future-season-r1", null);

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task Rollback_WithoutLambdaInvoker_FailsClosed()
    {
        FakeS3Client s3Client = new();
        SeasonConfiguration configuration = _createConfiguration();
        SeasonConfigurationCli cli = CreateCli(s3Client, null, null, null);

        await cli.PublishAsync(
            WriteTempFile(Serialize(configuration)),
            "future-season",
            "future-season-r1");

        int result = await cli.RollbackAsync("future-season", "future-season-r1", null);

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task Activate_NonExistentRevision_ReturnsOne()
    {
        FakeS3Client s3Client = new();
        SeasonConfigurationCli cli = CreateCli(s3Client, null, null);

        int result = await cli.ActivateAsync("future-season", "nonexistent", null);

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task Validate_NonExistentFile_ReturnsOne()
    {
        SeasonConfigurationCli cli = CreateCli();

        int result = await cli.ValidateAsync("/nonexistent/path.json");

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task Validate_MalformedJson_ReturnsOne()
    {
        SeasonConfigurationCli cli = CreateCli();

        int result = await cli.ValidateAsync(WriteTempFile("{ malformed"));

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task Schedule_WithoutSchedulerConfiguration_FailsClosed()
    {
        FakeS3Client s3Client = new();
        SeasonConfiguration configuration = _createConfiguration();
        SeasonConfigurationCli cli = CreateCli(s3Client, null, null);

        await cli.PublishAsync(
            WriteTempFile(Serialize(configuration)),
            "future-season",
            "future-season-r1");
        DateTimeOffset activationAt = DateTimeOffset.UtcNow.AddHours(1);

        int result = await cli.ScheduleAsync("future-season", "future-season-r1", activationAt);

        Assert.Equal(1, result);
        Assert.False(s3Client.Contains("season-config/v1/scheduled.json"));
    }

    private SeasonConfigurationCli CreateCli(
        FakeS3Client? s3Client = null,
        FakeLambdaInvoker? lambdaInvoker = null,
        FakeScheduler? scheduler = null,
        string? functionName = null,
        string? schedulerRoleArn = null,
        string? schedulerGroupName = null,
        string? activationFunctionArn = null,
        string? deadLetterQueueArn = null)
    {
        s3Client ??= new FakeS3Client();
        ISeasonRevisionStore store = new S3SeasonRevisionProvider(s3Client);
        if (scheduler != null)
        {
            activationFunctionArn ??= "arn:aws:lambda:us-east-1:123456789012:function:test-function";
            deadLetterQueueArn ??= "arn:aws:sqs:us-east-1:123456789012:test-dlq";
        }

        return new SeasonConfigurationCli(
            store,
            lambdaInvoker,
            scheduler,
            new SeasonConfigurationCliOptions(
                functionName,
                activationFunctionArn,
                schedulerRoleArn,
                schedulerGroupName,
                deadLetterQueueArn));
    }

    private static SeasonConfiguration _createConfiguration()
    {
        return new SeasonConfiguration(
            "future-season",
            "Future Season",
            "Future",
            "Future Expansion",
            null,
            [
                new SeasonActivityDefinition(
                    "future-activity",
                    "future-kind",
                    "Future Activity",
                    null,
                    0,
                    [
                        new SeasonSlotDefinition(
                            "future-slot",
                            "activities",
                            3,
                            "3 activities",
                            1,
                            new SeasonRewardDefinition(500, "epic"))
                    ],
                    [])
            ]);
    }

    private static string Serialize(SeasonConfiguration configuration)
    {
        return JsonSerializer.Serialize(configuration, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });
    }

    private static string WriteTempFile(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"season-config-test-{Guid.NewGuid()}.json");
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class FakeScheduler : IScheduler
    {
        public string? CreatedScheduleName { get; private set; }
        public string? DeletedScheduleName { get; private set; }
        public string? CreatedGroupName { get; private set; }
        public string? DeletedGroupName { get; private set; }
        public SchedulerScheduleRequest? CreatedRequest { get; private set; }
        public bool FailCreate { get; set; }
        public bool FailDelete { get; set; }

        public Task CreateOrUpdateScheduleAsync(
            SchedulerScheduleRequest request,
            CancellationToken cancellationToken = default)
        {
            if (FailCreate)
                throw new InvalidOperationException("scheduler create failed");

            CreatedGroupName = request.GroupName;
            CreatedScheduleName = request.ScheduleName;
            CreatedRequest = request;
            return Task.CompletedTask;
        }

        public Task DeleteScheduleAsync(
            string groupName,
            string scheduleName,
            CancellationToken cancellationToken = default)
        {
            if (FailDelete)
                throw new InvalidOperationException("scheduler delete failed");

            DeletedGroupName = groupName;
            DeletedScheduleName = scheduleName;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLambdaInvoker : ILambdaInvoker
    {
        public SeasonActivationRequest? LastRequest { get; private set; }

        public Task<SeasonActivationResponse> InvokeAsync(
            string functionName,
            SeasonActivationRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new SeasonActivationResponse
            {
                Status = request.Operation == "rollback" ? "rolled_back" : "activated",
                Operation = request.Operation,
                SeasonId = request.SeasonId,
                RevisionId = request.RevisionId,
                RevisionHash = request.RevisionHash,
                ActivatedAt = DateTimeOffset.UtcNow
            });
        }
    }

    private sealed class FakeS3Client : AmazonS3Client
    {
        private readonly Dictionary<string, StoredObject> _objects =
            new Dictionary<string, StoredObject>(StringComparer.Ordinal);
        private int _etagCounter;

        public FakeS3Client()
            : base(new AnonymousAWSCredentials(), RegionEndpoint.USEast1)
        {
        }

        public bool Contains(string key) => _objects.ContainsKey(key);
        public bool FailScheduledPut { get; set; }

        public override Task<GetObjectResponse> GetObjectAsync(
            GetObjectRequest request,
            CancellationToken cancellationToken)
        {
            if (!_objects.TryGetValue(request.Key, out StoredObject? stored))
            {
                throw new AmazonS3Exception("Not found") { StatusCode = HttpStatusCode.NotFound };
            }

            return Task.FromResult(new GetObjectResponse
            {
                ETag = stored.ETag,
                ResponseStream = new MemoryStream(stored.Content, writable: false)
            });
        }

        public override async Task<PutObjectResponse> PutObjectAsync(
            PutObjectRequest request,
            CancellationToken cancellationToken)
        {
            if (FailScheduledPut && request.Key == "season-config/v1/scheduled.json")
            {
                throw new AmazonS3Exception("scheduled pointer write failed");
            }

            if (request.IfNoneMatch == "*" && _objects.ContainsKey(request.Key))
            {
                throw new AmazonS3Exception("Precondition failed")
                {
                    StatusCode = HttpStatusCode.PreconditionFailed
                };
            }

            using MemoryStream content = new();
            await request.InputStream!.CopyToAsync(content, cancellationToken);
            _objects[request.Key] = new StoredObject(content.ToArray(), $"\"etag-{++_etagCounter}\"");
            return new PutObjectResponse { ETag = _objects[request.Key].ETag };
        }

        public override Task<DeleteObjectResponse> DeleteObjectAsync(
            DeleteObjectRequest request,
            CancellationToken cancellationToken)
        {
            _objects.Remove(request.Key);
            return Task.FromResult(new DeleteObjectResponse());
        }

        private sealed record StoredObject(byte[] Content, string ETag);
    }
}
