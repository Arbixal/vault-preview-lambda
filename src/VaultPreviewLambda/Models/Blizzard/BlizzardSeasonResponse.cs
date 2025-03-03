using System.Text.Json.Serialization;

namespace VaultPreviewLambda.Models.Blizzard;

public class BlizzardSeasonResponse
{
    public IList<BlizzardSeason> Seasons { get; set; }
    [JsonPropertyName("current_season")] public BlizzardSeason CurrentSeason { get; set; }
}