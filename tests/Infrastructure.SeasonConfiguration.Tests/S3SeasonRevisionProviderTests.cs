using System.Net;
using System.Text;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using VaultShared.Seasons;
using VaultPreview.SeasonConfigurationInfrastructure;
using Xunit;
using SeasonConfigurationModel = VaultShared.Seasons.SeasonConfiguration;

namespace Infrastructure.SeasonConfiguration.Tests;

public class S3SeasonRevisionProviderTests
{
    [Fact]
    public void IsValid_AcceptsRevisionWithMatchingValidatedHash()
    {
        SeasonConfigurationModel configuration = _createConfiguration();
        SeasonRevision revision = SeasonRevision.Create("future-r1", configuration);
        SeasonRevisionDocument document = new()
        {
            Id = revision.Id,
            Configuration = revision.Configuration,
            RevisionHash = revision.RevisionHash
        };

        Assert.True(S3SeasonRevisionProvider.IsValid(document));
    }

    [Fact]
    public void IsValid_RejectsHashMismatchAndInvalidConfiguration()
    {
        SeasonRevision revision = SeasonRevision.Create("future-r1", _createConfiguration());
        SeasonRevisionDocument hashMismatch = new()
        {
            Id = revision.Id,
            Configuration = revision.Configuration,
            RevisionHash = "sha256:0000000000000000000000000000000000000000000000000000000000000000"
        };
        SeasonRevisionDocument invalidConfiguration = new()
        {
            Id = revision.Id,
            Configuration = revision.Configuration with { DisplayName = string.Empty },
            RevisionHash = revision.RevisionHash
        };

        Assert.False(S3SeasonRevisionProvider.IsValid(hashMismatch));
        Assert.False(S3SeasonRevisionProvider.IsValid(invalidConfiguration));
    }

    [Fact]
    public void GetRevisionKey_UsesTheVersionedSeasonConfigurationPrefix()
    {
        Assert.Equal(
            "season-config/v1/revisions/future-season/future-r1.json",
            S3SeasonRevisionProvider.GetRevisionKey("future-season", "future-r1"));
    }

    [Fact]
    public void SelectActivePointer_DoesNotPromotePendingFutureActivation()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ActiveSeasonPointer pending = new()
        {
            SeasonId = "future-season",
            Revision = "future-r1",
            RevisionHash = "sha256:0000000000000000000000000000000000000000000000000000000000000000",
            ActivationAt = now.AddHours(1)
        };

        Assert.Null(S3SeasonRevisionProvider.SelectActivePointer(pending, now));
        Assert.Same(pending, S3SeasonRevisionProvider.SelectActivePointer(pending, now.AddHours(1)));
    }

    [Fact]
    public async Task ActivateAndRollback_AreIdempotentAndClearPendingSchedule()
    {
        FakeS3Client s3Client = new();
        S3SeasonRevisionProvider provider = new(s3Client);
        SeasonRevision first = SeasonRevision.Create("future-r1", _createConfiguration());
        SeasonRevision second = SeasonRevision.Create(
            "future-r2",
            _createConfiguration() with { ShortLabel = "Future Revised" });

        await provider.SaveRevision(first);
        await provider.SaveRevision(second);
        await provider.Activate("future-season", first.Id);
        await provider.Activate("future-season", first.Id);
        await provider.Schedule("future-season", second.Id, DateTimeOffset.UtcNow.AddHours(1));
        await provider.Rollback("future-season", first.Id);

        Assert.Equal(first.Id, (await provider.GetActiveRevision())?.Id);
        Assert.False(s3Client.Contains("season-config/v1/scheduled.json"));
    }

    [Fact]
    public async Task Activate_RejectsAStaleActivePointerEtag()
    {
        FakeS3Client s3Client = new();
        S3SeasonRevisionProvider provider = new(s3Client);
        SeasonRevision first = SeasonRevision.Create("future-r1", _createConfiguration());
        SeasonRevision second = SeasonRevision.Create(
            "future-r2",
            _createConfiguration() with { ShortLabel = "Future Revised" });

        await provider.SaveRevision(first);
        await provider.SaveRevision(second);
        await provider.Activate("future-season", first.Id);
        s3Client.MutateBeforeNextConditionalPut = true;

        await Assert.ThrowsAsync<AmazonS3Exception>(() =>
            provider.Activate("future-season", second.Id));

        Assert.Equal(first.Id, (await provider.GetActiveRevision())?.Id);
    }

    [Fact]
    public async Task GetScheduled_RejectsCorruptPendingPointer()
    {
        FakeS3Client s3Client = new();
        S3SeasonRevisionProvider provider = new(s3Client);
        s3Client.StoreRaw("season-config/v1/scheduled.json", "{}");

        await Assert.ThrowsAsync<InvalidDataException>(() => provider.GetScheduled());
    }

    private static SeasonConfigurationModel _createConfiguration()
    {
        return new SeasonConfigurationModel(
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

    private sealed class FakeS3Client : AmazonS3Client
    {
        private readonly IDictionary<string, StoredObject> _objects =
            new Dictionary<string, StoredObject>(StringComparer.Ordinal);
        private int _etagCounter;

        public FakeS3Client()
            : base(new AnonymousAWSCredentials(), RegionEndpoint.USEast1)
        {
        }

        public bool MutateBeforeNextConditionalPut { get; set; }

        public bool Contains(string key) => _objects.ContainsKey(key);

        public void StoreRaw(string key, string content) =>
            _objects[key] = new StoredObject(Encoding.UTF8.GetBytes(content), $"\"etag-{++_etagCounter}\"");

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
            if (MutateBeforeNextConditionalPut && request.IfMatch != null)
            {
                MutateBeforeNextConditionalPut = false;
                if (_objects.TryGetValue(request.Key, out StoredObject? concurrentValue))
                {
                    _objects[request.Key] = concurrentValue with
                    {
                        ETag = $"\"etag-{++_etagCounter}\""
                    };
                }
            }

            _objects.TryGetValue(request.Key, out StoredObject? existing);
            if (request.IfNoneMatch == "*" && existing != null ||
                request.IfMatch != null && (existing == null || existing.ETag != request.IfMatch))
            {
                throw new AmazonS3Exception("Precondition failed")
                {
                    StatusCode = HttpStatusCode.PreconditionFailed
                };
            }

            using MemoryStream content = new();
            await request.InputStream!.CopyToAsync(content, cancellationToken);
            _objects[request.Key] = new StoredObject(
                content.ToArray(),
                $"\"etag-{++_etagCounter}\"");
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
