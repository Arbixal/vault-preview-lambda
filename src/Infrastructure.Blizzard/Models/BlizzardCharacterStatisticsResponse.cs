namespace VaultPreview.Blizzard.Models;

public class BlizzardCharacterStatisticsResponse
{
    public BlizzardCharacter Character { get; set; } = new();

    public IList<BlizzardCharacterStatisticCategory> Categories { get; set; } =
        new List<BlizzardCharacterStatisticCategory>();
}
