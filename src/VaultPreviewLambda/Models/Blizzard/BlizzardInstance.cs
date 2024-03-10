namespace VaultPreviewLambda.Models.Blizzard;

public class BlizzardInstance
{
    public BlizzardBase Instance { get; set; }
    public IList<BlizzardMode> Modes { get; set; }
}