using System.Text.Json.Serialization;

namespace VaultPreview.Blizzard.Models;

public class BlizzardCharacterStatisticCategory
{
    public int Id { get; set; } = 0;
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("sub_categories")] public IList<BlizzardCharacterStatisticCategory>? SubCategories { get; set; } = null;
    public IList<BlizzardCharacterStatistic> Statistics { get; set; } = new List<BlizzardCharacterStatistic>();
}