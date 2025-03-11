using System.Text.Json.Serialization;

namespace VaultPreview.Blizzard.Models;

public class BlizzardEncounter
{
    public BlizzardBase Encounter { get; set; }
    [JsonPropertyName("completed_count")] public int CompletedCount { get; set; }
    [JsonPropertyName("last_kill_timestamp")] public long LastKillTimestamp { get; set; }
}