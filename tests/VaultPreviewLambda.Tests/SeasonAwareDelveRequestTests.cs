using VaultPreview.Blizzard;
using VaultPreview.Blizzard.Models;
using VaultPreview.RaiderIo;
using VaultPreview.RaiderIo.Models;
using VaultPreview.VaultCache;
using VaultPreview.VaultCache.Models;
using VaultPreviewLambda.Models;
using VaultShared.Seasons;
using Xunit;

namespace VaultPreviewLambda.Tests;

public class SeasonAwareDelveRequestTests
{
    [Fact]
    public async Task RequestWithMismatchedBaseline_SavesFreshBaselineAndReturnsZeroInitialProgress()
    {
        CharacterData oldBaseline = new("character", "realm", "us")
        {
            SeasonId = "old-season",
            SeasonRevision = "old-revision",
            SeasonRevisionHash = "sha256:old",
            DelvesCompleted = new Dictionary<int, int> { [8] = 99 }
        };
        FakeVaultCacheHandler cache = new(oldBaseline);

        IDictionary<string, CharacterProgress> response = await new VaultPreviewLambda.Function(
                new FakeBlizzardApiHandler(),
                new FakeRaiderIoHandler(),
                cache,
                new FakeActiveSeasonRevisionProvider())
            .FunctionHandler("us", "realm", "character");

        CharacterProgress progress = response["character-realm"];
        Assert.Equal(0, progress.Delves[8]);
        Assert.NotNull(cache.SavedCharacter);
        Assert.Equal("midnight-s2", cache.SavedCharacter.SeasonId);
        Assert.Equal("midnight-s2-r1", cache.SavedCharacter.SeasonRevision);
        Assert.Equal(5, cache.SavedCharacter.DelvesCompleted[8]);
    }

    private sealed class FakeActiveSeasonRevisionProvider : IActiveSeasonRevisionProvider
    {
        public Task<ActiveSeasonRevision?> GetActive(CancellationToken cancellationToken = default) =>
            Task.FromResult<ActiveSeasonRevision?>(new ActiveSeasonRevision(
                "midnight-s2",
                "midnight-s2-r1",
                "sha256:0000000000000000000000000000000000000000000000000000000000000000",
                18));
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

        public Task<int?> GetSeason(string region) => Task.FromResult<int?>(18);

        public Task<Dictionary<int, int>> GetDelveStatistics(string region, string realm, string character) =>
            Task.FromResult(new Dictionary<int, int> { [8] = 5 });
    }

    private sealed class FakeRaiderIoHandler : IRaiderIoHandler
    {
        public Task<RaiderIoProfileResponse> GetWeeklyHighestLevelRuns(string region, string realm, string character) =>
            Task.FromResult(new RaiderIoProfileResponse());
    }

    private sealed class FakeVaultCacheHandler(CharacterData baseline) : IVaultCacheHandler
    {
        public CharacterData? SavedCharacter { get; private set; }

        public Task<CharacterData?> GetCharacter(string region, string realm, string name) =>
            Task.FromResult<CharacterData?>(baseline);

        public Task<IList<CharacterData>> GetAllCharacters() =>
            Task.FromResult<IList<CharacterData>>([]);

        public Task<bool> SaveCharacter(CharacterData characterData)
        {
            SavedCharacter = characterData;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteCharacter(string region, string realm, string name) =>
            Task.FromResult(true);
    }
}
