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
        SeasonRevision revision = SeasonRevision.Create("future-r1", configuration);

        SeasonConfigurationCli cli = CreateCli(s3Client);

        int result = await cli.PublishAsync(
            WriteTempFile(Serialize(configuration)),
            "future-season",
            "future-r1");

        Assert.Equal(0, result);
        Assert.True(s3Client.Contains("season-config/v1/revisions/future-season/future-r1.json"));
    }

    [Fact]
    public async Task Publish_SeasonIdMismatch_ReturnsOne()
    {
        SeasonConfiguration configuration = _createConfiguration();
        SeasonConfigurationCli cli = CreateCli();

        int result = await cli.PublishAsync(
            WriteTempFile(Serialize(configuration)),
            "wrong-season",
            "future-r1");

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task Schedule_FutureActivation_WritesScheduledPointer()
    {
        FakeS3Client s3Client = new();
        SeasonConfiguration configuration = _createConfiguration();
        SeasonConfigurationCli cli = CreateCli(s3Client);

        await cli.PublishAsync(
            WriteTempFile(Serialize(configuration)),
            "future-season",
            "future-r1");

        DateTimeOffset activationAt = DateTimeOffset.UtcNow.AddHours(1);
        int result = await cli.ScheduleAsync("future-season", "future-r1", activationAt);

        Assert.Equal(0, result);
        Assert.True(s3Client.Contains("season-config/v1/scheduled.json"));
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
        SeasonConfiguration configuration = _createConfiguration();
        SeasonConfigurationCli cli = CreateCli(s3Client);

        await cli.PublishAsync(
            WriteTempFile(Serialize(configuration)),
            "future-season",
            "future-r1");
        await cli.ScheduleAsync("future-season", "future-r1", DateTimeOffset.UtcNow.AddHours(1));
        Assert.True(s3Client.Contains("season-config/v1/scheduled.json"));

        int result = await cli.CancelAsync();

        Assert.Equal(0, result);
        Assert.False(s3Client.Contains("season-config/v1/scheduled.json"));
    }

    [Fact]
    public async Task Activate_WithLambdaInvoker_InvokesLambda()
    {
        FakeS3Client s3Client = new();
        FakeLambdaInvoker lambdaInvoker = new();
        SeasonConfiguration configuration = _createConfiguration();
        SeasonConfigurationCli cli = CreateCli(s3Client, lambdaInvoker, "test-function");

        await cli.PublishAsync(
            WriteTempFile(Serialize(configuration)),
            "future-season",
            "future-r1");

        int result = await cli.ActivateAsync("future-season", "future-r1", null);

        Assert.Equal(0, result);
        Assert.Equal("future-r1", lambdaInvoker.LastRequest?.RevisionId);
        Assert.Equal("activate", lambdaInvoker.LastRequest?.Operation);
    }

    [Fact]
    public async Task Rollback_WithLambdaInvoker_InvokesLambda()
    {
        FakeS3Client s3Client = new();
        FakeLambdaInvoker lambdaInvoker = new();
        SeasonConfiguration configuration = _createConfiguration();
        SeasonConfigurationCli cli = CreateCli(s3Client, lambdaInvoker, "test-function");

        await cli.PublishAsync(
            WriteTempFile(Serialize(configuration)),
            "future-season",
            "future-r1");

        int result = await cli.RollbackAsync("future-season", "future-r1", null);

        Assert.Equal(0, result);
        Assert.Equal("future-r1", lambdaInvoker.LastRequest?.RevisionId);
        Assert.Equal("rollback", lambdaInvoker.LastRequest?.Operation);
    }

    [Fact]
    public async Task Activate_WithoutLambdaInvoker_UsesStoreDirectly()
    {
        FakeS3Client s3Client = new();
        SeasonConfiguration configuration = _createConfiguration();
        SeasonConfigurationCli cli = CreateCli(s3Client, null, null);

        await cli.PublishAsync(
            WriteTempFile(Serialize(configuration)),
            "future-season",
            "future-r1");

        int result = await cli.ActivateAsync("future-season", "future-r1", null);

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task Rollback_WithoutLambdaInvoker_UsesStoreDirectly()
    {
        FakeS3Client s3Client = new();
        SeasonConfiguration configuration = _createConfiguration();
        SeasonConfigurationCli cli = CreateCli(s3Client, null, null);

        await cli.PublishAsync(
            WriteTempFile(Serialize(configuration)),
            "future-season",
            "future-r1");

        int result = await cli.RollbackAsync("future-season", "future-r1", null);

        Assert.Equal(0, result);
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
    public async Task Validate_NonExistentFile_Throws()
    {
        SeasonConfigurationCli cli = CreateCli();

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => cli.ValidateAsync("/nonexistent/path.json"));
    }

    [Fact]
    public async Task Schedule_WithLambdaInvoker_UsesStoreSchedule()
    {
        FakeS3Client s3Client = new();
        SeasonConfiguration configuration = _createConfiguration();
        SeasonConfigurationCli cli = CreateCli(s3Client, null, null);

        await cli.PublishAsync(
            WriteTempFile(Serialize(configuration)),
            "future-season",
            "future-r1");
        DateTimeOffset activationAt = DateTimeOffset.UtcNow.AddHours(1);

        int result = await cli.ScheduleAsync("future-season", "future-r1", activationAt);

        Assert.Equal(0, result);
    }

    private SeasonConfigurationCli CreateCli(
        FakeS3Client? s3Client = null,
        FakeLambdaInvoker? lambdaInvoker = null,
        string? functionName = null)
    {
        s3Client ??= new FakeS3Client();
        ISeasonRevisionStore store = new S3SeasonRevisionProvider(s3Client);
        return new SeasonConfigurationCli(store, lambdaInvoker, functionName);
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
                Status = "activated",
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
