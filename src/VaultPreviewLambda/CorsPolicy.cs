namespace VaultPreviewLambda;

public static class CorsPolicy
{
    private const string _DEFAULT_ORIGINS = "https://vault-preview.bixnpieces.com,http://localhost:3000";

    public static string? GetAllowedOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
            return null;

        string normalizedOrigin = origin.Trim();
        string configuredOrigins = Environment.GetEnvironmentVariable("VAULT_PREVIEW_CORS_ORIGINS")
            ?? _DEFAULT_ORIGINS;
        bool isAllowed = configuredOrigins
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(candidate => string.Equals(candidate, normalizedOrigin, StringComparison.OrdinalIgnoreCase));

        return isAllowed ? normalizedOrigin : null;
    }
}
