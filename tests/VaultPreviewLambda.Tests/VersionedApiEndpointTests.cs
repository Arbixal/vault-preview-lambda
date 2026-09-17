using System.Net;
using System.Text.Json;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.Lambda.Annotations.APIGateway;
using VaultPreview.Blizzard;
using VaultPreview.Blizzard.Models;
using VaultPreview.RaiderIo;
using VaultPreview.RaiderIo.Models;
using VaultPreview.VaultCache;
using VaultPreview.VaultCache.Models;
using VaultPreviewLambda.Calculations;
using VaultShared.Seasons;
using Xunit;

namespace VaultPreviewLambda.Tests;

public class VersionedApiEndpointTests
{
    [Fact]
    public async Task GetAppConfig_ReturnsActiveSeasonSnapshot()
    {
        SeasonRevision revision = SeasonRevision.Create("future-r1", _createConfiguration());
        Function function = _createFunction(revision);

        IHttpResult result = await function.GetAppConfig(string.Empty, "http://localhost:3000");

        SerializedHttpResponse response = _serialize(result);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=60, stale-while-revalidate=300", response.Headers["cache-control"]);
        Assert.StartsWith("\"sha256:", response.Headers["etag"]);
        Assert.Equal("http://localhost:3000", response.Headers["access-control-allow-origin"]);

        using JsonDocument body = _readBody(response);
        JsonElement root = body.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("future-season", root.GetProperty("activeSeason").GetProperty("id").GetString());
        Assert.Equal("future-r1", root.GetProperty("activeSeason").GetProperty("revision").GetString());
    }

    [Fact]
    public async Task GetAppConfig_ReturnsNotModifiedForMatchingRevisionEntityTag()
    {
        SeasonRevision revision = SeasonRevision.Create("future-r1", _createConfiguration());
        Function function = _createFunction(revision);

        IHttpResult result = await function.GetAppConfig(
            $"\"{revision.RevisionHash}\"",
            "http://localhost:3000");

        SerializedHttpResponse response = _serialize(result);
        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        Assert.Equal($"\"{revision.RevisionHash}\"", response.Headers["etag"]);
        Assert.Equal("public, max-age=60, stale-while-revalidate=300", response.Headers["cache-control"]);
    }

    [Fact]
    public async Task GetAppConfig_ReturnsStructuredUnavailableStatusWhenProviderIsMissing()
    {
        Function function = new(
            new FakeBlizzardApiHandler(),
            new FakeRaiderIoHandler(),
            new FakeVaultCacheHandler(),
            new FakeActiveSeasonRevisionProvider());

        IHttpResult result = await function.GetAppConfig(string.Empty, "http://localhost:3000");

        SerializedHttpResponse response = _serialize(result);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using JsonDocument body = _readBody(response);
        Assert.Equal(
            "ACTIVE_CONFIGURATION_UNAVAILABLE",
            body.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void GetAppConfigOptions_ReturnsCorsHeaders()
    {
        Function function = _createFunction(SeasonRevision.Create("future-r1", _createConfiguration()));

        SerializedHttpResponse response = _serialize(function.GetAppConfigOptions("http://localhost:3000"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("http://localhost:3000", response.Headers["access-control-allow-origin"]);
        Assert.Equal("GET, OPTIONS", response.Headers["access-control-allow-methods"]);
    }

    [Fact]
    public void GetVaultProgressOptions_ReturnsCorsHeaders()
    {
        Function function = _createFunction(SeasonRevision.Create("future-r1", _createConfiguration()));

        SerializedHttpResponse response = _serialize(function.GetVaultProgressOptions(
            "us",
            "realm",
            "character",
            "http://localhost:3000"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("http://localhost:3000", response.Headers["access-control-allow-origin"]);
    }

    [Fact]
    public async Task GetVaultProgress_ReturnsNormalizedResponseAndOperationalHeaders()
    {
        Function function = _createFunction(SeasonRevision.Create("future-r1", _createConfiguration()));

        SerializedHttpResponse response = _serialize(await function.GetVaultProgress(
            "us",
            "realm",
            "character",
            string.Empty,
            "http://localhost:3000"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=30, stale-while-revalidate=60", response.Headers["cache-control"]);
        Assert.StartsWith("\"sha256:", response.Headers["etag"]);
        using JsonDocument body = _readBody(response);
        JsonElement root = body.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("character", root.GetProperty("character").GetProperty("name").GetString());
        Assert.Equal("future-season", root.GetProperty("season").GetProperty("id").GetString());
        Assert.Equal("unsupported", root.GetProperty("sections")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task GetVaultProgress_ReturnsStructuredCharacterNotFoundError()
    {
        SeasonRevision revision = SeasonRevision.Create("future-r1", _createDelveConfiguration());
        Function function = _createFunction(
            revision,
            new FakeBlizzardApiHandler(HttpStatusCode.NotFound));

        SerializedHttpResponse response = _serialize(await function.GetVaultProgress(
            "us",
            "realm",
            "character",
            string.Empty,
            "http://localhost:3000"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using JsonDocument body = _readBody(response);
        Assert.Equal("CHARACTER_NOT_FOUND", body.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetVaultProgress_ReturnsStructuredUpstreamError()
    {
        SeasonRevision revision = SeasonRevision.Create("future-r1", _createDelveConfiguration());
        Function function = _createFunction(
            revision,
            new FakeBlizzardApiHandler(HttpStatusCode.BadGateway));

        SerializedHttpResponse response = _serialize(await function.GetVaultProgress(
            "us",
            "realm",
            "character",
            string.Empty,
            "http://localhost:3000"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using JsonDocument body = _readBody(response);
        Assert.Equal("UPSTREAM_UNAVAILABLE", body.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetVaultProgress_RejectsInvalidRequest()
    {
        Function function = _createFunction(SeasonRevision.Create("future-r1", _createConfiguration()));

        IHttpResult result = await function.GetVaultProgress(
            "u",
            "realm",
            "character",
            string.Empty,
            "http://localhost:3000");

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    private static Function _createFunction(
        SeasonRevision revision,
        FakeBlizzardApiHandler? blizzard = null)
    {
        blizzard ??= new FakeBlizzardApiHandler();
        return new Function(
            blizzard,
            new FakeRaiderIoHandler(),
            new FakeVaultCacheHandler(),
            new FakeActiveSeasonRevisionProvider(),
            new FakeSeasonRevisionProvider(revision),
            new VersionedProgressService(
                blizzard,
                new FakeRaiderIoHandler(),
                new BlizzardJournalMetadataProvider(blizzard),
                new VaultProgressCalculator(),
                new FakeSeasonRevisionProvider(revision),
                new FakeDelveBaselineProvider()));
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

    private static SeasonConfiguration _createDelveConfiguration()
    {
        return new SeasonConfiguration(
            "future-season",
            "Future Season",
            "Future",
            "Future Expansion",
            null,
            [
                new SeasonActivityDefinition(
                    "delves",
                    "delves",
                    "Delves",
                    "Weekly completions",
                    0,
                    [
                        new SeasonSlotDefinition(
                            "delves-slot-1",
                            "delves",
                            1,
                            "1 Delve",
                            1,
                            new SeasonRewardDefinition(500, "epic"))
                    ],
                    [])
                {
                    ProgressRules = [new SeasonProgressRule("delve-tier-1", null, 1, 500, "epic")]
                }
            ]);
    }

    private static SerializedHttpResponse _serialize(IHttpResult result)
    {
        using Stream stream = result.Serialize(new HttpResultSerializationOptions
        {
            Format = HttpResultSerializationOptions.ProtocolFormat.HttpApi,
            Version = HttpResultSerializationOptions.ProtocolVersion.V2,
            Serializer = new DefaultLambdaJsonSerializer()
        });
        using JsonDocument envelope = JsonDocument.Parse(stream);
        JsonElement root = envelope.RootElement;
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("headers", out JsonElement headerElement))
        {
            foreach (JsonProperty header in headerElement.EnumerateObject())
                headers[header.Name] = header.Value.GetString() ?? string.Empty;
        }

        string? body = root.TryGetProperty("body", out JsonElement bodyElement) &&
                       bodyElement.ValueKind == JsonValueKind.String
            ? bodyElement.GetString()
            : null;
        return new((HttpStatusCode)root.GetProperty("statusCode").GetInt32(), headers, body);
    }

    private static JsonDocument _readBody(SerializedHttpResponse response)
    {
        Assert.False(string.IsNullOrWhiteSpace(response.Body));
        return JsonDocument.Parse(response.Body!);
    }

    private sealed class FakeSeasonRevisionProvider(SeasonRevision revision) : ISeasonRevisionProvider
    {
        public Task<SeasonRevision?> GetActiveRevision(CancellationToken cancellationToken = default) =>
            Task.FromResult<SeasonRevision?>(revision);
    }

    private sealed class FakeActiveSeasonRevisionProvider : IActiveSeasonRevisionProvider
    {
        public Task<ActiveSeasonRevision?> GetActive(CancellationToken cancellationToken = default) =>
            Task.FromResult<ActiveSeasonRevision?>(null);
    }

    private sealed class FakeDelveBaselineProvider : ISeasonAwareDelveBaselineProvider
    {
        public Task<DelveBaseline?> GetBaseline(string region, string realm, string character) =>
            Task.FromResult<DelveBaseline?>(null);

        public Task SaveBaseline(string region, string realm, string character, DelveBaseline baseline) =>
            Task.CompletedTask;
    }

    private sealed class FakeRaiderIoHandler : IRaiderIoHandler
    {
        public Task<RaiderIoProfileResponse> GetWeeklyHighestLevelRuns(string region, string realm, string character) =>
            Task.FromResult(new RaiderIoProfileResponse());
    }

    private sealed class FakeBlizzardApiHandler(HttpStatusCode? statisticsStatusCode = null) : IBlizzardApiHandler
    {
        public Task Connect() => Task.CompletedTask;

        public Task<BlizzardEncounterResponse> GetEncounters(string region, string realm, string character) =>
            Task.FromResult(new BlizzardEncounterResponse());

        public Task<BlizzardJournalMetadata?> GetJournalInstance(
            string region,
            long instanceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BlizzardJournalMetadata?>(null);

        public Task<int?> GetSeason(string region) => Task.FromResult<int?>(null);

        public Task<Dictionary<int, int>> GetDelveStatistics(string region, string realm, string character)
        {
            if (statisticsStatusCode.HasValue)
            {
                throw new HttpRequestException(
                    "Upstream test failure",
                    null,
                    statisticsStatusCode.Value);
            }

            return Task.FromResult(new Dictionary<int, int>());
        }
    }

    private sealed class FakeVaultCacheHandler : IVaultCacheHandler
    {
        public Task<CharacterData?> GetCharacter(string region, string realm, string name) =>
            Task.FromResult<CharacterData?>(null);

        public Task<IList<CharacterData>> GetAllCharacters() =>
            Task.FromResult<IList<CharacterData>>([]);

        public Task<bool> SaveCharacter(CharacterData characterData) =>
            Task.FromResult(true);

        public Task<bool> DeleteCharacter(string region, string realm, string name) =>
            Task.FromResult(true);
    }

    private sealed record SerializedHttpResponse(
        HttpStatusCode StatusCode,
        IReadOnlyDictionary<string, string> Headers,
        string? Body);
}
