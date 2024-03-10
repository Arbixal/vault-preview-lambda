using System.Text.Json.Serialization;

namespace VaultPreviewLambda.Models.RaiderIo;

public class RaiderIoDungeonRun
{
    public string Dungeon { get; set; }
    [JsonPropertyName("short_name")] public string ShortName { get; set; }
    [JsonPropertyName("mythic_level")] public int MythicLevel { get; set; }
    [JsonPropertyName("completed_at")] public DateTime CompletedAt { get; set; }
    [JsonPropertyName("clear_time_ms")] public long ClearTimeMs { get; set; }
    [JsonPropertyName("par_time_ms")] public long ParTimeMs { get; set; }
    [JsonPropertyName("num_keystone_upgrades")] public int NumKeystoneUpgrades { get; set; }
    [JsonPropertyName("map_challenge_mode_id")] public int MapChallengeModeId { get; set; }
    [JsonPropertyName("zone_id")] public int ZoneId { get; set; }
    public float Score { get; set; }
    public string Url { get; set; }
    public IList<RaiderIoAffix> Affixes { get; set; }
}