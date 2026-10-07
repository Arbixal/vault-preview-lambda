using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using System.Text.Json;
using VaultPreview.VaultCache;
using VaultPreview.VaultCache.Models;
using Xunit;

namespace Infrastructure.VaultCache.Tests;

public class VaultCacheHandlerTests
{
    [Fact]
    public async Task GetAllCharacters_DeserializesCharacters_IgnoresNestedObjects_AndPaginates()
    {
        FakeS3Client s3Client = new()
        {
            Pages =
            [
                new ListObjectsV2Response
                {
                    IsTruncated = true,
                    NextContinuationToken = "page-2",
                    S3Objects =
                    [
                        new S3Object { Key = "us-tarren-mill-character.json" },
                        new S3Object { Key = "journal-metadata/v1/us/static-us/40766.json" },
                        new S3Object { Key = "season-config/v1/active.json" }
                    ]
                },
                new ListObjectsV2Response
                {
                    S3Objects =
                    [
                        new S3Object { Key = "eu-hyjal-another-character.json" },
                        new S3Object { Key = "season-config/v1/revisions/future-season/future-season-r1.json" }
                    ]
                }
            ]
        };
        s3Client.StoreCharacter(
            "us-tarren-mill-character.json",
            new CharacterData("character", "tarren-mill", "us"));
        s3Client.StoreCharacter(
            "eu-hyjal-another-character.json",
            new CharacterData("another-character", "hyjal", "eu"));

        IList<CharacterData> characters = await new VaultCacheHandler(s3Client).GetAllCharacters();

        Assert.Equal(2, characters.Count);
        Assert.Contains(characters, character =>
            character.Name == "character" &&
            character.Realm == "tarren-mill" &&
            character.Region == "us");
        Assert.Contains(characters, character =>
            character.Name == "another-character" &&
            character.Realm == "hyjal" &&
            character.Region == "eu");
        Assert.Equal(2, s3Client.ListRequests.Count);
        Assert.All(s3Client.ListRequests, request => Assert.Equal("/", request.Delimiter));
        Assert.Null(s3Client.ListRequests[0].ContinuationToken);
        Assert.Equal("page-2", s3Client.ListRequests[1].ContinuationToken);
        Assert.Equal(
            ["us-tarren-mill-character.json", "eu-hyjal-another-character.json"],
            s3Client.RequestedObjectKeys);
    }

    private sealed class FakeS3Client : AmazonS3Client
    {
        public FakeS3Client()
            : base(new AnonymousAWSCredentials(), RegionEndpoint.USEast1)
        {
        }

        public List<ListObjectsV2Response> Pages { get; init; } = [];
        public Dictionary<string, CharacterData> Characters { get; } = new(StringComparer.Ordinal);
        public IList<ListObjectsV2Request> ListRequests { get; } = [];
        public IList<string> RequestedObjectKeys { get; } = [];

        public void StoreCharacter(string key, CharacterData character) => Characters[key] = character;

        public override Task<ListObjectsV2Response> ListObjectsV2Async(
            ListObjectsV2Request request,
            CancellationToken cancellationToken)
        {
            ListRequests.Add(request);
            return Task.FromResult(Pages[ListRequests.Count - 1]);
        }

        public override Task<GetObjectResponse> GetObjectAsync(
            GetObjectRequest request,
            CancellationToken cancellationToken)
        {
            RequestedObjectKeys.Add(request.Key);
            if (!Characters.TryGetValue(request.Key, out CharacterData? character))
                throw new AmazonS3Exception("Not found");

            return Task.FromResult(new GetObjectResponse
            {
                ResponseStream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(character))
            });
        }
    }
}
