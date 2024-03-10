using System.Text.Json.Serialization;

namespace VaultPreviewLambda.Models.Blizzard;

public class BlizzardEncounter
{
    public BlizzardBase Encounter { get; set; }
    [JsonPropertyName("completed_count")] public int CompletedCount { get; set; }
    [JsonPropertyName("last_kill_timestamp")] public long LastKillTimestamp { get; set; }
}