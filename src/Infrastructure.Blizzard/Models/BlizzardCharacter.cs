namespace VaultPreview.Blizzard.Models;

public class BlizzardCharacter: BlizzardBase
{
    public string Name { get; set; }
    public long Id { get; set; }
    public BlizzardRealm Realm { get; set; }
}