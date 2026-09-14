namespace VaultPreview.Blizzard.Models;

public class BlizzardInstance
{
    public BlizzardBase Instance { get; set; } = new();
    public IList<BlizzardMode> Modes { get; set; } = new List<BlizzardMode>();
}
