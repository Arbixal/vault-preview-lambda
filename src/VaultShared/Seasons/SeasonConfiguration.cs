using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VaultShared.Seasons;

public sealed record SeasonConfiguration(
    string Id,
    string DisplayName,
    string ShortLabel,
    string Expansion,
    int? SourceSeasonId,
    IReadOnlyList<SeasonActivityDefinition> Activities);

public sealed record SeasonActivityDefinition(
    string Id,
    string Kind,
    string Title,
    string? Subtitle,
    int Order,
    IReadOnlyList<SeasonSlotDefinition> Slots,
    IReadOnlyList<string> SourceIds);

public sealed record SeasonSlotDefinition(
    string Id,
    string Unit,
    int Required,
    string Label,
    int DisplayItemCount,
    SeasonRewardDefinition Reward);

public sealed record SeasonRewardDefinition(int ItemLevel, string Rarity);

public enum SeasonRevisionStatus
{
    Draft,
    Scheduled,
    Active,
    Retired
}

public sealed record SeasonRevision(
    string Id,
    SeasonConfiguration Configuration,
    SeasonRevisionStatus Status,
    DateTimeOffset? ActivationAt,
    string RevisionHash)
{
    public static SeasonRevision Create(string id, SeasonConfiguration configuration)
    {
        SeasonConfiguration snapshot = SeasonConfigurationSnapshot.Clone(configuration);
        return new(
            id,
            snapshot,
            SeasonRevisionStatus.Draft,
            null,
            SeasonRevisionHasher.Compute(snapshot));
    }
}

public static class SeasonConfigurationSnapshot
{
    public static SeasonConfiguration Clone(SeasonConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration with
        {
            Activities = new ReadOnlyCollection<SeasonActivityDefinition>(
                configuration.Activities
                    .Select(activity => activity with
                    {
                        Slots = new ReadOnlyCollection<SeasonSlotDefinition>(activity.Slots.ToList()),
                        SourceIds = new ReadOnlyCollection<string>(activity.SourceIds.ToList())
                    })
                    .ToList())
        };
    }
}

public static class SeasonRevisionHasher
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string Compute(SeasonConfiguration configuration)
    {
        string canonicalJson = JsonSerializer.Serialize(configuration, _jsonOptions);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
