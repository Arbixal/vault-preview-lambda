using VaultPreview.Blizzard;
using VaultPreview.Blizzard.Models;
using VaultPreview.RaiderIo.Models;
using VaultPreviewLambda.Models;
using VaultShared.Seasons;

namespace VaultPreviewLambda.Calculations;

public sealed class VaultProgressCalculator
{
    public async Task<VaultProgressResponse> Calculate(
        string region,
        string realm,
        string character,
        SeasonRevision revision,
        DateTimeOffset resetAt,
        DateTimeOffset asOf,
        BlizzardEncounterResponse? encounterResponse,
        IReadOnlyList<BlizzardJournalMetadata> journalMetadata,
        RaiderIoProfileResponse? raiderIoProfile,
        IReadOnlyDictionary<int, int>? delveStatistics,
        ISeasonAwareDelveBaselineProvider delveBaselineProvider)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(journalMetadata);
        ArgumentNullException.ThrowIfNull(delveBaselineProvider);

        List<VaultSection> sections = [];
        foreach (SeasonActivityDefinition activity in revision.Configuration.Activities.OrderBy(x => x.Order))
        {
            string kind = activity.Kind.Trim().ToLowerInvariant();
            VaultSection section = kind switch
            {
                "raid" => _calculateRaid(activity, encounterResponse, journalMetadata, resetAt),
                "mythic-plus" => _calculateMythicPlus(activity, raiderIoProfile),
                "delves" => await _calculateDelves(
                    activity,
                    region,
                    realm,
                    character,
                    revision,
                    delveStatistics,
                    delveBaselineProvider),
                _ => _createUnsupportedSection(activity)
            };
            sections.Add(section);
        }

        return new VaultProgressResponse
        {
            Character = new CharacterIdentity
            {
                Region = region,
                Realm = realm,
                Name = character,
                Class = raiderIoProfile?.Class is { Length: > 0 } className
                    ? _trimClassName(className)
                    : null
            },
            Season = new SeasonSnapshot
            {
                Id = revision.Configuration.Id,
                DisplayName = revision.Configuration.DisplayName,
                ShortLabel = revision.Configuration.ShortLabel,
                Expansion = revision.Configuration.Expansion,
                SourceSeasonId = revision.Configuration.SourceSeasonId,
                Revision = revision.Id,
                RevisionHash = revision.RevisionHash
            },
            ProgressPeriod = new ProgressPeriod { ResetAt = resetAt, AsOf = asOf },
            Sections = sections
        };
    }

    private static VaultSection _calculateRaid(
        SeasonActivityDefinition activity,
        BlizzardEncounterResponse? encounterResponse,
        IReadOnlyList<BlizzardJournalMetadata> journalMetadata,
        DateTimeOffset resetAt)
    {
        IReadOnlyList<long> eligibleIds = JournalMetadataResolver.GetEligibleInstanceIds(activity);
        Dictionary<long, BlizzardJournalMetadata> metadataById = journalMetadata
            .Where(x => eligibleIds.Contains(x.Instance.Id))
            .GroupBy(x => x.Instance.Id)
            .ToDictionary(x => x.Key, x => x.First());

        if (metadataById.Count == 0)
        {
            return _createUnavailableSection(activity, "No eligible Journal metadata is available.");
        }

        bool missingMetadata = eligibleIds.Any(id => !metadataById.ContainsKey(id));

        List<ProgressItem> encounterItems = [];
        foreach (long instanceId in eligibleIds)
        {
            if (!metadataById.TryGetValue(instanceId, out BlizzardJournalMetadata? metadata))
                continue;

            IList<BlizzardMode> modes = _getModes(encounterResponse, instanceId).ToList();
            foreach (BlizzardJournalEncounter encounter in metadata.Instance.Encounters)
            {
                List<ProgressDimension> dimensions = modes
                    .Select((mode, index) => _getDifficultyProgress(mode, encounter.Id, index, resetAt))
                    .ToList();
                bool? completed = dimensions.Count == 0
                    ? null
                    : dimensions.Any(x => x.Completed == true);
                Reward? evidenceReward = _getDimensionReward(activity, dimensions);

                encounterItems.Add(new ProgressItem
                {
                    Id = $"wow:journal-encounter:{encounter.Id}",
                    Label = encounter.Name,
                    State = completed == null ? "unknown" : completed.Value ? "complete" : "incomplete",
                    ItemLevel = evidenceReward?.ItemLevel,
                    Rarity = evidenceReward?.Rarity,
                    Progress = new ProgressData { Dimensions = dimensions }
                });
            }
        }

        encounterItems = encounterItems
            .OrderByDescending(_getItemQuality)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList();
        int maxDisplayItems = activity.Slots.Max(x => x.DisplayItemCount);
        IList<ProgressItem> additionalItems = encounterItems.Skip(maxDisplayItems).ToList();
        int completedCount = encounterItems.Count(x => x.State == "complete");
        bool stale = metadataById.Values.Any(x => x.IsStale);

        return new VaultSection
        {
            Id = activity.Id,
            Title = activity.Title,
            Subtitle = activity.Subtitle,
            Kind = activity.Kind,
            Status = missingMetadata ? "unavailable" : "available",
            Freshness = missingMetadata || stale ? "stale" : "fresh",
            Slots = activity.Slots.Select(slot => _createSlot(
                slot,
                completedCount,
                encounterItems,
                countBasedEvidence: false)).ToList(),
            AdditionalItems = additionalItems
        };
    }

    private static VaultSection _calculateMythicPlus(
        SeasonActivityDefinition activity,
        RaiderIoProfileResponse? profile)
    {
        if (profile?.WeeklyHighestLevelRuns == null)
            return _createUnavailableSection(activity, "Mythic+ data is unavailable.");

        IList<RaiderIoDungeonRun> runs = profile.WeeklyHighestLevelRuns
            .OrderByDescending(x => x.MythicLevel)
            .ThenByDescending(x => x.Score)
            .ThenBy(x => x.ClearTimeMs)
            .ThenBy(x => x.CompletedAt)
            .ThenBy(x => x.Dungeon, StringComparer.OrdinalIgnoreCase)
            .ToList();
        IList<ProgressItem> items = runs.Select((run, index) =>
        {
            Reward? evidenceReward = _getValueReward(activity, run.MythicLevel);
            return new ProgressItem
            {
                Id = $"raiderio:run:{run.MapChallengeModeId}:{run.CompletedAt.Ticks}:{index}",
                Label = $"{run.Dungeon} +{run.MythicLevel}",
                State = "complete",
                ItemLevel = evidenceReward?.ItemLevel,
                Rarity = evidenceReward?.Rarity,
                Progress = new ProgressData { Value = run.MythicLevel },
                Tooltip = new Tooltip
                {
                    Title = run.Dungeon,
                    Rows = [new TooltipRow { Label = "Mythic level", Value = $"+{run.MythicLevel}" }]
                }
            };
        }).ToList();
        int maxDisplayItems = activity.Slots.Max(x => x.DisplayItemCount);

        return new VaultSection
        {
            Id = activity.Id,
            Title = activity.Title,
            Subtitle = activity.Subtitle,
            Kind = activity.Kind,
            Status = "available",
            Freshness = "fresh",
            Slots = activity.Slots.Select(slot => _createSlot(
                slot,
                items.Count,
                items,
                countBasedEvidence: false)).ToList(),
            AdditionalItems = items.Skip(maxDisplayItems).ToList()
        };
    }

    private static async Task<VaultSection> _calculateDelves(
        SeasonActivityDefinition activity,
        string region,
        string realm,
        string character,
        SeasonRevision revision,
        IReadOnlyDictionary<int, int>? statistics,
        ISeasonAwareDelveBaselineProvider baselineProvider)
    {
        if (statistics == null)
            return _createUnavailableSection(activity, "Delve data is unavailable.");

        DelveBaseline? baseline = await baselineProvider.GetBaseline(region, realm, character);
        bool baselineMatches = baseline != null &&
                               baseline.SeasonId == revision.Configuration.Id &&
                               baseline.Revision == revision.Id &&
                               baseline.RevisionHash == revision.RevisionHash;
        IEnumerable<int> configuredLevels = activity.ProgressRules
            .Where(rule => string.IsNullOrWhiteSpace(rule.Dimension))
            .Select(rule => rule.MinimumValue);
        IEnumerable<int> baselineLevels = baseline?.Completed.Keys ?? [];
        IEnumerable<int> levels = statistics.Keys
            .Concat(baselineLevels)
            .Concat(configuredLevels)
            .Where(level => level > 0)
            .Distinct()
            .OrderBy(level => level);
        Dictionary<int, int> completedByLevel = levels
            .ToDictionary(level => level, level => Math.Max(
                0,
                statistics.GetValueOrDefault(level) -
                (baselineMatches ? baseline!.Completed.GetValueOrDefault(level) : statistics.GetValueOrDefault(level))));
        int totalCompleted = completedByLevel.Values.Sum();
        IList<ProgressItem> items = completedByLevel
            .Where(x => x.Value > 0)
            .OrderByDescending(x => x.Key)
            .Select(x =>
            {
                Reward? evidenceReward = _getValueReward(activity, x.Key);
                return new ProgressItem
                {
                    Id = $"wow:delve-level:{x.Key}",
                    Label = $"Tier {x.Key}",
                    State = "complete",
                    ItemLevel = evidenceReward?.ItemLevel,
                    Rarity = evidenceReward?.Rarity,
                    Progress = new ProgressData { Value = x.Key, Completed = x.Value }
                };
            })
            .ToList();
        int maxDisplayItems = activity.Slots.Max(x => x.DisplayItemCount);

        return new VaultSection
        {
            Id = activity.Id,
            Title = activity.Title,
            Subtitle = activity.Subtitle,
            Kind = activity.Kind,
            Status = "available",
            Freshness = "fresh",
            Slots = activity.Slots.Select(slot => _createSlot(
                slot,
                totalCompleted,
                items,
                countBasedEvidence: true)).ToList(),
            AdditionalItems = items.Skip(maxDisplayItems).ToList()
        };
    }

    private static Reward? _getDimensionReward(
        SeasonActivityDefinition activity,
        IEnumerable<ProgressDimension> dimensions)
    {
        HashSet<string> completedDimensions = dimensions
            .Where(x => x.Completed == true)
            .Select(x => x.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return activity.ProgressRules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Dimension) &&
                           completedDimensions.Contains(rule.Dimension))
            .OrderByDescending(rule => rule.ItemLevel)
            .Select(rule => new Reward { ItemLevel = rule.ItemLevel, Rarity = rule.Rarity })
            .FirstOrDefault();
    }

    private static Reward? _getValueReward(SeasonActivityDefinition activity, int value)
    {
        return activity.ProgressRules
            .Where(rule => string.IsNullOrWhiteSpace(rule.Dimension) && rule.MinimumValue <= value)
            .OrderByDescending(rule => rule.MinimumValue)
            .Select(rule => new Reward { ItemLevel = rule.ItemLevel, Rarity = rule.Rarity })
            .FirstOrDefault();
    }

    private static int _getItemQuality(ProgressItem item)
    {
        return item.ItemLevel ?? (item.State == "complete" ? 1 : 0);
    }

    private static VaultSlot _createSlot(
        SeasonSlotDefinition definition,
        int completed,
        IEnumerable<ProgressItem> orderedEvidence,
        bool countBasedEvidence)
    {
        IList<ProgressItem> evidence = orderedEvidence.ToList();
        bool isComplete = completed >= definition.Required;
        ProgressItem? evidenceItem = isComplete
            ? _getThresholdEvidence(definition.Required, evidence, countBasedEvidence)
            : null;
        Reward? evidenceReward = evidenceItem is { ItemLevel: not null, Rarity: not null }
            ? new Reward { ItemLevel = evidenceItem.ItemLevel, Rarity = evidenceItem.Rarity }
            : null;
        Reward reward = evidenceReward ?? new Reward
            {
                ItemLevel = isComplete ? definition.FallbackReward.ItemLevel : null,
                Rarity = isComplete ? definition.FallbackReward.Rarity : null
            };
        return new VaultSlot
        {
            Id = definition.Id,
            Requirement = new SlotRequirement
            {
                Unit = definition.Unit,
                Required = definition.Required,
                Label = definition.Label
            },
            Progress = new SlotProgress
            {
                Completed = completed,
                State = isComplete ? "complete" : "incomplete"
            },
            Reward = reward,
            Items = evidence.Take(definition.DisplayItemCount).ToList()
        };
    }

    private static ProgressItem? _getThresholdEvidence(
        int required,
        IList<ProgressItem> orderedEvidence,
        bool countBasedEvidence)
    {
        if (!countBasedEvidence)
            return required <= orderedEvidence.Count ? orderedEvidence[required - 1] : null;

        int completed = 0;
        foreach (ProgressItem item in orderedEvidence)
        {
            completed += item.Progress?.Completed ?? 0;
            if (completed >= required)
                return item;
        }

        return null;
    }

    private static VaultSection _createUnsupportedSection(SeasonActivityDefinition activity)
    {
        return new VaultSection
        {
            Id = activity.Id,
            Title = activity.Title,
            Subtitle = activity.Subtitle,
            Kind = activity.Kind,
            Status = "unsupported",
            Freshness = "fresh",
            Slots = [],
            AdditionalItems = []
        };
    }

    private static VaultSection _createUnavailableSection(SeasonActivityDefinition activity, string message)
    {
        return new VaultSection
        {
            Id = activity.Id,
            Title = activity.Title,
            Subtitle = message,
            Kind = activity.Kind,
            Status = "unavailable",
            Freshness = "stale",
            Slots = [],
            AdditionalItems = []
        };
    }

    private static IEnumerable<BlizzardMode> _getModes(
        BlizzardEncounterResponse? response,
        long instanceId)
    {
        return response?.Expansions
                   .SelectMany(x => x.Instances)
                   .Where(x => x.Instance.Id == instanceId)
                   .SelectMany(x => x.Modes)
               ?? [];
    }

    private static ProgressDimension _getDifficultyProgress(
        BlizzardMode mode,
        long encounterId,
        int index,
        DateTimeOffset resetAt)
    {
        BlizzardEncounter? encounter = mode.Progress.Encounters
            .FirstOrDefault(x => x.Encounter.Id == encounterId);
        bool completed = encounter != null && encounter.LastKillTimestamp > resetAt.ToUnixTimeMilliseconds();
        string id = string.IsNullOrWhiteSpace(mode.Difficulty.Type)
            ? $"difficulty-{index + 1}"
            : mode.Difficulty.Type.ToLowerInvariant();

        return new ProgressDimension
        {
            Id = id,
            Label = string.IsNullOrWhiteSpace(mode.Difficulty.Name) ? id : mode.Difficulty.Name,
            State = completed ? "complete" : "incomplete",
            Completed = completed
        };
    }

    private static string _trimClassName(string className)
    {
        return className.Replace(" ", "").ToLowerInvariant();
    }
}
