using System.Net.Http.Json;
using VaultPreviewLambda.Models.RaiderIo;

namespace VaultPreviewLambda;

public interface IRaiderIoHandler
{
    Task<IList<RaiderIoDungeonRun>> GetWeeklyHighestLevelRuns(string realm, string character);
}

public class RaiderIoHandler(IHttpClientFactory clientFactory) : IRaiderIoHandler
{
    public async Task<IList<RaiderIoDungeonRun>> GetWeeklyHighestLevelRuns(string realm, string character)
    {
        HttpClient client = clientFactory.CreateClient();

        string requestUrl =
            $"https://raider.io/api/v1/characters/profile?region=us&realm={realm}&name={character}&fields=mythic_plus_weekly_highest_level_runs";
        RaiderIoProfileResponse? response = await client.GetFromJsonAsync<RaiderIoProfileResponse>(requestUrl);

        return response?.WeeklyHighestLevelRuns ?? new List<RaiderIoDungeonRun>();
    }
}