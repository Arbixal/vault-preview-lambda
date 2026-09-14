using System.Net.Http.Json;
using System.Net.Http.Headers;
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

    private sealed record RegionSettings(
        string BaseUrl,
        string ProfileNamespace,
        string DynamicNamespace,
        string Locale);

    private static readonly IReadOnlyDictionary<string, RegionSettings> _regionSettings =
        new Dictionary<string, RegionSettings>(StringComparer.OrdinalIgnoreCase)
    {
        ["us"] = new("https://us.api.blizzard.com", "profile-us", "dynamic-us", "en_US"),
        ["eu"] = new("https://eu.api.blizzard.com", "profile-eu", "dynamic-eu", "en_GB"),
        ["kr"] = new("https://kr.api.blizzard.com", "profile-kr", "dynamic-kr", "ko_KR"),
        ["tw"] = new("https://tw.api.blizzard.com", "profile-tw", "dynamic-tw", "zh_TW"),
        ["cn"] = new("https://gateway.battlenet.com.cn", "profile-cn", "dynamic-cn", "zh_CN"),
    };

    private static readonly IReadOnlyDictionary<long, int> _delveTierByStatistic =
        new Dictionary<long, int>
        {
            [_TIER1_DELVE] = 1,
            [_TIER2_DELVE] = 2,
            [_TIER3_DELVE] = 3,
            [_TIER4_DELVE] = 4,
            [_TIER5_DELVE] = 5,
            [_TIER6_DELVE] = 6,
            [_TIER7_DELVE] = 7,
            [_TIER8_DELVE] = 8,
            [_TIER9_DELVE] = 9,
            [_TIER10_DELVE] = 10,
            [_TIER11_DELVE] = 11,
        };

    public async Task Connect()
    {
        string? token = await secretHandler.GetSecret("/Blizzard/Token");
        long tokenExpires = await secretHandler.GetSecretAsLong("/Blizzard/TokenExpires");
        if (token != null &&
            DateTimeOffset.UtcNow < DateTimeOffset.FromUnixTimeMilliseconds(tokenExpires).Subtract(TimeSpan.FromMinutes(1)))
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
            DateTimeOffset expires = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
            await secretHandler.PutSecret("/Blizzard/Token", _token);
            await secretHandler.PutSecret("/Blizzard/TokenExpires", expires.ToUnixTimeMilliseconds());
        }
    }

    public async Task<BlizzardEncounterResponse> GetEncounters(string region, string realm, string character)
    {
        RegionSettings settings = _getRegionSettings(region);
        HttpClient client = _getClient(settings);

        BlizzardEncounterResponse? response = await client.GetFromJsonAsync<BlizzardEncounterResponse>(
            $"/profile/wow/character/{_slug(realm)}/{_slug(character)}/encounters/raids?namespace={settings.ProfileNamespace}&locale={settings.Locale}");

        return response ?? new BlizzardEncounterResponse();
    }

    public async Task<int?> GetSeason(string region)
    {
        RegionSettings settings = _getRegionSettings(region);
        HttpClient client = _getClient(settings);

        BlizzardSeasonResponse? response = await client.GetFromJsonAsync<BlizzardSeasonResponse>(
            $"/data/wow/mythic-keystone/season/index?namespace={settings.DynamicNamespace}&locale={settings.Locale}");

        return response?.CurrentSeason?.Id;
    }

    public async Task<Dictionary<int, int>> GetDelveStatistics(string region, string realm, string character)
    {
        BlizzardCharacterStatisticsResponse response = await _getCharacterStatistics(region, realm, character);

        IList<BlizzardCharacterStatisticCategory> categories = response.Categories ?? [];
        BlizzardCharacterStatisticCategory? delveCategory = categories
            .FirstOrDefault(x => x.Name.Contains("delve", StringComparison.OrdinalIgnoreCase));

        IEnumerable<BlizzardCharacterStatistic> statisticList = _flattenStatistics(
            delveCategory != null ? [delveCategory] : categories);

        Dictionary<int, int> delveData = Enumerable.Range(1, 11)
            .ToDictionary(x => x, _ => 0);

        foreach (BlizzardCharacterStatistic statistic in statisticList)
        {
            int? tier = _delveTierByStatistic.GetValueOrDefault(statistic.Id);
            tier ??= _getDelveTierFromName(statistic.Name);

            if (tier is >= 1 and <= 11)
                delveData[tier.Value] = (int)statistic.Quantity;
        }

        return delveData;
    }

    private static IEnumerable<BlizzardCharacterStatistic> _flattenStatistics(
        IEnumerable<BlizzardCharacterStatisticCategory> categories)
    {
        foreach (BlizzardCharacterStatisticCategory category in categories)
        {
            foreach (BlizzardCharacterStatistic statistic in category.Statistics ?? [])
                yield return statistic;

            if (category.SubCategories == null)
                continue;

            foreach (BlizzardCharacterStatistic statistic in _flattenStatistics(category.SubCategories))
                yield return statistic;
        }
    }

    private static int? _getDelveTierFromName(string name)
    {
        const string TIER_PREFIX = "tier ";
        int tierStart = name.IndexOf(TIER_PREFIX, StringComparison.OrdinalIgnoreCase);
        if (tierStart < 0)
            return null;

        tierStart += TIER_PREFIX.Length;
        int tierEnd = tierStart;
        while (tierEnd < name.Length && char.IsDigit(name[tierEnd]))
            ++tierEnd;

        return int.TryParse(name[tierStart..tierEnd], out int tier) ? tier : null;
    }
    
    private async Task<BlizzardCharacterStatisticsResponse> _getCharacterStatistics(string region, string realm,
        string character)
    {
        RegionSettings settings = _getRegionSettings(region);
        HttpClient client = _getClient(settings);

        BlizzardCharacterStatisticsResponse? response = await client.GetFromJsonAsync<BlizzardCharacterStatisticsResponse>(
            $"/profile/wow/character/{_slug(realm)}/{_slug(character)}/achievements/statistics?namespace={settings.ProfileNamespace}&locale={settings.Locale}");

        return response ?? new BlizzardCharacterStatisticsResponse();
    }

    private RegionSettings _getRegionSettings(string region)
    {
        string normalizedRegion = region.Trim().ToLowerInvariant();
        if (_regionSettings.TryGetValue(normalizedRegion, out RegionSettings? settings))
            return settings;

        throw new ArgumentOutOfRangeException(nameof(region), "Region must be one of us, eu, kr, tw, or cn");
    }

    private HttpClient _getClient(RegionSettings settings)
    {
        if (string.IsNullOrEmpty(_token))
            throw new MissingFieldException(nameof(BlizzardApiHandler), nameof(_token));

        HttpClient client = clientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        client.BaseAddress = new Uri(settings.BaseUrl);

        return client;
    }

    private static string _slug(string value)
    {
        return Uri.EscapeDataString(value.Trim().ToLowerInvariant());
    }

    private static string _Base64Encode(string plainText)
    {
        byte[] plainTextBytes = Encoding.UTF8.GetBytes(plainText);
        return Convert.ToBase64String(plainTextBytes);
    }
}
