using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SeasonConfigCli;

public sealed record ProductionSmokeCheckOptions(
    string ApiBaseUrl,
    string Region,
    string Realm,
    string Character,
    string Origin,
    string? ExpectedSeasonId,
    string? ExpectedRevisionId,
    string? ExpectedRevisionHash)
{
    public static ProductionSmokeCheckOptions FromEnvironment()
    {
        string[] requiredNames =
        [
            "VAULT_PREVIEW_API_BASE_URL",
            "VAULT_PREVIEW_REGION",
            "VAULT_PREVIEW_REALM",
            "VAULT_PREVIEW_CHARACTER"
        ];
        List<string> missingNames = [];
        foreach (string name in requiredNames)
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
                missingNames.Add(name);
        }

        if (missingNames.Count > 0)
            throw new ArgumentException(
                $"Missing environment variables: {string.Join(", ", missingNames)}.");

        string apiBaseUrl = Environment.GetEnvironmentVariable("VAULT_PREVIEW_API_BASE_URL")!.TrimEnd('/');
        if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "VAULT_PREVIEW_API_BASE_URL must be an absolute HTTP or HTTPS URL.");
        }

        return new ProductionSmokeCheckOptions(
            apiBaseUrl,
            Environment.GetEnvironmentVariable("VAULT_PREVIEW_REGION")!,
            Environment.GetEnvironmentVariable("VAULT_PREVIEW_REALM")!,
            Environment.GetEnvironmentVariable("VAULT_PREVIEW_CHARACTER")!,
            _optionalEnvironmentValue("VAULT_PREVIEW_SMOKE_ORIGIN")
                ?? "https://vault-preview.bixnpieces.com",
            _optionalEnvironmentValue("VAULT_PREVIEW_EXPECTED_SEASON_ID"),
            _optionalEnvironmentValue("VAULT_PREVIEW_EXPECTED_REVISION_ID"),
            _optionalEnvironmentValue("VAULT_PREVIEW_EXPECTED_REVISION_HASH"));
    }

    private static string? _optionalEnvironmentValue(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(value) ? null : value;
    }
}

public sealed record ProductionSmokeCheckResult(
    string SeasonId,
    string RevisionId,
    string RevisionHash,
    string Region,
    string Realm,
    string Character);

public sealed class ProductionSmokeCheckException : Exception
{
    public ProductionSmokeCheckException(string message)
        : base(message)
    {
    }

    public ProductionSmokeCheckException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class ProductionSmokeCheck(HttpClient httpClient)
{
    private static readonly Regex _revisionHashPattern = new(
        "^sha256:[0-9a-f]{64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<ProductionSmokeCheckResult> CheckAsync(
        ProductionSmokeCheckOptions options,
        CancellationToken cancellationToken = default)
    {
        SmokeResponse appConfigResponse = await _getJsonAsync(
            options,
            "/v1/app-config",
            null,
            cancellationToken);
        if (appConfigResponse.StatusCode != HttpStatusCode.OK)
        {
            throw new ProductionSmokeCheckException(
                $"/v1/app-config returned HTTP {(int)appConfigResponse.StatusCode}; expected 200.");
        }

        JsonElement appConfig = _requireObject(appConfigResponse.Body, "/v1/app-config");
        _requireSchemaVersion(appConfig, "/v1/app-config");
        JsonElement activeSeason = _requireObject(
            _getProperty(appConfig, "activeSeason"),
            "activeSeason");
        string seasonId = _requireString(_getProperty(activeSeason, "id"), "activeSeason.id");
        string revisionId = _requireString(
            _getProperty(activeSeason, "revision"),
            "activeSeason.revision");
        string revisionHash = _requireString(
            _getProperty(activeSeason, "revisionHash"),
            "activeSeason.revisionHash");
        if (!_revisionHashPattern.IsMatch(revisionHash))
        {
            throw new ProductionSmokeCheckException(
                $"activeSeason.revisionHash '{revisionHash}' is not canonical.");
        }

        _assertExpected(seasonId, options.ExpectedSeasonId, "Active season ID");
        _assertExpected(revisionId, options.ExpectedRevisionId, "Active revision ID");
        _assertExpected(revisionHash, options.ExpectedRevisionHash, "Active revision hash");

        string expectedEtag = $"\"{revisionHash}\"";
        if (!string.Equals(appConfigResponse.ETag, expectedEtag, StringComparison.Ordinal))
        {
            throw new ProductionSmokeCheckException(
                $"/v1/app-config returned unexpected ETag '{appConfigResponse.ETag ?? ""}'.");
        }

        SmokeResponse revalidatedConfig = await _getJsonAsync(
            options,
            "/v1/app-config",
            appConfigResponse.ETag,
            cancellationToken);
        if (revalidatedConfig.StatusCode != HttpStatusCode.NotModified)
        {
            throw new ProductionSmokeCheckException(
                $"/v1/app-config conditional request returned HTTP {(int)revalidatedConfig.StatusCode}; expected 304.");
        }

        string progressPath =
            $"/v1/vault-progress/{Uri.EscapeDataString(options.Region)}/" +
            $"{Uri.EscapeDataString(options.Realm)}/{Uri.EscapeDataString(options.Character)}?readOnly=true";
        SmokeResponse progressResponse = await _getJsonAsync(
            options,
            progressPath,
            null,
            cancellationToken);
        if (progressResponse.StatusCode != HttpStatusCode.OK)
        {
            throw new ProductionSmokeCheckException(
                $"{progressPath} returned HTTP {(int)progressResponse.StatusCode}; expected 200.");
        }

        JsonElement progress = _requireObject(progressResponse.Body, progressPath);
        _requireSchemaVersion(progress, progressPath);
        JsonElement progressSeason = _requireObject(
            _getProperty(progress, "season"),
            "progress.season");
        string progressSeasonId = _requireString(
            _getProperty(progressSeason, "id"),
            "progress.season.id");
        string progressRevisionId = _requireString(
            _getProperty(progressSeason, "revision"),
            "progress.season.revision");
        string progressRevisionHash = _requireString(
            _getProperty(progressSeason, "revisionHash"),
            "progress.season.revisionHash");

        if (!string.Equals(progressSeasonId, seasonId, StringComparison.Ordinal) ||
            !string.Equals(progressRevisionId, revisionId, StringComparison.Ordinal) ||
            !string.Equals(progressRevisionHash, revisionHash, StringComparison.Ordinal))
        {
            throw new ProductionSmokeCheckException(
                "Character progress season metadata does not match /v1/app-config.");
        }

        return new ProductionSmokeCheckResult(
            seasonId,
            revisionId,
            revisionHash,
            options.Region,
            options.Realm,
            options.Character);
    }

    private async Task<SmokeResponse> _getJsonAsync(
        ProductionSmokeCheckOptions options,
        string path,
        string? ifNoneMatch,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Get,
            new Uri($"{options.ApiBaseUrl}{path}", UriKind.Absolute));
        request.Headers.TryAddWithoutValidation("Origin", options.Origin);
        if (ifNoneMatch != null)
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);

        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        string text = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonElement? body = null;
        if (text.Length > 0)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(text);
                body = document.RootElement.Clone();
            }
            catch (JsonException exception)
            {
                throw new ProductionSmokeCheckException(
                    $"{path} returned non-JSON content with HTTP {(int)response.StatusCode}.",
                    exception);
            }
        }

        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotModified)
        {
            string? errorCode = _getString(body, "error", "code");
            string suffix = errorCode == null ? string.Empty : $" ({errorCode})";
            throw new ProductionSmokeCheckException(
                $"{path} returned HTTP {(int)response.StatusCode}{suffix}.");
        }

        return new SmokeResponse(
            response.StatusCode,
            response.Headers.ETag?.ToString(),
            body);
    }

    private static JsonElement _requireObject(JsonElement? value, string description)
    {
        if (!value.HasValue || value.Value.ValueKind != JsonValueKind.Object)
            throw new ProductionSmokeCheckException($"{description} is missing or not an object.");

        return value.Value;
    }

    private static string _requireString(JsonElement? value, string description)
    {
        if (!value.HasValue || value.Value.ValueKind != JsonValueKind.String)
            throw new ProductionSmokeCheckException($"{description} is missing or empty.");

        string? result = value.Value.GetString();
        if (string.IsNullOrEmpty(result))
            throw new ProductionSmokeCheckException($"{description} is missing or empty.");

        return result;
    }

    private static void _requireSchemaVersion(JsonElement value, string description)
    {
        if (_getProperty(value, "schemaVersion") is not { } schemaVersion ||
            schemaVersion.ValueKind != JsonValueKind.Number ||
            !schemaVersion.TryGetInt32(out int version) ||
            version != 1)
        {
            throw new ProductionSmokeCheckException(
                $"{description} did not return schemaVersion 1.");
        }
    }

    private static JsonElement? _getProperty(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement property)
            ? property
            : null;

    private static string? _getString(JsonElement? value, params string[] properties)
    {
        foreach (string property in properties)
        {
            if (!value.HasValue || value.Value.ValueKind != JsonValueKind.Object ||
                !value.Value.TryGetProperty(property, out JsonElement child))
            {
                return null;
            }

            value = child;
        }

        return value is { ValueKind: JsonValueKind.String }
            ? value.Value.GetString()
            : null;
    }

    private static void _assertExpected(string actual, string? expected, string description)
    {
        if (!string.IsNullOrEmpty(expected) &&
            !string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new ProductionSmokeCheckException(
                $"{description} '{actual}' does not match expected value '{expected}'.");
        }
    }

    private sealed record SmokeResponse(
        HttpStatusCode StatusCode,
        string? ETag,
        JsonElement? Body);
}
