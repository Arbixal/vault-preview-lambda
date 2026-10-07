using VaultPreview.Blizzard;
using VaultPreview.Blizzard.Models;
using VaultPreview.RaiderIo;
using VaultPreview.RaiderIo.Models;
using VaultPreview.VaultCache;
using VaultPreview.VaultCache.Models;
using VaultPreviewLambda.Calculations;
using VaultPreviewLambda.Models;
using VaultShared.Seasons;
using Xunit;

namespace VaultPreviewLambda.Tests;

public class ScheduledRequestParityTests
{
    [Fact]
    public async Task ScheduledRefreshBaselineIsConsumedByVersionedRequestPath()
    {
        SeasonRevision revision = SeasonRevision.Create("future-r1", _createDelveConfiguration());
        SharedCharacterCache cache = new(new CharacterData("character", "realm", "us"));
        SharedBlizzardApiHandler blizzard = new(new Dictionary<int, int> { [1] = 10 });
        ActiveSeasonProvider activeSeason = new(revision);

        await new CharacterDataLambda.Function(blizzard, cache, activeSeason)
            .FunctionHandler(string.Empty, null!);

        Assert.Equal(revision.Configuration.Id, cache.Character.SeasonId);
        Assert.Equal(revision.Id, cache.Character.SeasonRevision);
        Assert.Equal(revision.RevisionHash, cache.Character.SeasonRevisionHash);

        VersionedProgressService service = new(
            blizzard,
            new EmptyRaiderIoHandler(),
            new BlizzardJournalMetadataProvider(blizzard),
            new VaultProgressCalculator(),
            new SeasonRevisionProvider(revision),
            new SharedBaselineProvider(cache));

        VaultProgressResponse initial = (await service.Calculate("us", "realm", "character"))!;
        Assert.Equal(0, _getDelveProgress(initial));

        blizzard.Statistics = new Dictionary<int, int> { [1] = 12 };
        VaultProgressResponse changed = (await service.Calculate("us", "realm", "character"))!;

        Assert.Equal(2, _getDelveProgress(changed));
    }

    private static int _getDelveProgress(VaultProgressResponse response)
    {
        VaultSection section = response.Sections.Single(section => section.Kind == "delves");
        return section.Slots.Single().Progress.Completed ?? 0;
    }

    private static SeasonConfiguration _createDelveConfiguration() => new(
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
                [new SeasonSlotDefinition(
                    "delves-slot-1",
                    "delves",
                    1,
                    "1 Delve",
                    1,
                    new SeasonRewardDefinition(500, "epic"))],
                [])
            {
                ProgressRules = [new SeasonProgressRule("delve-tier-1", null, 1, 500, "epic")]
            }
        ]);

    private sealed class ActiveSeasonProvider(SeasonRevision revision) : IActiveSeasonRevisionProvider
    {
        public Task<ActiveSeasonRevision?> GetActive(CancellationToken cancellationToken = default) =>
            Task.FromResult<ActiveSeasonRevision?>(new ActiveSeasonRevision(
                revision.Configuration.Id,
                revision.Id,
                revision.RevisionHash,
                revision.Configuration.SourceSeasonId));
    }

    private sealed class SeasonRevisionProvider(SeasonRevision revision) : ISeasonRevisionProvider
    {
        public Task<SeasonRevision?> GetActiveRevision(CancellationToken cancellationToken = default) =>
            Task.FromResult<SeasonRevision?>(revision);
    }

    private sealed class SharedCharacterCache(CharacterData character) : IVaultCacheHandler
    {
        public CharacterData Character { get; private set; } = character;

        public Task<CharacterData?> GetCharacter(string region, string realm, string name) =>
            Task.FromResult<CharacterData?>(Character);

        public Task<IList<CharacterData>> GetAllCharacters() =>
            Task.FromResult<IList<CharacterData>>([Character]);

        public Task<bool> SaveCharacter(CharacterData characterData)
        {
            Character = characterData;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteCharacter(string region, string realm, string name) =>
            Task.FromResult(true);
    }

    private sealed class SharedBaselineProvider(SharedCharacterCache cache) : ISeasonAwareDelveBaselineProvider
    {
        public Task<DelveBaseline?> GetBaseline(string region, string realm, string character)
        {
            CharacterData stored = cache.Character;
            if (!stored.HasSeasonAwareBaseline)
                return Task.FromResult<DelveBaseline?>(null);

            return Task.FromResult<DelveBaseline?>(new DelveBaseline(
                stored.SeasonId!,
                stored.SeasonRevision!,
                stored.SeasonRevisionHash!,
                stored.DelvesCompleted));
        }

        public Task SaveBaseline(
            string region,
            string realm,
            string character,
            DelveBaseline baseline) => Task.CompletedTask;
    }

    private sealed class SharedBlizzardApiHandler(Dictionary<int, int> statistics) : IBlizzardApiHandler
    {
        public Dictionary<int, int> Statistics { get; set; } = statistics;

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
            Task.FromResult(new Dictionary<int, int>(Statistics));
    }

    private sealed class EmptyRaiderIoHandler : IRaiderIoHandler
    {
        public Task<RaiderIoProfileResponse> GetWeeklyHighestLevelRuns(string region, string realm, string character) =>
            Task.FromResult(new RaiderIoProfileResponse());
    }
}
