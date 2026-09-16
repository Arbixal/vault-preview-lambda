using System.Text.Json.Serialization;

namespace VaultPreviewLambda.Models;

public sealed class VaultProgressResponse
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; init; } = 1;
    [JsonPropertyName("character")] public CharacterIdentity Character { get; init; } = new();
    [JsonPropertyName("season")] public SeasonSnapshot Season { get; init; } = new();
    [JsonPropertyName("progressPeriod")] public ProgressPeriod ProgressPeriod { get; init; } = new();
    [JsonPropertyName("sections")] public IList<VaultSection> Sections { get; init; } = [];
}

public sealed class CharacterIdentity
{
    [JsonPropertyName("region")] public string Region { get; init; } = string.Empty;
    [JsonPropertyName("realm")] public string Realm { get; init; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("class")] public string? Class { get; init; }
}

public sealed class SeasonSnapshot
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("displayName")] public string DisplayName { get; init; } = string.Empty;
    [JsonPropertyName("shortLabel")] public string ShortLabel { get; init; } = string.Empty;
    [JsonPropertyName("expansion")] public string Expansion { get; init; } = string.Empty;
    [JsonPropertyName("sourceSeasonId")] public int? SourceSeasonId { get; init; }
    [JsonPropertyName("revision")] public string Revision { get; init; } = string.Empty;
    [JsonPropertyName("revisionHash")] public string RevisionHash { get; init; } = string.Empty;
}

public sealed class ProgressPeriod
{
    [JsonPropertyName("resetAt")] public DateTimeOffset ResetAt { get; init; }
    [JsonPropertyName("asOf")] public DateTimeOffset AsOf { get; init; }
}

public sealed class VaultSection
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; init; } = string.Empty;
    [JsonPropertyName("subtitle")] public string? Subtitle { get; init; }
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
    [JsonPropertyName("freshness")] public string Freshness { get; init; } = string.Empty;
    [JsonPropertyName("slots")] public IList<VaultSlot> Slots { get; init; } = [];
    [JsonPropertyName("additionalItems")] public IList<ProgressItem> AdditionalItems { get; init; } = [];
}

public sealed class VaultSlot
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("requirement")] public SlotRequirement Requirement { get; init; } = new();
    [JsonPropertyName("progress")] public SlotProgress Progress { get; init; } = new();
    [JsonPropertyName("reward")] public Reward Reward { get; init; } = new();
    [JsonPropertyName("items")] public IList<ProgressItem> Items { get; init; } = [];
}

public sealed class SlotRequirement
{
    [JsonPropertyName("unit")] public string Unit { get; init; } = string.Empty;
    [JsonPropertyName("required")] public int Required { get; init; }
    [JsonPropertyName("label")] public string Label { get; init; } = string.Empty;
}

public sealed class SlotProgress
{
    [JsonPropertyName("completed")] public int? Completed { get; init; }
    [JsonPropertyName("state")] public string State { get; init; } = string.Empty;
}

public sealed class ProgressItem
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("label")] public string Label { get; init; } = string.Empty;
    [JsonPropertyName("state")] public string State { get; init; } = string.Empty;
    [JsonPropertyName("itemLevel")] public int? ItemLevel { get; init; }
    [JsonPropertyName("rarity")] public string? Rarity { get; init; }
    [JsonPropertyName("progress")] public ProgressData? Progress { get; init; }
    [JsonPropertyName("tooltip")] public Tooltip? Tooltip { get; init; }
}

public sealed class ProgressData
{
    [JsonPropertyName("value")] public object? Value { get; init; }
    [JsonPropertyName("completed")] public int? Completed { get; init; }
    [JsonPropertyName("required")] public int? Required { get; init; }
    [JsonPropertyName("dimensions")] public IList<ProgressDimension> Dimensions { get; init; } = [];
}

public sealed class ProgressDimension
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("label")] public string Label { get; init; } = string.Empty;
    [JsonPropertyName("state")] public string State { get; init; } = string.Empty;
    [JsonPropertyName("value")] public object? Value { get; init; }
    [JsonPropertyName("completed")] public bool? Completed { get; init; }
}

public sealed class Tooltip
{
    [JsonPropertyName("title")] public string Title { get; init; } = string.Empty;
    [JsonPropertyName("rows")] public IList<TooltipRow> Rows { get; init; } = [];
}

public sealed class TooltipRow
{
    [JsonPropertyName("label")] public string Label { get; init; } = string.Empty;
    [JsonPropertyName("value")] public string? Value { get; init; }
    [JsonPropertyName("state")] public string? State { get; init; }
    [JsonPropertyName("completed")] public bool? Completed { get; init; }
}

public sealed class Reward
{
    [JsonPropertyName("itemLevel")] public int? ItemLevel { get; init; }
    [JsonPropertyName("rarity")] public string? Rarity { get; init; }
}
