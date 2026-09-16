using VaultShared.Seasons;
using Xunit;

namespace VaultShared.Tests;

public class SeasonConfigurationTests
{
    [Fact]
    public void SaveDraft_RejectsInvalidConfiguration()
    {
        InMemorySeasonConfigurationStore store = new();
        SeasonConfiguration configuration = _createConfiguration() with
        {
            Activities =
            [
                _createActivity("raid", "raid", 0) with
                {
                    Slots =
                    [
                        new SeasonSlotDefinition(
                            "raid-slot-2",
                            "bosses",
                            2,
                            "2 bosses",
                            2,
                            new SeasonRewardDefinition(318, "mythic-plus-only-rarity")),
                        new SeasonSlotDefinition(
                            "raid-slot-2-duplicate",
                            "bosses",
                            2,
                            "2 bosses again",
                            2,
                            new SeasonRewardDefinition(318, "epic"))
                    ]
                }
            ]
        };

        SeasonConfigurationValidationException exception = Assert.Throws<SeasonConfigurationValidationException>(
            () => store.SaveDraft(SeasonRevision.Create("midnight-s2-r1", configuration)));

        Assert.Contains(exception.Errors, error => error.Contains("Duplicate slot requirement"));
        Assert.Contains(exception.Errors, error => error.Contains("not supported"));
    }

    [Fact]
    public void Validate_RejectsMissingMetadataDuplicateOrderingAndCrossActivitySources()
    {
        SeasonConfiguration configuration = _createConfiguration() with
        {
            Id = string.Empty,
            DisplayName = string.Empty,
            Activities =
            [
                _createActivity("duplicate", "raid", 0) with
                {
                    SourceIds = ["shared-source"],
                    Slots =
                    [
                        new SeasonSlotDefinition(
                            "invalid-slot",
                            "bosses",
                            -1,
                            "",
                            -1,
                            null!)
                    ]
                },
                _createActivity("duplicate", "mythic-plus", 0) with
                {
                    SourceIds = ["shared-source"]
                }
            ]
        };

        IReadOnlyList<string> errors = SeasonConfigurationValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("Season ID"));
        Assert.Contains(errors, error => error.Contains("Season display name"));
        Assert.Contains(errors, error => error.Contains("Duplicate activity ID"));
        Assert.Contains(errors, error => error.Contains("Duplicate activity order"));
        Assert.Contains(errors, error => error.Contains("source ID across activities"));
        Assert.Contains(errors, error => error.Contains("required value must be greater"));
        Assert.Contains(errors, error => error.Contains("display item count must be non-negative"));
        Assert.Contains(errors, error => error.Contains("reward mapping is required"));
    }

    [Fact]
    public void SaveDraft_RejectsHashMismatchAndNonCanonicalRarity()
    {
        SeasonConfiguration configuration = _createConfiguration() with
        {
            Activities =
            [
                _createActivity("raid", "raid", 0) with
                {
                    Slots =
                    [
                        new SeasonSlotDefinition(
                            "raid-slot-2",
                            "bosses",
                            2,
                            "2 bosses",
                            2,
                            new SeasonRewardDefinition(318, "EPIC"))
                    ]
                }
            ]
        };

        IReadOnlyList<string> errors = SeasonConfigurationValidator.Validate(configuration);
        Assert.Contains(errors, error => error.Contains("lowercase canonical casing"));

        InMemorySeasonConfigurationStore store = new();
        SeasonRevision revision = SeasonRevision.Create("midnight-s2-r1", _createConfiguration()) with
        {
            RevisionHash = "sha256:0000000000000000000000000000000000000000000000000000000000000000"
        };

        SeasonConfigurationValidationException exception = Assert.Throws<SeasonConfigurationValidationException>(
            () => store.SaveDraft(revision));

        Assert.Contains(exception.Errors, error => error.Contains("content hash"));
    }

    [Fact]
    public void ActivateAndRollback_RestoresPreviousRevision()
    {
        InMemorySeasonConfigurationStore store = new();
        SeasonRevision first = SeasonRevision.Create("midnight-s2-r1", _createConfiguration());
        SeasonRevision second = SeasonRevision.Create(
            "midnight-s2-r2",
            _createConfiguration() with { ShortLabel = "Season 2 Revised" });

        store.SaveDraft(first);
        store.SaveDraft(second);
        store.Activate(first.Id, DateTimeOffset.UnixEpoch.AddDays(1));
        store.Activate(second.Id, DateTimeOffset.UnixEpoch.AddDays(2));

        Assert.Equal(second.Id, store.GetActive(DateTimeOffset.UnixEpoch.AddDays(2))?.Id);

        store.Rollback(first.Id, DateTimeOffset.UnixEpoch.AddDays(3));

        Assert.Equal(first.Id, store.GetActive(DateTimeOffset.UnixEpoch.AddDays(3))?.Id);
        Assert.Equal(SeasonRevisionStatus.Retired, store.GetRevision(second.Id)?.Status);
    }

    [Fact]
    public void ScheduledRevision_BecomesActiveAtItsEffectiveTime()
    {
        InMemorySeasonConfigurationStore store = new();
        SeasonRevision revision = SeasonRevision.Create("midnight-s2-r1", _createConfiguration());
        DateTimeOffset activationAt = DateTimeOffset.UtcNow.AddMinutes(10);

        store.SaveDraft(revision);
        store.Schedule(revision.Id, activationAt);

        Assert.Null(store.GetActive(activationAt.AddMinutes(-1)));
        Assert.Equal(revision.Id, store.GetActive(activationAt)?.Id);
        Assert.Equal(SeasonRevisionStatus.Active, store.GetRevision(revision.Id)?.Status);
    }

    [Fact]
    public void ScheduledRevisions_ActivateInEffectiveOrderWithoutRegression()
    {
        InMemorySeasonConfigurationStore store = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SeasonRevision first = SeasonRevision.Create("midnight-s2-r1", _createConfiguration());
        SeasonRevision second = SeasonRevision.Create(
            "midnight-s2-r2",
            _createConfiguration() with { ShortLabel = "Season 2 Revised" });
        DateTimeOffset firstActivation = now.AddMinutes(1);
        DateTimeOffset secondActivation = now.AddMinutes(2);

        store.SaveDraft(first);
        store.SaveDraft(second);
        store.Schedule(first.Id, firstActivation);
        store.Schedule(second.Id, secondActivation);

        Assert.Equal(first.Id, store.GetActive(firstActivation.AddSeconds(1))?.Id);
        Assert.Equal(second.Id, store.GetActive(secondActivation.AddSeconds(1))?.Id);
        Assert.Equal(second.Id, store.GetActive(secondActivation.AddMinutes(1))?.Id);
        Assert.Equal(secondActivation, store.GetRevision(second.Id)?.ActivationAt);
        Assert.Equal(SeasonRevisionStatus.Retired, store.GetRevision(first.Id)?.Status);
    }

    [Fact]
    public void SaveDraft_TakesAnImmutableConfigurationSnapshot()
    {
        List<SeasonSlotDefinition> slots =
        [
            new SeasonSlotDefinition(
                "raid-slot-2",
                "bosses",
                2,
                "2 bosses",
                2,
                new SeasonRewardDefinition(318, "epic"))
        ];
        List<SeasonActivityDefinition> activities =
        [
            new SeasonActivityDefinition(
                "raid",
                "raid",
                "Raids",
                "Vault slots",
                0,
                slots,
                ["wow:journal-instance:1320"])
        ];
        SeasonConfiguration configuration = new(
            "midnight-s2",
            "Midnight Season 2",
            "Season 2",
            "Midnight",
            18,
            activities);
        SeasonRevision revision = SeasonRevision.Create("midnight-s2-r1", configuration);
        InMemorySeasonConfigurationStore store = new();

        store.SaveDraft(revision);
        slots.Clear();
        activities.Clear();

        SeasonRevision stored = Assert.IsType<SeasonRevision>(store.GetRevision(revision.Id));
        Assert.Single(stored.Configuration.Activities);
        Assert.Single(stored.Configuration.Activities[0].Slots);
        Assert.Equal(revision.RevisionHash, stored.RevisionHash);
    }

    [Fact]
    public void RevisionHash_IsDeterministicAndSha256Encoded()
    {
        string firstHash = SeasonRevisionHasher.Compute(_createConfiguration());
        string secondHash = SeasonRevisionHasher.Compute(_createConfiguration());

        Assert.Equal(firstHash, secondHash);
        Assert.Matches("^sha256:[0-9a-f]{64}$", firstHash);
    }

    private static SeasonConfiguration _createConfiguration()
    {
        return new SeasonConfiguration(
            "midnight-s2",
            "Midnight Season 2",
            "Season 2",
            "Midnight",
            18,
            [_createActivity("raid", "raid", 0)]);
    }

    private static SeasonActivityDefinition _createActivity(string id, string kind, int order)
    {
        return new SeasonActivityDefinition(
            id,
            kind,
            "Raids",
            "Vault slots",
            order,
            [
                new SeasonSlotDefinition(
                    "raid-slot-2",
                    "bosses",
                    2,
                    "2 bosses",
                    2,
                    new SeasonRewardDefinition(318, "epic"))
            ],
            ["wow:journal-instance:1320"]);
    }
}
