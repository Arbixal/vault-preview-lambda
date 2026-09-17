using VaultPreview.VaultCache.Models;
using VaultShared.Seasons;

namespace VaultPreview.VaultCache;

public sealed class S3DelveBaselineProvider(IVaultCacheHandler vaultCacheHandler)
    : ISeasonAwareDelveBaselineProvider
{
    public async Task<DelveBaseline?> GetBaseline(string region, string realm, string character)
    {
        CharacterData? characterData = await vaultCacheHandler.GetCharacter(region, realm, character);
        if (characterData is not { HasSeasonAwareBaseline: true })
            return null;

        return new DelveBaseline(
            characterData.SeasonId!,
            characterData.SeasonRevision!,
            characterData.SeasonRevisionHash!,
            characterData.DelvesCompleted);
    }

    public async Task SaveBaseline(
        string region,
        string realm,
        string character,
        DelveBaseline baseline)
    {
        CharacterData characterData = await vaultCacheHandler.GetCharacter(region, realm, character)
            ?? new CharacterData(character, realm, region);
        characterData.SetDelveBaseline(
            baseline.Completed.ToDictionary(x => x.Key, x => x.Value),
            new ActiveSeasonRevision(
                baseline.SeasonId,
                baseline.Revision,
                baseline.RevisionHash,
                null));

        await vaultCacheHandler.SaveCharacter(characterData);
    }
}
