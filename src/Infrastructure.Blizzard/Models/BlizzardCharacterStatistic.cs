using System.Text.Json.Serialization;

namespace VaultPreview.Blizzard.Models;

public class BlizzardCharacterStatistic
{
    public long Id { get; set; }
    public string Name { get; set; }
    [JsonPropertyName("last_updated_timestamp")] public long LastUpdatedTimestamp { get; set; }
    public double Quantity { get; set; }

    public DateTimeOffset LastUpdated => DateTimeOffset.FromUnixTimeMilliseconds(LastUpdatedTimestamp);
}