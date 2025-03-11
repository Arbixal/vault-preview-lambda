using System.Text.Json.Serialization;

namespace VaultPreview.RaiderIo.Models;

public class RaiderIoProfileResponse
{
    public string Name { get; set; }
    public string Race { get; set; }
    public string Class { get; set; }
    [JsonPropertyName("active_spec_name")] public string ActiveSpecName { get; set; }
    [JsonPropertyName("active_spec_role")] public string ActivceSpecRole { get; set; }
    public string Gender { get; set; }
    public string Faction { get; set; }
    [JsonPropertyName("achievement_points")] public int AchievementPoints { get; set; }
    [JsonPropertyName("honorable_kills")] public int HonorableKills { get; set; }
    [JsonPropertyName("thumbnail_url")] public string ThumbnailUrl { get; set; }
    public string Region { get; set; }
    public string Realm { get; set; }
    [JsonPropertyName("last_crawled_at")] public DateTime LastCrawledAt { get; set; }
    [JsonPropertyName("profile_url")] public string ProfileUrl { get; set; }
    [JsonPropertyName("profile_banner")] public string ProfileBanner { get; set; }
    [JsonPropertyName("mythic_plus_weekly_highest_level_runs")]
    public IList<RaiderIoDungeonRun>? WeeklyHighestLevelRuns { get; set; }
    
}