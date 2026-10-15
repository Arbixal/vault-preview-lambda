using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SeasonConfigCli;
using Xunit;

namespace SeasonConfigCli.Tests;

public class ProductionSmokeCheckTests
{
    private const string _HASH = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task CheckAsync_ValidatesConfigurationAndReadOnlyProgress()
    {
        FakeSmokeHandler handler = new();
        using HttpClient client = new(handler);
        ProductionSmokeCheck checker = new(client);

        ProductionSmokeCheckResult result = await checker.CheckAsync(_options());

        Assert.Equal("midnight-s2", result.SeasonId);
        Assert.Equal("midnight-s2-r3", result.RevisionId);
        Assert.Equal(_HASH, result.RevisionHash);
        Assert.Equal(
            ["/v1/app-config", "/v1/app-config", "/v1/vault-progress/us/realm/character?readOnly=true"],
            handler.Requests.Select(request => request.Path));
        Assert.All(handler.Requests, request => Assert.Equal("https://origin.example", request.Origin));
        Assert.Equal($"\"{_HASH}\"", handler.Requests[1].IfNoneMatch);
    }

    [Fact]
    public async Task CheckAsync_MismatchedProgressMetadataFails()
    {
        FakeSmokeHandler handler = new() { ProgressRevisionId = "midnight-s2-r2" };
        using HttpClient client = new(handler);
        ProductionSmokeCheck checker = new(client);

        ProductionSmokeCheckException exception = await Assert.ThrowsAsync<ProductionSmokeCheckException>(
            () => checker.CheckAsync(_options()));

        Assert.Equal(
            "Character progress season metadata does not match /v1/app-config.",
            exception.Message);
    }

    [Fact]
    public async Task CheckAsync_NonSuccessResponseIncludesApiErrorCode()
    {
        FakeSmokeHandler handler = new() { ProgressStatusCode = HttpStatusCode.Forbidden };
        using HttpClient client = new(handler);
        ProductionSmokeCheck checker = new(client);

        ProductionSmokeCheckException exception = await Assert.ThrowsAsync<ProductionSmokeCheckException>(
            () => checker.CheckAsync(_options()));

        Assert.Equal(
            "/v1/vault-progress/us/realm/character?readOnly=true returned HTTP 403 (forbidden).",
            exception.Message);
    }

    private static ProductionSmokeCheckOptions _options() => new(
        "https://api.example",
        "us",
        "realm",
        "character",
        "https://origin.example",
        "midnight-s2",
        "midnight-s2-r3",
        _HASH);

    private sealed class FakeSmokeHandler : HttpMessageHandler
    {
        public List<SmokeRequest> Requests { get; } = [];
        public string ProgressRevisionId { get; init; } = "midnight-s2-r3";
        public HttpStatusCode ProgressStatusCode { get; init; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.PathAndQuery;
            Requests.Add(new(
                path,
                request.Headers.GetValues("Origin").Single(),
                request.Headers.TryGetValues("If-None-Match", out IEnumerable<string>? values)
                    ? values.Single()
                    : null));

            if (path == "/v1/app-config" && request.Headers.Contains("If-None-Match"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            }

            if (path == "/v1/app-config")
            {
                HttpResponseMessage response = _json(
                    HttpStatusCode.OK,
                    new
                    {
                        schemaVersion = 1,
                        activeSeason = new
                        {
                            id = "midnight-s2",
                            revision = "midnight-s2-r3",
                            revisionHash = _HASH
                        }
                    });
                response.Headers.ETag = new EntityTagHeaderValue($"\"{_HASH}\"");
                return Task.FromResult(response);
            }

            return Task.FromResult(_json(
                ProgressStatusCode,
                ProgressStatusCode == HttpStatusCode.OK
                    ? new
                    {
                        schemaVersion = 1,
                        season = new
                        {
                            id = "midnight-s2",
                            revision = ProgressRevisionId,
                            revisionHash = _HASH
                        }
                    }
                    : new
                    {
                        error = new { code = "forbidden" }
                    }));
        }

        private static HttpResponseMessage _json(HttpStatusCode statusCode, object body) =>
            new(statusCode)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(body),
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
    }

    private sealed record SmokeRequest(string Path, string Origin, string? IfNoneMatch);
}
