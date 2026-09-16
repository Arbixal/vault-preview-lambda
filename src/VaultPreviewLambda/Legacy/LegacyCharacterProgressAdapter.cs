using System.Text.Json;
using VaultPreviewLambda.Models;

namespace VaultPreviewLambda.Legacy;

public static class LegacyCharacterProgressAdapter
{
    private const string _ENCOUNTER_ID_PREFIX = "wow:journal-encounter:";
    private const string _DELVE_ID_PREFIX = "wow:delve-level:";

    public static IDictionary<string, CharacterProgress> Adapt(VaultProgressResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        CharacterProgress characterProgress = new()
        {
            PlayerClass = response.Character.Class,
            Season = response.Season.SourceSeasonId,
            Raid = _adaptRaid(response),
            Dungeons = _adaptDungeons(response),
            Delves = _adaptDelves(response)
        };

        return new Dictionary<string, CharacterProgress>
        {
            [$"{response.Character.Name}-{response.Character.Realm}"] = characterProgress
        };
    }

    private static IDictionary<string, BossProgress> _adaptRaid(VaultProgressResponse response)
    {
        IDictionary<string, BossProgress> result = new Dictionary<string, BossProgress>();
        if (response.Season.SourceSeasonId is not int sourceSeasonId ||
            !RaidCatalog.Seasons.TryGetValue(sourceSeasonId, out RaidSeasonDefinition? season))
        {
            return result;
        }

        IReadOnlyDictionary<long, string> slugByEncounterId = season.Raids
            .SelectMany(raid => raid.Bosses)
            .ToDictionary(boss => boss.EncounterId, boss => boss.Slug);

        foreach (RaidBossDefinition boss in season.Raids.SelectMany(raid => raid.Bosses))
            result[boss.Slug] = new BossProgress();

        VaultSection? raidSection = response.Sections.FirstOrDefault(x =>
            string.Equals(x.Kind, "raid", StringComparison.OrdinalIgnoreCase));
        if (raidSection == null)
            return result;

        foreach (ProgressItem item in _getAllItems(raidSection))
        {
            if (!_tryGetEncounterId(item.Id, out long encounterId) ||
                !slugByEncounterId.TryGetValue(encounterId, out string? slug) ||
                !result.TryGetValue(slug, out BossProgress? bossProgress))
            {
                continue;
            }

            foreach (ProgressDimension dimension in item.Progress?.Dimensions ?? [])
            {
                string difficulty = dimension.Id.Trim().ToLowerInvariant();
                bossProgress[difficulty] = dimension.Completed == true ||
                                           string.Equals(dimension.State, "complete", StringComparison.OrdinalIgnoreCase);
            }
        }

        return result;
    }

    private static IList<DungeonRun> _adaptDungeons(VaultProgressResponse response)
    {
        VaultSection? section = response.Sections.FirstOrDefault(x =>
            string.Equals(x.Kind, "mythic-plus", StringComparison.OrdinalIgnoreCase));
        if (section == null)
            return [];

        return _getAllItems(section)
            .Select(item =>
            {
                int level = _tryGetInt(item.Progress?.Value, out int parsedLevel) ? parsedLevel : 0;
                string name = item.Tooltip?.Title ?? _getDungeonName(item.Label, level);
                return new DungeonRun { Level = level, Name = name };
            })
            .Where(run => run.Level > 0 && !string.IsNullOrWhiteSpace(run.Name))
            .ToList();
    }

    private static IDictionary<int, int> _adaptDelves(VaultProgressResponse response)
    {
        Dictionary<int, int> result = Enumerable.Range(1, 11)
            .ToDictionary(level => level, _ => 0);
        VaultSection? section = response.Sections.FirstOrDefault(x =>
            string.Equals(x.Kind, "delves", StringComparison.OrdinalIgnoreCase));
        if (section == null)
            return result;

        foreach (ProgressItem item in _getAllItems(section))
        {
            int? level = _tryGetDelveLevel(item);
            int? completed = item.Progress?.Completed;
            if (level.HasValue && completed.HasValue && level is >= 1 and <= 11)
                result[level.Value] = completed.Value;
        }

        return result;
    }

    private static IEnumerable<ProgressItem> _getAllItems(VaultSection section)
    {
        return section.Slots
            .SelectMany(slot => slot.Items)
            .Concat(section.AdditionalItems)
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.First());
    }

    private static bool _tryGetEncounterId(string id, out long encounterId)
    {
        if (!id.StartsWith(_ENCOUNTER_ID_PREFIX, StringComparison.OrdinalIgnoreCase))
        {
            encounterId = 0;
            return false;
        }

        return long.TryParse(id[_ENCOUNTER_ID_PREFIX.Length..], out encounterId);
    }

    private static int? _tryGetDelveLevel(ProgressItem item)
    {
        if (_tryGetInt(item.Progress?.Value, out int value))
            return value;

        if (item.Id.StartsWith(_DELVE_ID_PREFIX, StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(item.Id[_DELVE_ID_PREFIX.Length..], out int idValue))
        {
            return idValue;
        }

        return null;
    }

    private static bool _tryGetInt(object? value, out int result)
    {
        switch (value)
        {
            case int intValue:
                result = intValue;
                return true;
            case long longValue when longValue is >= int.MinValue and <= int.MaxValue:
                result = (int)longValue;
                return true;
            case double doubleValue when doubleValue is >= int.MinValue and <= int.MaxValue:
                result = (int)doubleValue;
                return true;
            case JsonElement { ValueKind: JsonValueKind.Number } jsonNumber when jsonNumber.TryGetInt32(out int jsonValue):
                result = jsonValue;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } jsonString when jsonString.TryGetInt32(out int parsedValue):
                result = parsedValue;
                return true;
            case string stringValue when int.TryParse(stringValue, out int parsedValue):
                result = parsedValue;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static string _getDungeonName(string label, int level)
    {
        string suffix = $" +{level}";
        return label.EndsWith(suffix, StringComparison.Ordinal)
            ? label[..^suffix.Length]
            : label;
    }
}
