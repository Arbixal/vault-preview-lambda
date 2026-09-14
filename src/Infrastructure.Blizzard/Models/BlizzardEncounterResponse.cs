namespace VaultPreview.Blizzard.Models;

public class BlizzardEncounterResponse
{
    public BlizzardCharacter Character { get; set; } = new();
    public IList<BlizzardExpansion> Expansions { get; set; } = new List<BlizzardExpansion>();
}
