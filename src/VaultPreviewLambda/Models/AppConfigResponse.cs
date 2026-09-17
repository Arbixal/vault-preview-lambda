using System.Text.Json.Serialization;

namespace VaultPreviewLambda.Models;

public sealed class AppConfigResponse
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; init; } = 1;
    [JsonPropertyName("activeSeason")] public SeasonSnapshot ActiveSeason { get; init; } = new();
}
