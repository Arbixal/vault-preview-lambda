using System.Text.Json.Serialization;

namespace VaultPreview.RaiderIo.Models;

public class RaiderIoProfileResponse
{
    public string Name { get; set; } = string.Empty;
    public string Race { get; set; } = string.Empty;
    public string Class { get; set; } = string.Empty;
    [JsonPropertyName("active_spec_name")] public string ActiveSpecName { get; set; } = string.Empty;
    [JsonPropertyName("active_spec_role")] public string ActivceSpecRole { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string Faction { get; set; } = string.Empty;
    [JsonPropertyName("achievement_points")] public int AchievementPoints { get; set; }
    [JsonPropertyName("honorable_kills")] public int HonorableKills { get; set; }
    [JsonPropertyName("thumbnail_url")] public string ThumbnailUrl { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Realm { get; set; } = string.Empty;
    [JsonPropertyName("last_crawled_at")] public DateTime LastCrawledAt { get; set; }
    [JsonPropertyName("profile_url")] public string ProfileUrl { get; set; } = string.Empty;
    [JsonPropertyName("profile_banner")] public string ProfileBanner { get; set; } = string.Empty;
    [JsonPropertyName("mythic_plus_weekly_highest_level_runs")]
    public IList<RaiderIoDungeonRun>? WeeklyHighestLevelRuns { get; set; }
    
}
