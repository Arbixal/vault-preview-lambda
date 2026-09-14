using System.Text.Json.Serialization;

namespace VaultPreview.Blizzard.Models;

public class BlizzardSeasonResponse
{
    public IList<BlizzardSeason> Seasons { get; set; } = new List<BlizzardSeason>();
    [JsonPropertyName("current_season")] public BlizzardSeason? CurrentSeason { get; set; }
}
