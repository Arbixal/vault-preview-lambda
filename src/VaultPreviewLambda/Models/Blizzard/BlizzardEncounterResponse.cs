namespace VaultPreviewLambda.Models.Blizzard;

public class BlizzardEncounterResponse
{
    public BlizzardCharacter Character { get; set; }
    public IList<BlizzardExpansion> Expansions { get; set; }
}