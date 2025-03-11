namespace VaultPreview.Blizzard.Models;

public class BlizzardRealm: BlizzardBase
{
    public BlizzardKey Key { get; set; }
    public string Name { get; set; }
    public int Id { get; set; }
    public string Slug { get; set; }
}