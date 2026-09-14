using Amazon.Lambda.Annotations;
using Amazon.Lambda.Annotations.APIGateway;
using Amazon.Lambda.Core;
using VaultPreview.Blizzard;
using VaultPreview.Blizzard.Models;
using VaultPreview.RaiderIo;
using VaultPreview.RaiderIo.Models;
using VaultPreview.VaultCache;
using VaultPreview.VaultCache.Models;
using VaultPreviewLambda.Models;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace VaultPreviewLambda;

public class Function
{
    private readonly IBlizzardApiHandler _blizzardApiHandler;
    private readonly IRaiderIoHandler _raiderIoHandler;
    private readonly IVaultCacheHandler _vaultCacheHandler;

    public Function(
        IBlizzardApiHandler blizzardApiHandler,
        IRaiderIoHandler raiderIoHandler,
        IVaultCacheHandler vaultCacheHandler
        )
    {
        _blizzardApiHandler = blizzardApiHandler;
        _raiderIoHandler = raiderIoHandler;
        _vaultCacheHandler = vaultCacheHandler;
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

        characterProgress.Delves = await _getDelveData(region, realm, character);

        (characterProgress.PlayerClass, characterProgress.Dungeons) = await _getRaiderIoMythicPlusData(region, realm, character);
        
        progress.Add($"{character}-{realm}", characterProgress);

        return progress;
    }

    private async Task<IDictionary<string, BossProgress>> _getBlizzardRaidData(string region, string realm, string character, int? currentSeason)
    {
        const string EXPANSION = "Current Season";
        RaidSeasonDefinition? configuredSeason = null;
        if (currentSeason.HasValue &&
            RaidCatalog.Seasons.TryGetValue(currentSeason.Value, out RaidSeasonDefinition? season))
        {
            configuredSeason = season;
        }

        IReadOnlyList<RaidDefinition> raids = configuredSeason?.Raids ?? [];
        
        DateTimeOffset compareDate = _getLastTuesday();

        IDictionary<string, BossProgress> result = new Dictionary<string, BossProgress>();

        // Seed data
        foreach (RaidBossDefinition boss in raids.SelectMany(x => x.Bosses))
        {
            result.Add(boss.Slug, new BossProgress());
        }
        
        BlizzardEncounterResponse response =
            await _blizzardApiHandler.GetEncounters(region, realm, character);
            
        Console.WriteLine($"Expansions: {response.Expansions.Count}");
        BlizzardExpansion? currentExpansion = response.Expansions.FirstOrDefault(x =>
            string.Equals(x.Expansion.Name, EXPANSION, StringComparison.OrdinalIgnoreCase));

        currentExpansion ??= response.Expansions.FirstOrDefault(x =>
            x.Instances.Any(instance => raids.Any(raid => raid.InstanceId == instance.Instance.Id)));

        if (currentExpansion == null)
        {
            // Return blank boss progress
            return result;
        }
            
        Console.WriteLine($"Instances: {currentExpansion.Instances.Count}");
        foreach (BlizzardInstance currentInstance in currentExpansion.Instances)
        {
            RaidDefinition? currentRaid = raids.FirstOrDefault(x => x.InstanceId == currentInstance.Instance.Id);
            if (currentRaid is null)
                continue;
            
            Console.WriteLine($"Current Raid: {currentInstance.Instance.Name}");
            foreach (BlizzardMode mode in currentInstance.Modes)
            {
                foreach (BlizzardEncounter encounter in mode.Progress.Encounters)
                {
                    DateTimeOffset lastKill = DateTimeOffset.UnixEpoch.AddMilliseconds(encounter.LastKillTimestamp);
                    if (lastKill > compareDate)
                    {
                        RaidBossDefinition? boss = currentRaid.Bosses.FirstOrDefault(x =>
                            x.EncounterId == encounter.Encounter.Id);
                        if (boss != null && result.TryGetValue(boss.Slug, out BossProgress? bossProgress))
                        {
                            bossProgress[mode.Difficulty.Type.ToLowerInvariant()] = true;
                        }
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

        return (string.IsNullOrEmpty(response.Class) ? string.Empty : _trimClassName(response.Class), weeklyRuns.ToList());
    }

    private async Task<Dictionary<int, int>> _getDelveData(string region, string realm, string character)
    {
        Dictionary<int, int> delveData = new Dictionary<int, int>
        {
            [1] = 0,
            [2] = 0,
            [3] = 0,
            [4] = 0,
            [5] = 0,
            [6] = 0,
            [7] = 0,
            [8] = 0,
            [9] = 0,
            [10] = 0,
            [11] = 0,
        };
        
        CharacterData? characterData = await _vaultCacheHandler.GetCharacter(region, realm, character);
        
        Dictionary<int,int> delveStatistics = await _blizzardApiHandler.GetDelveStatistics(region, realm, character);
        
        if (characterData == null)
        {
            Console.WriteLine("characterData doesn't exist");
            // Doesn't exist, should create it and save it
            characterData = new CharacterData(character, realm, region);
            characterData.SetDelveData(delveStatistics);

            await _vaultCacheHandler.SaveCharacter(characterData);
            return delveData;
        }

        for (int i = 1; i <= 11; ++i)
        {
            delveData[i] = Math.Max(0, delveStatistics[i] - characterData.DelvesCompleted[i]);
            
            Console.WriteLine($"Delve {i}: {delveStatistics[i]} - {characterData.DelvesCompleted[i]} = {delveData[i]}");
        }

        return delveData;
    }

    private static string _trimClassName(string className)
    {
        return className.Replace(" ", "").ToLowerInvariant();
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
