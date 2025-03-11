using VaultShared.Models.Blizzard;

namespace VaultPreviewLambda.Models.Blizzard;

public class BlizzardCharacter: BlizzardBase
{
    public BlizzardRealm Realm { get; set; }
}