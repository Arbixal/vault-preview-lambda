using VaultPreview.Blizzard.Models;
using VaultShared.Seasons;

namespace VaultPreview.Blizzard;

public static class JournalMetadataResolver
{
    private const string _INSTANCE_SOURCE_PREFIX = "wow:journal-instance:";

    public static IReadOnlyList<long> GetCharacterInstanceIds(BlizzardEncounterResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return response.Expansions
            .SelectMany(expansion => expansion.Instances)
            .Select(instance => instance.Instance.Id)
            .Where(id => id > 0)
            .Distinct()
            .ToList();
    }

    public static IReadOnlyList<long> GetEligibleInstanceIds(SeasonActivityDefinition activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        return activity.SourceIds
            .Select(_parseInstanceId)
            .Where(instanceId => instanceId.HasValue)
            .Select(instanceId => instanceId!.Value)
            .Distinct()
            .ToList();
    }

    public static IReadOnlyList<BlizzardJournalInstance> SelectEligibleInstances(
        SeasonActivityDefinition activity,
        IEnumerable<BlizzardJournalInstance> instances)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(instances);

        IReadOnlyDictionary<long, int> orderByInstanceId = GetEligibleInstanceIds(activity)
            .Select((instanceId, order) => (instanceId, order))
            .ToDictionary(x => x.instanceId, x => x.order);

        return instances
            .Where(instance => orderByInstanceId.ContainsKey(instance.Id))
            .OrderBy(instance => orderByInstanceId[instance.Id])
            .ToList();
    }

    private static long? _parseInstanceId(string sourceId)
    {
        if (!sourceId.StartsWith(_INSTANCE_SOURCE_PREFIX, StringComparison.OrdinalIgnoreCase))
            return null;

        return long.TryParse(sourceId[_INSTANCE_SOURCE_PREFIX.Length..], out long instanceId)
            ? instanceId
            : null;
    }
}
