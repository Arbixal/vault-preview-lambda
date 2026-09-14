namespace VaultPreview.Blizzard.Models;

public class BlizzardMode
{
    public BlizzardType Difficulty { get; set; } = new();
    public BlizzardType Status { get; set; } = new();
    public BlizzardProgress Progress { get; set; } = new();
}
