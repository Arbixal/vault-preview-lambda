using System.Text.Json.Serialization;

namespace VaultPreviewLambda.Models;

public class CharacterProgress
{
    [JsonPropertyName("raid")] public IDictionary<string, BossProgress> Raid { get; set; } = new Dictionary<string, BossProgress>();
    [JsonPropertyName("dungeons")] public IList<DungeonRun> Dungeons { get; set; } = new List<DungeonRun>();
}