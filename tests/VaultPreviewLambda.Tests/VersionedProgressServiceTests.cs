using VaultPreview.Blizzard;
using VaultPreview.Blizzard.Models;
using VaultPreview.RaiderIo;
using VaultPreview.RaiderIo.Models;
using VaultPreview.VaultCache;
using VaultPreviewLambda.Calculations;
using VaultShared.Seasons;
using Xunit;

namespace VaultPreviewLambda.Tests;

public class VersionedProgressServiceTests
{
    [Fact]
    public async Task Calculate_ReturnsDirectCharacterAndFutureActivitySection()
    {
        SeasonRevision revision = SeasonRevision.Create("future-r1", _createConfiguration());
        VersionedProgressService service = new(
            new FakeBlizzardApiHandler(),
            new FakeRaiderIoHandler(),
            new BlizzardJournalMetadataProvider(new FakeBlizzardApiHandler()),
            new VaultProgressCalculator(),
            new FakeSeasonRevisionProvider(revision),
            new FakeDelveBaselineProvider());

        VaultPreviewLambda.Models.VaultProgressResponse response =
            Assert.IsType<VaultPreviewLambda.Models.VaultProgressResponse>(
                await service.Calculate("us", "realm", "character"));

        Assert.Equal("character", response.Character.Name);
        Assert.Equal("future-season", response.Season.Id);
        Assert.Equal("future-r1", response.Season.Revision);
        Assert.Single(response.Sections);
        Assert.Equal("unsupported", response.Sections[0].Status);
    }

    [Fact]
    public async Task Calculate_ReturnsNullWhenNoActiveRevisionExists()
    {
        FakeBlizzardApiHandler blizzard = new();
        VersionedProgressService service = new(
            blizzard,
            new FakeRaiderIoHandler(),
            new BlizzardJournalMetadataProvider(blizzard),
            new VaultProgressCalculator(),
            new FakeSeasonRevisionProvider(null),
            new FakeDelveBaselineProvider());

        Assert.Null(await service.Calculate("us", "realm", "character"));
        Assert.False(blizzard.Connected);
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

    private sealed class FakeSeasonRevisionProvider(SeasonRevision? revision) : ISeasonRevisionProvider
    {
        public Task<SeasonRevision?> GetActiveRevision(CancellationToken cancellationToken = default) =>
            Task.FromResult(revision);
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
        public bool Connected { get; private set; }

        public Task Connect()
        {
            Connected = true;
            return Task.CompletedTask;
        }

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
}
