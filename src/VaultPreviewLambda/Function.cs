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
    /// <param name="characters">Comma-separated list of characters (e.g. bixshift-nagrand,bixsham-nagrand,bixmonk-nagrand)</param>
    /// <returns></returns>
    [LambdaFunction]
    [HttpApi(LambdaHttpMethod.Get,"/vault-progress/{characters}")]
    public async Task<IDictionary<string, CharacterProgress>> FunctionHandler(string characters)
    {
        Dictionary<string, CharacterProgress> progress = new Dictionary<string, CharacterProgress>();

        if (string.IsNullOrEmpty(characters))
        {
            return progress;
        }
        
        await _blizzardApiHandler.Connect();
        
        string[] characterList = characters.Split(",", StringSplitOptions.TrimEntries);

        foreach (string aCharacter in characterList)
        {
            CharacterProgress characterProgress = new CharacterProgress();
            
            string[] characterSplit = aCharacter.Split("-");
            
            Console.WriteLine($"{characterSplit[0]} from {characterSplit[1]}");

            characterProgress.Raid = await _getBlizzardRaidData(characterSplit[1], characterSplit[0]);

            characterProgress.Dungeons = await _getRaiderIoMythicPlusData(characterSplit[1], characterSplit[0]);
            
            progress.Add(aCharacter, characterProgress);
        }

        return progress;
    }

    private async Task<IDictionary<string, BossProgress>> _getBlizzardRaidData(string realm, string character)
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
            await _blizzardApiHandler.GetEncounters(realm, character);
            
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

    private async Task<IList<DungeonRun>> _getRaiderIoMythicPlusData(string realm, string character)
    {
        IList<RaiderIoDungeonRun> response = await _raiderIoHandler.GetWeeklyHighestLevelRuns(realm, character);

        return response.Select(x => new DungeonRun() { Level = x.MythicLevel, Name = x.Dungeon })
            .ToList();
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
