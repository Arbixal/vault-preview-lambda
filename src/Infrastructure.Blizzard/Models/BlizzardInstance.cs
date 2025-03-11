namespace VaultPreview.Blizzard.Models;

public class BlizzardInstance
{
    public BlizzardBase Instance { get; set; }
    public IList<BlizzardMode> Modes { get; set; }
}