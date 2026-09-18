using System.Text.Json.Serialization;

namespace SeasonConfigCli.Response;

public sealed class SeasonActivationResponse
{
    [JsonPropertyName("status")] public string Status { get; init; } = "activated";
    [JsonPropertyName("operation")] public string Operation { get; init; } = string.Empty;
    [JsonPropertyName("seasonId")] public string SeasonId { get; init; } = string.Empty;
    [JsonPropertyName("revisionId")] public string RevisionId { get; init; } = string.Empty;
    [JsonPropertyName("revisionHash")] public string RevisionHash { get; init; } = string.Empty;
    [JsonPropertyName("activatedAt")] public DateTimeOffset ActivatedAt { get; init; }
}
