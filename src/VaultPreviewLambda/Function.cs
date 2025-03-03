using Amazon.Lambda.Annotations;
using Amazon.Lambda.Annotations.APIGateway;
using Amazon.Lambda.Core;
using VaultPreviewLambda.Models;
using VaultPreviewLambda.Models.Blizzard;
using VaultPreviewLambda.Models.RaiderIo;

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
        
        characterProgress.Season = await _blizzardApiHandler.GetSeason(region);

        characterProgress.Raid = await _getBlizzardRaidData(region, realm, character, characterProgress.Season);

        (characterProgress.PlayerClass, characterProgress.Dungeons) = await _getRaiderIoMythicPlusData(region, realm, character);
        
        progress.Add($"{character}-{realm}", characterProgress);

        return progress;
    }

    private async Task<IDictionary<string, BossProgress>> _getBlizzardRaidData(string region, string realm, string character, int? currentSeason)
    {
        const string EXPANSION = "Current Season";
        const int AMIRDRASSIL_INSTANCE = 1207; // Amirdrassil
        const int VAULT_INSTANCE = 1200; // Vault of the Incarnates
        const int ABERRUS_INSTANCE = 1208; // Aberrus
        const int NERUBAR_PALACE = 1273; // Nerubar Palace
        const int UNDERMINE = 1296; // Liberation of Undermine
        string[] AMIRDRASSIL_BOSSES =
        [
            "gnarlroot", "igira-the-cruel", "volcoross", "council-of-dreams", "larodar", "nymue", "smolderon", "tindral-sageswift", "fyrakk-the-blazing"
        ];

        string[] VAULT_BOSSES =
        [
            "eranog", "terros", "the-primal-council", "sennarth", "dathea", "kurog-grimtotem", "broodkeeper-diurna", "raszageth-the-storm-eater"
        ];

        string[] ABERRUS_BOSSES =
        [
            "kazzara", "the-amalgamation-chamber", "the-forgotten-experiments", "assault-of-the-zaqali", "rashok", "the-vigilant-steward", "magmorax", 
            "echo-of-neltharion", "scalecommander-sarkareth"
        ];

        string[] NERUBAR_BOSSES =
        [
            "ulgrax-the-devourer", "the-bloodbound-horror", "sikran", "rashanan", "broodtwister-ovinax", "nexus-princess-kyveza", "the-silken-court", "queen-ansurek"
        ];

        string[] UNDERMINE_BOSSES =
        [
            "vexie-and-the-geargrinders", "cauldron-of-carnage", "rik-reverb", "stix-bunkjunker", "sprocketmonger-lockenstock", "the-one-armed-bandit", "mugzee", "chrome-king-gallywix"
        ];

        Dictionary<int, string[]> bossList = new Dictionary<int, string[]>
        {
            [9] = VAULT_BOSSES,
            [10] = ABERRUS_BOSSES,
            [11] = AMIRDRASSIL_BOSSES,
            [12] = [.. AMIRDRASSIL_BOSSES, .. VAULT_BOSSES, .. ABERRUS_BOSSES],
            [13] = NERUBAR_BOSSES,
            [14] = UNDERMINE_BOSSES
        };

        Dictionary<int, int[]> instanceList = new Dictionary<int, int[]>
        {
            [9] = [VAULT_INSTANCE],
            [10] = [ABERRUS_INSTANCE],
            [11] = [AMIRDRASSIL_INSTANCE],
            [12] = [VAULT_INSTANCE, ABERRUS_INSTANCE, AMIRDRASSIL_INSTANCE],
            [13] = [NERUBAR_PALACE],
            [14] = [UNDERMINE]
        };
        
        DateTimeOffset compareDate = _getLastTuesday();

        IDictionary<string, BossProgress> result = new Dictionary<string, BossProgress>();

        string[] bosses = (currentSeason.HasValue ? bossList.GetValueOrDefault(currentSeason.Value) : []) ?? [];

        // Seed data
        foreach (string aBoss in bosses)
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
        foreach (BlizzardInstance currentInstance in currentExpansion.Instances)
        {
            int[] instances = (currentSeason.HasValue ? instanceList.GetValueOrDefault(currentSeason.Value) : []) ?? [];

            if (instances.All(x => x != currentInstance.Instance.Id))
                continue;
            
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
        DateTimeOffset today = DateTimeOffset.UtcNow;
        if (today is { DayOfWeek: DayOfWeek.Tuesday, Hour: >= 15 })
        {
            return new DateTimeOffset(today.Year, today.Month, today.Day, 15, 0, 0, TimeSpan.Zero);
        }
        
        DateTimeOffset lastTuesday = DateTimeOffset.UtcNow.AddDays(-1);
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
