using System.Text.Json.Serialization;

namespace VaultPreviewLambda.Models.Blizzard;

public class BlizzardProgress
{
    [JsonPropertyName("completed_count")] public int CompletedCount { get; set; }
    [JsonPropertyName("total_count")] public int TotalCount { get; set; }
    public IList<BlizzardEncounter> Encounters { get; set; }
}