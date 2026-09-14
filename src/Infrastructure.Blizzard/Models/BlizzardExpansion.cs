namespace VaultPreview.Blizzard.Models;

public class BlizzardExpansion
{
    public BlizzardBase Expansion { get; set; } = new();
    public IList<BlizzardInstance> Instances { get; set; } = new List<BlizzardInstance>();
}
