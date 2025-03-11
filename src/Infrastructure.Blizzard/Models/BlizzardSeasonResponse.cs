using System.Text.Json.Serialization;

namespace VaultPreview.Blizzard.Models;

public class BlizzardSeasonResponse
{
    public IList<BlizzardSeason> Seasons { get; set; }
    [JsonPropertyName("current_season")] public BlizzardSeason CurrentSeason { get; set; }
}