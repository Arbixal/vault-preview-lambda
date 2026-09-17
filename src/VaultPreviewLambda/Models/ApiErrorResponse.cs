using System.Text.Json.Serialization;

namespace VaultPreviewLambda.Models;

public sealed class ApiErrorResponse
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; init; } = 1;
    [JsonPropertyName("error")] public ApiError Error { get; init; } = new();
}
