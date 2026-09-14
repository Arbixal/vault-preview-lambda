namespace VaultPreview.Blizzard.Models;

public class BlizzardBase
{
    public string Name { get; set; } = string.Empty;
    public long Id { get; set; }
    public BlizzardKey Key { get; set; } = new();
}
