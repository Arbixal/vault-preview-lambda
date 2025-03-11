namespace VaultPreview.Blizzard.Models;

public class BlizzardExpansion
{
    public BlizzardBase Expansion { get; set; }
    public IList<BlizzardInstance> Instances { get; set; }
}