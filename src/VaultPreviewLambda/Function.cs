using Amazon.Lambda.Annotations;
using Amazon.Lambda.Annotations.APIGateway;
using Amazon.Lambda.Core;
using VaultPreviewLambda.Models;
using VaultPreviewLambda.Models.Blizzard;
using VaultPreviewLambda.Models.RaiderIo;
using VaultShared;
using VaultShared.Models.Blizzard;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace VaultPreviewLambda;

public class Function
{
    private readonly IBlizzardApiHandler _blizzardApiHandler;
    private readonly IRaiderIoHandler _raiderIoHandler;

    public Function(
        IBlizzardApiHandler blizzardApiHandler,
        IRaiderIoHandler raiderIoHandler
        )
    {
        _blizzardApiHandler = blizzardApiHandler;
        _raiderIoHandler = raiderIoHandler;
    }
    
    /// <summary>
    /// A simple function that takes a string and does a ToUpper
    /// </summary>
    /// <param name="region">The region the character belongs to ("us", "eu", "kr", etc)</param>
    /// <param name="realm">The name of the realm that the character belongs to</param>
    /// <param name="character">The name of the character</param>
    /// <returns></returns>
    [LambdaFunction]
    [HttpApi(LambdaHttpMethod.Get,"/vault-progress/{region}/{realm}/{character}")]
    public async Task<IDictionary<string, CharacterProgress>> FunctionHandler(string region, string realm, string character)
    {
        Dictionary<string, CharacterProgress> progress = new Dictionary<string, CharacterProgress>();

        if (string.IsNullOrEmpty(region) || string.IsNullOrEmpty(realm) || string.IsNullOrEmpty(character))
        {
            return progress;
        }
        
        await _blizzardApiHandler.Connect();
        
        CharacterProgress characterProgress = new CharacterProgress();
        
        Console.WriteLine($"{character} from {realm}");

        characterProgress.Raid = await _getBlizzardRaidData(region, realm, character);

        (characterProgress.PlayerClass, characterProgress.Dungeons) = await _getRaiderIoMythicPlusData(region, realm, character);
        
        progress.Add($"{character}-{realm}", characterProgress);

        return progress;
    }

    private async Task<IDictionary<string, BossProgress>> _getBlizzardRaidData(string region, string realm, string character)
    {
        const string EXPANSION = "Current Season";
        const int INSTANCE = 1207; // Amirdrassil
        string[] BOSSES = { "gnarlroot", "igira-the-cruel", "volcoross", "council-of-dreams", "larodar", "nymue", "smolderon", "tindral-sageswift", "fyrakk-the-blazing" };
        DateTimeOffset compareDate = _getLastTuesday();

        IDictionary<string, BossProgress> result = new Dictionary<string, BossProgress>();

        // Seed data
        foreach (string aBoss in BOSSES)
        {
            result.Add(aBoss, new BossProgress());
        }
        
        BlizzardEncounterResponse response =
            await _blizzardApiHandler.GetEncounters(region, realm, character);
            
        Console.WriteLine($"Expansions: {response.Expansions.Count}");
        BlizzardExpansion? currentExpansion = response.Expansions.FirstOrDefault(x => x.Expansion.Name == EXPANSION);

        if (currentExpansion == null)
        {
            // Return blank boss progress
            return result;
        }
            
        Console.WriteLine($"Instances: {currentExpansion.Instances.Count}");
        BlizzardInstance? currentInstance = currentExpansion.Instances.FirstOrDefault(x => x.Instance.Id == INSTANCE);

        if (currentInstance == null)
        {
            // Return blank boss progress
            return result;
        }
        
        Console.WriteLine($"Current Raid: {currentInstance.Instance.Name}");
        foreach (BlizzardMode mode in currentInstance.Modes)
        {
            foreach (BlizzardEncounter encounter in mode.Progress.Encounters)
            {
                DateTimeOffset lastKill = DateTimeOffset.UnixEpoch.AddMilliseconds(encounter.LastKillTimestamp);
                if (lastKill > compareDate)
                {
                    result[_trimBossName(encounter.Encounter.Name)][mode.Difficulty.Type.ToLower()] = true;
                }
            }
        }

        return result;
    }

    private async Task<(string, IList<DungeonRun>)> _getRaiderIoMythicPlusData(string region, string realm, string character)
    {
        RaiderIoProfileResponse response = await _raiderIoHandler.GetWeeklyHighestLevelRuns(region, realm, character);

        IEnumerable<DungeonRun> weeklyRuns =
            response.WeeklyHighestLevelRuns?.Select(x => new DungeonRun() { Level = x.MythicLevel, Name = x.Dungeon })
            ?? new List<DungeonRun>();

        return (_trimClassName(response.Class), weeklyRuns.ToList());
    }

    private static string _trimClassName(string className)
    {
        return className.Replace(" ", "").ToLower();
    }

    private static string _trimBossName(string bossName)
    {
        int commaPos = bossName.IndexOf(',');

        if (commaPos > 0)
            bossName = bossName.Substring(0, commaPos);

        return bossName.Replace(" ", "-").ToLower();
    }

    private static DateTimeOffset _getLastTuesday()
    {
        DateTimeOffset lastTuesday = DateTimeOffset.Now.AddDays(-1);
        while (lastTuesday.DayOfWeek != DayOfWeek.Tuesday)
            lastTuesday = lastTuesday.AddDays(-1);

        return new DateTimeOffset(lastTuesday.Year, lastTuesday.Month, lastTuesday.Day, 15, 0, 0, TimeSpan.Zero);
    }
    
    
    /*
     * Aiming for
     * {
     *  "{character-name}": {
     *    "raid": {
     *      "{boss-name}": {
     *        "mythic": false,
     *        "heroic": false,
     *        "normal": false,
     *        "lfr": false,
     *      },
     *    },
     *    "dungeons": [
     *      { "level": {level}, "name": "{dungeon-name}"},
     *    ]
     * }
     */
}
