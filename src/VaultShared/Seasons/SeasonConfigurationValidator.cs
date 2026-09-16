namespace VaultShared.Seasons;

public static class SeasonConfigurationValidator
{
    private static readonly ISet<string> _supportedRarities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "poor",
        "common",
        "uncommon",
        "rare",
        "epic",
        "legendary"
    };

    public static void ValidateOrThrow(SeasonConfiguration configuration)
    {
        IReadOnlyList<string> errors = Validate(configuration);
        if (errors.Count > 0)
            throw new SeasonConfigurationValidationException(errors);
    }

    public static IReadOnlyList<string> Validate(SeasonConfiguration? configuration)
    {
        if (configuration == null)
            return ["Configuration is required."];

        List<string> errors = [];
        _requireText(configuration.Id, "Season ID", errors);
        _requireText(configuration.DisplayName, "Season display name", errors);
        _requireText(configuration.ShortLabel, "Season short label", errors);
        _requireText(configuration.Expansion, "Season expansion", errors);

        if (configuration.Activities == null || configuration.Activities.Count == 0)
        {
            errors.Add("At least one activity is required.");
            return errors;
        }

        _validateUnique(
            configuration.Activities,
            x => x.Id,
            "activity ID",
            errors);
        _validateUnique(
            configuration.Activities,
            x => x.Order.ToString(),
            "activity order",
            errors);
        _validateUnique(
            configuration.Activities.SelectMany(activity => activity.SourceIds ?? []),
            x => x,
            "source ID across activities",
            errors);

        foreach (SeasonActivityDefinition activity in configuration.Activities)
        {
            string activityPrefix = $"Activity '{activity.Id}'";
            _requireText(activity.Id, $"{activityPrefix} ID", errors);
            _requireText(activity.Kind, $"{activityPrefix} kind", errors);
            _requireText(activity.Title, $"{activityPrefix} title", errors);

            if (activity.Order < 0)
                errors.Add($"{activityPrefix} order must be non-negative.");

            if (activity.SourceIds == null)
            {
                errors.Add($"{activityPrefix} source IDs must not be null.");
            }
            else
            {
                _validateUnique(activity.SourceIds, x => x, $"source ID in activity '{activity.Id}'", errors);
                foreach (string sourceId in activity.SourceIds)
                    _requireText(sourceId, $"{activityPrefix} source ID", errors);
            }

            if (activity.Slots == null || activity.Slots.Count == 0)
            {
                errors.Add($"{activityPrefix} must define at least one slot.");
                continue;
            }

            _validateUnique(activity.Slots, x => x.Id, $"slot ID in activity '{activity.Id}'", errors);
            _validateUnique(
                activity.Slots,
                x => x.Required.ToString(),
                $"slot requirement in activity '{activity.Id}'",
                errors);

            foreach (SeasonSlotDefinition slot in activity.Slots)
            {
                string slotPrefix = $"Slot '{activity.Id}/{slot.Id}'";
                _requireText(slot.Id, $"{slotPrefix} ID", errors);
                _requireText(slot.Unit, $"{slotPrefix} unit", errors);
                _requireText(slot.Label, $"{slotPrefix} label", errors);

                if (slot.Required <= 0)
                    errors.Add($"{slotPrefix} required value must be greater than zero.");

                if (slot.DisplayItemCount < 0)
                    errors.Add($"{slotPrefix} display item count must be non-negative.");

                if (slot.Reward == null)
                {
                    errors.Add($"{slotPrefix} reward mapping is required.");
                }
                else
                {
                    if (slot.Reward.ItemLevel <= 0)
                        errors.Add($"{slotPrefix} reward item level must be greater than zero.");

                    _requireText(slot.Reward.Rarity, $"{slotPrefix} reward rarity", errors);
                    if (!_supportedRarities.Contains(slot.Reward.Rarity))
                    {
                        errors.Add(
                            $"{slotPrefix} reward rarity '{slot.Reward.Rarity}' is not supported.");
                    }
                    else if (slot.Reward.Rarity != slot.Reward.Rarity.ToLowerInvariant())
                    {
                        errors.Add($"{slotPrefix} reward rarity must use lowercase canonical casing.");
                    }
                }
            }
        }

        return errors;
    }

    private static void _requireText(string? value, string field, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"{field} is required.");
    }

    private static void _validateUnique<T>(
        IEnumerable<T> values,
        Func<T, string> keySelector,
        string field,
        ICollection<string> errors)
    {
        IEnumerable<string> duplicates = values
            .GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        foreach (string duplicate in duplicates)
            errors.Add($"Duplicate {field} '{duplicate}'.");
    }
}

public sealed class SeasonConfigurationValidationException(IReadOnlyList<string> errors)
    : InvalidOperationException($"Season configuration is invalid: {string.Join(" ", errors)}")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
