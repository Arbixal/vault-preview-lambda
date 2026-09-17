using System;
using System.Net;
using System.Text;
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

        if (response.FunctionError != null)
        {
            string errorPayload = response.Payload != null
                ? await new StreamReader(response.Payload).ReadToEndAsync()
                : string.Empty;
            throw new InvalidOperationException(
                $"Lambda function error ({response.FunctionError}): {errorPayload}");
        }

        string responsePayload = response.Payload != null
            ? await new StreamReader(response.Payload).ReadToEndAsync()
            : "{}";
        SeasonActivationResponse? result = JsonSerializer.Deserialize<SeasonActivationResponse>(
            responsePayload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return result ?? new SeasonActivationResponse();
    }
}
