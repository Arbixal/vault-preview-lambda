using System.Net.Http.Json;
using System.Text;
using VaultPreviewLambda.Models.Blizzard;
using VaultShared;
using VaultShared.Models.Blizzard;

namespace VaultPreviewLambda;

public interface IBlizzardApiHandler
{
    Task Connect();
    Task<BlizzardEncounterResponse> GetEncounters(string region, string realm, string character);
}

public class BlizzardApiHandler(
    IHttpClientFactory clientFactory,
    ISecretHandler secretHandler)
    : IBlizzardApiHandler
{
    private string? _token;
    private HttpClient? _client;

    private readonly IDictionary<string, string> _baseUrlByRegion = new Dictionary<string, string>()
    {
        {"us", "https://us.api.blizzard.com"},
        {"eu", "https://eu.api.blizzard.com"},
        {"kr", "https://kr.api.blizzard.com"},
        {"tw", "https://tw.api.blizzard.com"},
        {"cn", "https://gateway.battlenet.com.cn"},
    };

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

    public async Task<BlizzardEncounterResponse> GetEncounters(string region, string realm, string character)
    {
        if (!_baseUrlByRegion.ContainsKey(region.ToLower()))
            throw new ArgumentOutOfRangeException(nameof(region), "Region must be one of us, eu, kr, tw, or cn");
        
        HttpClient client = _getOrCreateClient(region.ToLower());

        BlizzardEncounterResponse? response = await client.GetFromJsonAsync<BlizzardEncounterResponse>(
            $"/profile/wow/character/{realm}/{character}/encounters/raids?namespace=profile-us&locale=en_US");

        return response ?? new BlizzardEncounterResponse();
    }

    private HttpClient _getOrCreateClient(string region)
    {
        if (string.IsNullOrEmpty(_token))
            throw new MissingFieldException(nameof(BlizzardApiHandler), nameof(_token));

        if (_client != null)
            return _client;

        _client = clientFactory.CreateClient();
        
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_token}");
        _client.BaseAddress = new Uri(_baseUrlByRegion[region]);

        return _client;
    }

    private static string _Base64Encode(string plainText)
    {
        byte[] plainTextBytes = Encoding.UTF8.GetBytes(plainText);
        return Convert.ToBase64String(plainTextBytes);
    }
}