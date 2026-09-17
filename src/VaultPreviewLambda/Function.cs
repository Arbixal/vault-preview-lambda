using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
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
using VaultShared.Seasons;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace VaultPreviewLambda;

public class Function
{
    private readonly IBlizzardApiHandler _blizzardApiHandler;
    private readonly IRaiderIoHandler _raiderIoHandler;
    private readonly IVaultCacheHandler _vaultCacheHandler;
    private readonly IActiveSeasonRevisionProvider _activeSeasonRevisionProvider;
    private readonly ISeasonRevisionProvider? _seasonRevisionProvider;
    private readonly VersionedProgressService? _versionedProgressService;

    private const string _CONFIG_CACHE_CONTROL = "public, max-age=60, stale-while-revalidate=300";
    private const string _PROGRESS_CACHE_CONTROL = "public, max-age=30, stale-while-revalidate=60";

    public Function(
        IBlizzardApiHandler blizzardApiHandler,
        IRaiderIoHandler raiderIoHandler,
        IVaultCacheHandler vaultCacheHandler,
        IActiveSeasonRevisionProvider activeSeasonRevisionProvider,
        ISeasonRevisionProvider? seasonRevisionProvider = null,
        VersionedProgressService? versionedProgressService = null
        )
    {
        _blizzardApiHandler = blizzardApiHandler;
        _raiderIoHandler = raiderIoHandler;
        _vaultCacheHandler = vaultCacheHandler;
        _activeSeasonRevisionProvider = activeSeasonRevisionProvider;
        _seasonRevisionProvider = seasonRevisionProvider;
        _versionedProgressService = versionedProgressService;
    }

    [LambdaFunction]
    [HttpApi(LambdaHttpMethod.Get, "/v1/app-config")]
    public async Task<IHttpResult> GetAppConfig(
        [FromHeader(Name = "If-None-Match")] string ifNoneMatch,
        [FromHeader(Name = "Origin")] string origin)
    {
        if (_seasonRevisionProvider == null)
        {
            return _createError(
                HttpStatusCode.ServiceUnavailable,
                "ACTIVE_CONFIGURATION_UNAVAILABLE",
                "Active application configuration is unavailable.",
                origin);
        }

        try
        {
            SeasonRevision? revision = await _seasonRevisionProvider.GetActiveRevision();
            if (revision == null)
            {
                return _createError(
                    HttpStatusCode.ServiceUnavailable,
                    "ACTIVE_CONFIGURATION_UNAVAILABLE",
                    "Active application configuration is unavailable.",
                    origin);
            }

            string entityTag = _getRevisionEntityTag(revision);
            if (_matchesEntityTag(ifNoneMatch, entityTag))
            {
                return _withHeaders(
                    HttpResults.NewResult(HttpStatusCode.NotModified, null),
                    origin,
                    _CONFIG_CACHE_CONTROL,
                    entityTag);
            }

            AppConfigResponse response = new()
            {
                ActiveSeason = _toSeasonSnapshot(revision)
            };
            return _withHeaders(
                HttpResults.Ok(response),
                origin,
                _CONFIG_CACHE_CONTROL,
                entityTag);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Unable to serve active application configuration: {exception.GetType().Name}");
            return _createError(
                HttpStatusCode.ServiceUnavailable,
                "ACTIVE_CONFIGURATION_UNAVAILABLE",
                "Active application configuration is unavailable.",
                origin);
        }
    }

    [LambdaFunction]
    [HttpApi(LambdaHttpMethod.Get, "/v1/vault-progress/{region}/{realm}/{character}")]
    public async Task<IHttpResult> GetVaultProgress(
        string region,
        string realm,
        string character,
        [FromHeader(Name = "If-None-Match")] string ifNoneMatch,
        [FromHeader(Name = "Origin")] string origin)
    {
        if (!_isValidRequest(region, realm, character))
        {
            return _createError(
                HttpStatusCode.BadRequest,
                "INVALID_REQUEST",
                "The request is invalid.",
                origin);
        }

        if (_versionedProgressService == null)
        {
            return _createError(
                HttpStatusCode.ServiceUnavailable,
                "ACTIVE_CONFIGURATION_UNAVAILABLE",
                "Active application configuration is unavailable.",
                origin);
        }

        try
        {
            VaultProgressResponse? response = await _versionedProgressService.Calculate(
                region.Trim().ToLowerInvariant(),
                realm.Trim(),
                character.Trim());
            if (response == null)
            {
                return _createError(
                    HttpStatusCode.ServiceUnavailable,
                    "ACTIVE_CONFIGURATION_UNAVAILABLE",
                    "Active application configuration is unavailable.",
                    origin);
            }

            string entityTag = _getResponseEntityTag(response);
            if (_matchesEntityTag(ifNoneMatch, entityTag))
            {
                return _withHeaders(
                    HttpResults.NewResult(HttpStatusCode.NotModified, null),
                    origin,
                    _PROGRESS_CACHE_CONTROL,
                    entityTag);
            }

            return _withHeaders(
                HttpResults.Ok(response),
                origin,
                _PROGRESS_CACHE_CONTROL,
                entityTag);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return _createError(
                HttpStatusCode.NotFound,
                "CHARACTER_NOT_FOUND",
                "Character data is not available.",
                origin);
        }
        catch (HttpRequestException exception)
        {
            Console.WriteLine($"Character progress upstream request failed: {exception.GetType().Name}");
            return _createError(
                HttpStatusCode.ServiceUnavailable,
                "UPSTREAM_UNAVAILABLE",
                "Required upstream data is unavailable.",
                origin);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Character progress calculation failed: {exception.GetType().Name}");
            return _createError(
                HttpStatusCode.ServiceUnavailable,
                "UPSTREAM_UNAVAILABLE",
                "Required upstream data is unavailable.",
                origin);
        }
    }

    [LambdaFunction]
    [HttpApi(LambdaHttpMethod.Options, "/v1/app-config")]
    public IHttpResult GetAppConfigOptions([FromHeader(Name = "Origin")] string origin) =>
        _withHeaders(HttpResults.Ok(null), origin, "no-store", null);

    [LambdaFunction]
    [HttpApi(LambdaHttpMethod.Options, "/v1/vault-progress/{region}/{realm}/{character}")]
    public IHttpResult GetVaultProgressOptions(
        string region,
        string realm,
        string character,
        [FromHeader(Name = "Origin")] string origin) =>
        _withHeaders(HttpResults.Ok(null), origin, "no-store", null);
    
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

        ActiveSeasonRevision? activeSeason = await _activeSeasonRevisionProvider.GetActive();
        characterProgress.Delves = await _getDelveData(region, realm, character, activeSeason);

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

    private async Task<Dictionary<int, int>> _getDelveData(
        string region,
        string realm,
        string character,
        ActiveSeasonRevision? activeSeason)
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
        
        if (activeSeason == null)
        {
            Console.WriteLine("No active season revision is configured; skipping Delve baseline calculation.");
            return delveData;
        }

        CharacterData? characterData = await _vaultCacheHandler.GetCharacter(region, realm, character);
        
        Dictionary<int,int> delveStatistics = await _blizzardApiHandler.GetDelveStatistics(region, realm, character);
        
        bool baselineMatches = characterData != null &&
                               characterData.SeasonId == activeSeason.SeasonId &&
                               characterData.SeasonRevision == activeSeason.Revision &&
                               characterData.SeasonRevisionHash == activeSeason.RevisionHash;
        if (!baselineMatches)
        {
            Console.WriteLine("Character baseline is missing or belongs to another season revision.");
            characterData ??= new CharacterData(character, realm, region);
            characterData.SetDelveBaseline(delveStatistics, activeSeason);

            await _vaultCacheHandler.SaveCharacter(characterData);
            return delveData;
        }

        CharacterData baseline = characterData!;
        foreach (int level in delveStatistics.Keys.OrderBy(x => x))
        {
            delveData[level] = Math.Max(
                0,
                delveStatistics.GetValueOrDefault(level) - baseline.DelvesCompleted.GetValueOrDefault(level));
            
            Console.WriteLine($"Delve {level}: {delveStatistics.GetValueOrDefault(level)} - {baseline.DelvesCompleted.GetValueOrDefault(level)} = {delveData[level]}");
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

    private static bool _isValidRequest(string region, string realm, string character)
    {
        return region.Trim().Length == 2 &&
               region.Trim().All(char.IsLetter) &&
               !string.IsNullOrWhiteSpace(realm) &&
               !string.IsNullOrWhiteSpace(character);
    }

    private static SeasonSnapshot _toSeasonSnapshot(SeasonRevision revision)
    {
        return new SeasonSnapshot
        {
            Id = revision.Configuration.Id,
            DisplayName = revision.Configuration.DisplayName,
            ShortLabel = revision.Configuration.ShortLabel,
            Expansion = revision.Configuration.Expansion,
            SourceSeasonId = revision.Configuration.SourceSeasonId,
            Revision = revision.Id,
            RevisionHash = revision.RevisionHash
        };
    }

    private static IHttpResult _createError(
        HttpStatusCode statusCode,
        string code,
        string message,
        string? origin)
    {
        return _withHeaders(
            HttpResults.NewResult(
                statusCode,
                new ApiErrorResponse
                {
                    Error = new ApiError { Code = code, Message = message }
                }),
            origin,
            "no-store",
            null);
    }

    private static IHttpResult _withHeaders(
        IHttpResult result,
        string? origin,
        string cacheControl,
        string? entityTag)
    {
        result.AddHeader("Cache-Control", cacheControl)
            .AddHeader("Access-Control-Allow-Methods", "GET, OPTIONS")
            .AddHeader("Access-Control-Allow-Headers", "If-None-Match, Content-Type")
            .AddHeader("Vary", "Origin");

        string? allowedOrigin = CorsPolicy.GetAllowedOrigin(origin);
        if (allowedOrigin != null)
            result.AddHeader("Access-Control-Allow-Origin", allowedOrigin);
        if (entityTag != null)
            result.AddHeader("ETag", entityTag);

        return result;
    }

    private static string _getRevisionEntityTag(SeasonRevision revision) =>
        $"\"{revision.RevisionHash}\"";

    private static string _getResponseEntityTag(VaultProgressResponse response)
    {
        byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(response);
        byte[] hash = SHA256.HashData(serialized);
        return $"\"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}\"";
    }

    private static bool _matchesEntityTag(string? ifNoneMatch, string entityTag)
    {
        if (string.IsNullOrWhiteSpace(ifNoneMatch))
            return false;
        if (ifNoneMatch.Trim() == "*")
            return true;

        string normalizedEntityTag = entityTag.Trim();
        return ifNoneMatch
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value.StartsWith("W/", StringComparison.OrdinalIgnoreCase)
                ? value[2..].Trim()
                : value)
            .Any(value => string.Equals(value, normalizedEntityTag, StringComparison.Ordinal));
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
