using System.Text.Json;
using Amazon.Lambda;
using Amazon.Lambda.Model;
using SeasonConfigCli.Request;
using SeasonConfigCli.Response;

namespace SeasonConfigCli;

public sealed class LambdaInvoker : ILambdaInvoker
{
    private readonly IAmazonLambda _lambdaClient;

    public LambdaInvoker(IAmazonLambda lambdaClient)
    {
        _lambdaClient = lambdaClient;
    }

    public async Task<SeasonActivationResponse> InvokeAsync(
        string functionName,
        SeasonActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        string payload = JsonSerializer.Serialize(request);
        InvokeResponse response = await _lambdaClient.InvokeAsync(
            new InvokeRequest
            {
                FunctionName = functionName,
                Payload = payload,
                InvocationType = InvocationType.RequestResponse
            },
            cancellationToken);

        string responsePayload = await _readPayloadAsync(response.Payload, cancellationToken);
        if (!string.IsNullOrWhiteSpace(response.FunctionError))
        {
            throw new InvalidOperationException(
                $"Lambda function error ({response.FunctionError}): {responsePayload}");
        }

        if (string.IsNullOrWhiteSpace(responsePayload))
            throw new InvalidOperationException("Activation Lambda returned an empty response.");

        SeasonActivationResponse result;
        try
        {
            result = JsonSerializer.Deserialize<SeasonActivationResponse>(
                responsePayload,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Activation Lambda returned a null response.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Activation Lambda returned malformed JSON: {exception.Message}",
                exception);
        }

        string expectedStatus = string.Equals(
            request.Operation,
            "rollback",
            StringComparison.OrdinalIgnoreCase)
            ? "rolled_back"
            : "activated";
        if (!string.Equals(result.Status, expectedStatus, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(result.Operation, request.Operation, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(result.SeasonId, request.SeasonId, StringComparison.Ordinal) ||
            !string.Equals(result.RevisionId, request.RevisionId, StringComparison.Ordinal) ||
            !string.Equals(result.RevisionHash, request.RevisionHash, StringComparison.Ordinal) ||
            result.ActivatedAt == default)
        {
            throw new InvalidOperationException(
                "Activation Lambda returned a response that does not match the requested operation or revision.");
        }

        return result;
    }

    private static async Task<string> _readPayloadAsync(
        System.IO.Stream? payload,
        CancellationToken cancellationToken)
    {
        if (payload == null)
            return string.Empty;

        using StreamReader reader = new(payload);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
