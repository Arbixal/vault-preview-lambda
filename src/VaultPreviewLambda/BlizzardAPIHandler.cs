using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using VaultPreviewLambda.Models.Blizzard;

namespace VaultPreviewLambda;

public interface IBlizzardApiHandler
{
    Task Connect();
    Task<BlizzardEncounterResponse> GetEncounters(string realm, string character);
}

public class TokenResponse
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; }
    [JsonPropertyName("token_type")] public string TokenType { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("sub")] public string Sub { get; set; }
}

public class BlizzardApiHandler(
    IHttpClientFactory clientFactory,
    ISecretHandler secretHandler)
    : IBlizzardApiHandler
{
    private string? _token;
    private HttpClient? _client;

    public async Task Connect()
    {
        string? token = await secretHandler.GetSecret("/Blizzard/Token");
        long tokenExpires = await secretHandler.GetSecretAsLong("/Blizzard/TokenExpires");
        if (token != null && DateTime.UtcNow < DateTime.UnixEpoch.AddMilliseconds(tokenExpires))
        {
            _token = token;
            return;
        }
        
        Console.WriteLine("Empty or expired token");
        
        HttpClient client = clientFactory.CreateClient();

        string? clientId = await secretHandler.GetSecret("/Blizzard/ClientId");
        string? secret = await secretHandler.GetSecret("/Blizzard/ClientSecret");

        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post,
            "https://oauth.battle.net/token?grant_type=client_credentials");

        request.Headers.Add("Authorization", $"Basic {_Base64Encode($"{clientId}:{secret}")}");

        HttpResponseMessage response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();

        TokenResponse? tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>();

        if (tokenResponse != null)
        {
            _token = tokenResponse.AccessToken;
            DateTimeOffset expires = DateTimeOffset.Now.AddSeconds(tokenResponse.ExpiresIn);
            await secretHandler.PutSecret("/Blizzard/Token", _token);
            await secretHandler.PutSecret("/Blizzard/TokenExpires", expires.ToUnixTimeMilliseconds());
        }
    }

    public async Task<BlizzardEncounterResponse> GetEncounters(string realm, string character)
    {
        HttpClient client = _getOrCreateClient();

        BlizzardEncounterResponse? response = await client.GetFromJsonAsync<BlizzardEncounterResponse>(
            $"/profile/wow/character/{realm}/{character}/encounters/raids?namespace=profile-us&locale=en_US");

        return response ?? new BlizzardEncounterResponse();
    }

    private HttpClient _getOrCreateClient()
    {
        if (string.IsNullOrEmpty(_token))
            throw new MissingFieldException(nameof(BlizzardApiHandler), nameof(_token));

        if (_client != null)
            return _client;

        _client = clientFactory.CreateClient();
        
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_token}");
        _client.BaseAddress = new Uri("https://us.api.blizzard.com");

        return _client;
    }

    private static string _Base64Encode(string plainText)
    {
        byte[] plainTextBytes = Encoding.UTF8.GetBytes(plainText);
        return Convert.ToBase64String(plainTextBytes);
    }
}