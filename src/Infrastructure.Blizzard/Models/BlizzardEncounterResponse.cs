namespace VaultPreview.Blizzard.Models;

public class BlizzardEncounterResponse
{
    public BlizzardCharacter Character { get; set; }
    public IList<BlizzardExpansion> Expansions { get; set; }
}