using VaultPreview.VaultCache;
using VaultPreview.VaultCache.Models;
using VaultShared.Seasons;
using Xunit;

namespace Infrastructure.VaultCache.Tests;

public class S3DelveBaselineProviderTests
{
    [Fact]
    public async Task GetBaseline_TreatsLegacyObjectWithoutRevisionAsFreshBaseline()
    {
        FakeVaultCacheHandler cache = new(new CharacterData("character", "realm", "us"));
        S3DelveBaselineProvider provider = new(cache);

        Assert.Null(await provider.GetBaseline("us", "realm", "character"));
    }

    [Fact]
    public async Task SaveBaseline_PersistsSeasonRevisionAndDynamicLevels()
    {
        FakeVaultCacheHandler cache = new();
        S3DelveBaselineProvider provider = new(cache);
        DelveBaseline baseline = new(
            "midnight-s2",
            "midnight-s2-r1",
            "sha256:0000000000000000000000000000000000000000000000000000000000000000",
            new Dictionary<int, int> { [12] = 3 });

        await provider.SaveBaseline("us", "realm", "character", baseline);

        Assert.NotNull(cache.SavedCharacter);
        Assert.Equal("midnight-s2", cache.SavedCharacter.SeasonId);
        Assert.Equal("midnight-s2-r1", cache.SavedCharacter.SeasonRevision);
        Assert.Equal(3, cache.SavedCharacter.DelvesCompleted[12]);
    }

    private sealed class FakeVaultCacheHandler(CharacterData? character = null) : IVaultCacheHandler
    {
        public CharacterData? SavedCharacter { get; private set; }

        public Task<CharacterData?> GetCharacter(string region, string realm, string name) =>
            Task.FromResult(character);

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
