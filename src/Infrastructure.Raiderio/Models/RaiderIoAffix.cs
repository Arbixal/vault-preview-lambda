using System.Text.Json.Serialization;

namespace VaultPreview.RaiderIo.Models;

public class RaiderIoAffix
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    [JsonPropertyName("wowhead_url")] public string WowheadUrl { get; set; } = string.Empty;
}
