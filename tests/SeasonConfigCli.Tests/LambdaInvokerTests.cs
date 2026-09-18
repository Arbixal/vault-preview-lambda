using System.Text;
using Amazon;
using Amazon.Lambda;
using Amazon.Lambda.Model;
using Amazon.Runtime;
using SeasonConfigCli;
using SeasonConfigCli.Request;
using Xunit;

namespace SeasonConfigCli.Tests;

public sealed class LambdaInvokerTests
{
    [Fact]
    public async Task InvokeAsync_FunctionErrorThrows()
    {
        FakeLambdaClient client = new()
        {
            FunctionError = "Unhandled",
            Payload = "{\"errorMessage\":\"activation failed\"}"
        };
        LambdaInvoker invoker = new(client);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => invoker.InvokeAsync("activation", _createRequest()));

        Assert.Contains("activation failed", exception.Message);
    }

    [Fact]
    public async Task InvokeAsync_MalformedSuccessfulPayloadThrows()
    {
        FakeLambdaClient client = new() { Payload = "{}" };
        LambdaInvoker invoker = new(client);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => invoker.InvokeAsync("activation", _createRequest()));

        Assert.Contains("does not match", exception.Message);
    }

    private static SeasonActivationRequest _createRequest() => new()
    {
        Operation = "activate",
        SeasonId = "future-season",
        RevisionId = "future-season-r1",
        RevisionHash = "sha256:0000000000000000000000000000000000000000000000000000000000000000"
    };

    private sealed class FakeLambdaClient : AmazonLambdaClient
    {
        public FakeLambdaClient()
            : base(new AnonymousAWSCredentials(), RegionEndpoint.USEast1)
        {
        }

        public string? FunctionError { get; init; }
        public string Payload { get; init; } = "{}";

        public override Task<InvokeResponse> InvokeAsync(
            InvokeRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new InvokeResponse
            {
                FunctionError = FunctionError,
                Payload = new MemoryStream(Encoding.UTF8.GetBytes(Payload))
            });
        }
    }
}
