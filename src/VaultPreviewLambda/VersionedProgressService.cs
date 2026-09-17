using VaultPreview.Blizzard;
using VaultPreview.Blizzard.Models;
using VaultPreview.RaiderIo;
using VaultPreview.RaiderIo.Models;
using VaultPreviewLambda.Calculations;
using VaultPreviewLambda.Models;
using VaultShared.Seasons;

namespace VaultPreviewLambda;

public sealed class VersionedProgressService(
    IBlizzardApiHandler blizzardApiHandler,
    IRaiderIoHandler raiderIoHandler,
    BlizzardJournalMetadataProvider journalMetadataProvider,
    VaultProgressCalculator calculator,
    ISeasonRevisionProvider seasonRevisionProvider,
    ISeasonAwareDelveBaselineProvider delveBaselineProvider)
{
    public async Task<VaultProgressResponse?> Calculate(
        string region,
        string realm,
        string character,
        CancellationToken cancellationToken = default)
    {
        SeasonRevision? revision = await seasonRevisionProvider.GetActiveRevision(cancellationToken);
        if (revision == null)
            return null;

        IReadOnlyList<SeasonActivityDefinition> activities = revision.Configuration.Activities;
        bool requiresBlizzard = activities.Any(activity =>
            string.Equals(activity.Kind, "raid", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(activity.Kind, "delves", StringComparison.OrdinalIgnoreCase));
        if (requiresBlizzard)
            await blizzardApiHandler.Connect();

        BlizzardEncounterResponse? encounterResponse = null;
        IReadOnlyList<BlizzardJournalMetadata> journalMetadata = [];
        if (activities.Any(activity => string.Equals(activity.Kind, "raid", StringComparison.OrdinalIgnoreCase)))
        {
            encounterResponse = await blizzardApiHandler.GetEncounters(region, realm, character);
            List<BlizzardJournalMetadata> metadata = [];
            foreach (SeasonActivityDefinition activity in activities.Where(activity =>
                         string.Equals(activity.Kind, "raid", StringComparison.OrdinalIgnoreCase)))
            {
                metadata.AddRange(await journalMetadataProvider.GetEligibleInstances(
                    region,
                    activity,
                    cancellationToken));
            }

            journalMetadata = metadata
                .GroupBy(entry => entry.Instance.Id)
                .Select(group => group.First())
                .ToList();
        }

        RaiderIoProfileResponse? raiderIoProfile = null;
        if (activities.Any(activity =>
                string.Equals(activity.Kind, "mythic-plus", StringComparison.OrdinalIgnoreCase)))
        {
            raiderIoProfile = await raiderIoHandler.GetWeeklyHighestLevelRuns(region, realm, character);
        }

        IReadOnlyDictionary<int, int>? delveStatistics = null;
        if (activities.Any(activity => string.Equals(activity.Kind, "delves", StringComparison.OrdinalIgnoreCase)))
        {
            delveStatistics = await blizzardApiHandler.GetDelveStatistics(region, realm, character);
        }

        DateTimeOffset asOf = _truncateToMinute(DateTimeOffset.UtcNow);
        DateTimeOffset resetAt = ProgressResetCalculator.GetLastTuesday(asOf);
        return await calculator.Calculate(
            region,
            realm,
            character,
            revision,
            resetAt,
            asOf,
            encounterResponse,
            journalMetadata,
            raiderIoProfile,
            delveStatistics,
            delveBaselineProvider);
    }

    private static DateTimeOffset _truncateToMinute(DateTimeOffset value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, TimeSpan.Zero);
}
