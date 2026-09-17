using System.Text.Json.Serialization;

namespace SeasonActivationLambda.Request;

public sealed class SeasonActivationRequest
{
    [JsonPropertyName("operation")] public string Operation { get; init; } = string.Empty;
    [JsonPropertyName("seasonId")] public string SeasonId { get; init; } = string.Empty;
    [JsonPropertyName("revisionId")] public string RevisionId { get; init; } = string.Empty;
    [JsonPropertyName("revisionHash")] public string RevisionHash { get; init; } = string.Empty;
    [JsonPropertyName("activationAt")] public DateTimeOffset? ActivationAt { get; init; }
}
