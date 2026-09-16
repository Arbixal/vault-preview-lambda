using VaultPreview.Blizzard.Models;
using VaultShared.Seasons;

namespace VaultPreview.Blizzard;

public sealed class BlizzardJournalMetadataProvider(IBlizzardApiHandler blizzardApiHandler)
{
    public async Task<IReadOnlyList<BlizzardJournalMetadata>> GetEligibleInstances(
        string region,
        SeasonActivityDefinition activity,
        CancellationToken cancellationToken = default)
    {
        List<BlizzardJournalMetadata> metadata = [];

        foreach (long instanceId in JournalMetadataResolver.GetEligibleInstanceIds(activity))
        {
            BlizzardJournalMetadata? instance = await blizzardApiHandler.GetJournalInstance(
                region,
                instanceId,
                cancellationToken);
            if (instance != null)
                metadata.Add(instance);
        }

        return metadata;
    }
}
