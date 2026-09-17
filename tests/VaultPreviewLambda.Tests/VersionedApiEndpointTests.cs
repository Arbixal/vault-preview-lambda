using System.Net;
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

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    [Fact]
    public async Task GetAppConfig_ReturnsNotModifiedForMatchingRevisionEntityTag()
    {
        SeasonRevision revision = SeasonRevision.Create("future-r1", _createConfiguration());
        Function function = _createFunction(revision);

        IHttpResult result = await function.GetAppConfig(
            $"\"{revision.RevisionHash}\"",
            "http://localhost:3000");

        Assert.Equal(HttpStatusCode.NotModified, result.StatusCode);
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

        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
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

    private static Function _createFunction(SeasonRevision revision)
    {
        FakeBlizzardApiHandler blizzard = new();
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

    private sealed class FakeBlizzardApiHandler : IBlizzardApiHandler
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

        public Task<Dictionary<int, int>> GetDelveStatistics(string region, string realm, string character) =>
            Task.FromResult(new Dictionary<int, int>());
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
}
