using System.Text.Json;

namespace VaultPreviewLambda;

public interface IApiTelemetry
{
    void Record(ApiTelemetryEvent telemetryEvent);
}

public sealed record ApiTelemetryEvent(
    string Route,
    string Outcome,
    int StatusCode,
    int SchemaVersion,
    string? SeasonId = null,
    string? Revision = null,
    string? RevisionHash = null,
    string? Freshness = null,
    string? FailureType = null);

public sealed class ConsoleApiTelemetry : IApiTelemetry
{
    public void Record(ApiTelemetryEvent telemetryEvent)
    {
        Dictionary<string, object?> payload = new()
        {
            ["_aws"] = new Dictionary<string, object?>
            {
                ["Timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["CloudWatchMetrics"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["Namespace"] = "VaultPreview/API",
                        ["Dimensions"] = new[] { new[] { "Route", "Outcome" } },
                        ["Metrics"] = new[]
                        {
                            new Dictionary<string, object?> { ["Name"] = "RequestCount", ["Unit"] = "Count" },
                            new Dictionary<string, object?> { ["Name"] = "FailureCount", ["Unit"] = "Count" }
                        }
                    }
                }
            },
            ["Event"] = "vault_preview_api",
            ["Route"] = telemetryEvent.Route,
            ["Outcome"] = telemetryEvent.Outcome,
            ["StatusCode"] = telemetryEvent.StatusCode,
            ["SchemaVersion"] = telemetryEvent.SchemaVersion,
            ["SeasonId"] = telemetryEvent.SeasonId,
            ["Revision"] = telemetryEvent.Revision,
            ["RevisionHash"] = telemetryEvent.RevisionHash,
            ["Freshness"] = telemetryEvent.Freshness,
            ["FailureType"] = telemetryEvent.FailureType,
            ["RequestCount"] = 1,
            ["FailureCount"] = string.Equals(telemetryEvent.Outcome, "error", StringComparison.Ordinal) ? 1 : 0
        };

        Console.WriteLine(JsonSerializer.Serialize(payload));
    }
}
