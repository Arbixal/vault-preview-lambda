using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Runtime;
using VaultPreview.VaultCache;
using Xunit;

namespace Infrastructure.VaultCache.Tests;

public class VaultCacheHandlerTests
{
    [Fact]
    public async Task GetAllCharacters_IgnoresNestedConfigurationAndMetadataObjects()
    {
        FakeS3Client s3Client = new()
        {
            Objects =
            [
                new S3Object { Key = "us-realm-character.json" },
                new S3Object { Key = "journal-metadata/v1/us/static-us/40766.json" },
                new S3Object { Key = "season-config/v1/active.json" },
                new S3Object { Key = "season-config/v1/revisions/future-season/future-season-r1.json" }
            ]
        };

        IList<VaultPreview.VaultCache.Models.CharacterData> characters =
            await new VaultCacheHandler(s3Client).GetAllCharacters();

        Assert.Single(characters);
        Assert.Equal("character", characters[0].Name);
        Assert.Equal("realm", characters[0].Realm);
        Assert.Equal("us", characters[0].Region);
        Assert.Equal("/", s3Client.LastListRequest?.Delimiter);
    }

    private sealed class FakeS3Client : AmazonS3Client
    {
        public FakeS3Client()
            : base(new AnonymousAWSCredentials(), RegionEndpoint.USEast1)
        {
        }

        public List<S3Object> Objects { get; init; } = [];
        public ListObjectsRequest? LastListRequest { get; private set; }

        public override Task<ListObjectsResponse> ListObjectsAsync(
            ListObjectsRequest request,
            CancellationToken cancellationToken)
        {
            LastListRequest = request;
            return Task.FromResult(new ListObjectsResponse
            {
                S3Objects = Objects
            });
        }
    }
}
