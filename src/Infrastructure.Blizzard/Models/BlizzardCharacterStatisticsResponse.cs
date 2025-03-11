namespace VaultPreview.Blizzard.Models;

public class BlizzardCharacterStatisticsResponse
{
    public BlizzardCharacter Character { get; set; }

    public IList<BlizzardCharacterStatisticCategory> Categories { get; set; } =
        new List<BlizzardCharacterStatisticCategory>();
}