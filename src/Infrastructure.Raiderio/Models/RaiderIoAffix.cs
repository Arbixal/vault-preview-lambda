using System.Text.Json.Serialization;

namespace VaultPreview.RaiderIo.Models;

public class RaiderIoAffix
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Icon { get; set; }
    [JsonPropertyName("wowhead_url")] public string WowheadUrl { get; set; }
}