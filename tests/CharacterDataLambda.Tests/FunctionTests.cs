using System.Net;
using System.Net.Http;
using VaultPreview.Blizzard;
using VaultPreview.Blizzard.Models;
using VaultPreview.VaultCache;
using VaultPreview.VaultCache.Models;
using Xunit;

namespace CharacterDataLambda.Tests;

public class FunctionTests
{
    [Fact]
    public async Task FunctionHandler_RemovesMissingCharacterAndContinues()
    {
        CharacterData missingCharacter = new("renamed-character", "realm", "us");
        CharacterData availableCharacter = new("available-character", "realm", "us");
        FakeBlizzardApiHandler blizzardApiHandler = new(missingCharacter.Name!);
        FakeVaultCacheHandler vaultCacheHandler = new(missingCharacter, availableCharacter);

        string result = await new CharacterDataLambda.Function(blizzardApiHandler, vaultCacheHandler)
            .FunctionHandler(string.Empty, null!);

        Assert.Equal("0k", result);
        Assert.Equal([missingCharacter.Name!, availableCharacter.Name!], blizzardApiHandler.RequestedCharacters);
        Assert.Single(vaultCacheHandler.DeletedCharacters);
        Assert.Equal(missingCharacter.FullName, vaultCacheHandler.DeletedCharacters[0].FullName);
        Assert.Single(vaultCacheHandler.SavedCharacters);
        Assert.Equal(availableCharacter.FullName, vaultCacheHandler.SavedCharacters[0].FullName);
    }

    private sealed class FakeBlizzardApiHandler(string missingCharacter) : IBlizzardApiHandler
    {
        public IList<string> RequestedCharacters { get; } = [];

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
            RequestedCharacters.Add(character);
            if (character == missingCharacter)
            {
                throw new HttpRequestException("Character not found", null, HttpStatusCode.NotFound);
            }

            return Task.FromResult(Enumerable.Range(1, 11).ToDictionary(tier => tier, tier => tier));
        }
    }

    private sealed class FakeVaultCacheHandler(params CharacterData[] characters) : IVaultCacheHandler
    {
        public IList<CharacterData> SavedCharacters { get; } = [];
        public IList<CharacterData> DeletedCharacters { get; } = [];

        public Task<CharacterData?> GetCharacter(string region, string realm, string name) =>
            Task.FromResult<CharacterData?>(null);

        public Task<IList<CharacterData>> GetAllCharacters() => Task.FromResult<IList<CharacterData>>(characters);

        public Task<bool> SaveCharacter(CharacterData characterData)
        {
            SavedCharacters.Add(characterData);
            return Task.FromResult(true);
        }

        public Task<bool> DeleteCharacter(string region, string realm, string name)
        {
            DeletedCharacters.Add(new CharacterData(name, realm, region));
            return Task.FromResult(true);
        }
    }
}
