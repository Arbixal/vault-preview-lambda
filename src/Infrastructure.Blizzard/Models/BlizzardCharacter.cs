namespace VaultPreview.Blizzard.Models;

public class BlizzardCharacter: BlizzardBase
{
    public BlizzardRealm Realm { get; set; } = new();
}
