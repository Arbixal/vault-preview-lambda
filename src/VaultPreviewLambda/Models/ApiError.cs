using System.Text.Json.Serialization;

namespace VaultPreviewLambda.Models;

public sealed class ApiError
{
    [JsonPropertyName("code")] public string Code { get; init; } = string.Empty;
    [JsonPropertyName("message")] public string Message { get; init; } = string.Empty;
    [JsonPropertyName("requestId")] public string? RequestId { get; init; }
}
