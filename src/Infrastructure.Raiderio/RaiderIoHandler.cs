using System.Net.Http.Json;
using VaultPreview.RaiderIo.Models;

namespace VaultPreview.RaiderIo;

public interface IRaiderIoHandler
{
    Task<RaiderIoProfileResponse> GetWeeklyHighestLevelRuns(string region, string realm, string character);
}

public class RaiderIoHandler(IHttpClientFactory clientFactory) : IRaiderIoHandler
{
    public async Task<RaiderIoProfileResponse> GetWeeklyHighestLevelRuns(string region, string realm, string character)
    {
        HttpClient client = clientFactory.CreateClient();

        string requestUrl =
            $"https://raider.io/api/v1/characters/profile?region={region}&realm={realm}&name={character}&fields=mythic_plus_weekly_highest_level_runs";
        RaiderIoProfileResponse? response = await client.GetFromJsonAsync<RaiderIoProfileResponse>(requestUrl);

        return response ?? new RaiderIoProfileResponse();
    }
}