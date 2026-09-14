using System.Text.Json.Serialization;

namespace VaultShared.Models.Blizzard;

public class TokenResponse
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
    [JsonPropertyName("token_type")] public string TokenType { get; set; } = string.Empty;
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("sub")] public string Sub { get; set; } = string.Empty;
}
