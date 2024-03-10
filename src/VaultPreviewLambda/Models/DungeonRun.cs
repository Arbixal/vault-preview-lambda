using System.Text.Json.Serialization;

namespace VaultPreviewLambda.Models;

public class DungeonRun
{
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
}