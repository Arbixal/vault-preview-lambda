using System.Net.Http.Json;
using System.Text;
using VaultPreview.Blizzard.Models;
using VaultShared;
using VaultShared.Models.Blizzard;

namespace VaultPreview.Blizzard;

public interface IBlizzardApiHandler
{
    Task Connect();
    Task<BlizzardEncounterResponse> GetEncounters(string region, string realm, string character);

    Task<int?> GetSeason(string region);

    Task<Dictionary<int, int>> GetDelveStatistics(string region, string realm, string character);
}

public class BlizzardApiHandler(
    IHttpClientFactory clientFactory,
    ISecretHandler secretHandler)
    : IBlizzardApiHandler
{
    private const int _DELVE_CATEGORY = 15533;
    private const long _TIER1_DELVE = 40766;
    private const long _TIER2_DELVE = 40767;
    private const long _TIER3_DELVE = 40768;
    private const long _TIER4_DELVE = 40769;
    private const long _TIER5_DELVE = 40770;
    private const long _TIER6_DELVE = 40771;
    private const long _TIER7_DELVE = 40772;
    private const long _TIER8_DELVE = 40773;
    private const long _TIER9_DELVE = 40774;
    private const long _TIER10_DELVE = 40775;
    private const long _TIER11_DELVE = 40776;
    
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

    public async Task<int?> GetSeason(string region)
    {
        if (!_baseUrlByRegion.ContainsKey(region.ToLower()))
            throw new ArgumentOutOfRangeException(nameof(region), "Region must be one of us, eu, kr, tw, or cn");
        
        HttpClient client = _getOrCreateClient(region.ToLower());

        BlizzardSeasonResponse? response = await client.GetFromJsonAsync<BlizzardSeasonResponse>(
            $"/data/wow/mythic-keystone/season/index?namespace=dynamic-us&locale=en_US");

        return response?.CurrentSeason.Id;
    }

    public async Task<Dictionary<int, int>> GetDelveStatistics(string region, string realm, string character)
    {
        BlizzardCharacterStatisticsResponse response = await _getCharacterStatistics(region, realm, character);
        
        Dictionary<int, BlizzardCharacterStatisticCategory> categories =
            response.Categories.ToDictionary(x => x.Id, x => x);

        IList<BlizzardCharacterStatistic> statisticList = categories.GetValueOrDefault(_DELVE_CATEGORY)?.Statistics ??
                                                          new List<BlizzardCharacterStatistic>();

        Dictionary<long, double> statistics = statisticList.ToDictionary(x => x.Id, x => x.Quantity);

        Dictionary<int, int> delveData = new Dictionary<int, int>
        {
            [1] = (int)statistics.GetValueOrDefault(_TIER1_DELVE, 0.0),
            [2] = (int)statistics.GetValueOrDefault(_TIER2_DELVE, 0.0),
            [3] = (int)statistics.GetValueOrDefault(_TIER3_DELVE, 0.0),
            [4] = (int)statistics.GetValueOrDefault(_TIER4_DELVE, 0.0),
            [5] = (int)statistics.GetValueOrDefault(_TIER5_DELVE, 0.0),
            [6] = (int)statistics.GetValueOrDefault(_TIER6_DELVE, 0.0),
            [7] = (int)statistics.GetValueOrDefault(_TIER7_DELVE, 0.0),
            [8] = (int)statistics.GetValueOrDefault(_TIER8_DELVE, 0.0),
            [9] = (int)statistics.GetValueOrDefault(_TIER9_DELVE, 0.0),
            [10] = (int)statistics.GetValueOrDefault(_TIER10_DELVE, 0.0),
            [11] = (int)statistics.GetValueOrDefault(_TIER11_DELVE, 0.0)
        };

        return delveData;
    }
    
    private async Task<BlizzardCharacterStatisticsResponse> _getCharacterStatistics(string region, string realm,
        string character)
    {
        if (!_baseUrlByRegion.ContainsKey(region.ToLower()))
            throw new ArgumentOutOfRangeException(nameof(region), "Region must be one of us, eu, kr, tw, or cn");
        
        HttpClient client = _getOrCreateClient(region.ToLower());

        BlizzardCharacterStatisticsResponse? response = await client.GetFromJsonAsync<BlizzardCharacterStatisticsResponse>(
            $"/profile/wow/character/{realm}/{character}/achievements/statistics?namespace=profile-us&locale=en_US");

        return response ?? new BlizzardCharacterStatisticsResponse();
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