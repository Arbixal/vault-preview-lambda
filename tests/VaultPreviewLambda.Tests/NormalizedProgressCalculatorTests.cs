using VaultPreview.Blizzard;
using VaultPreview.Blizzard.Models;
using VaultPreview.RaiderIo.Models;
using VaultPreviewLambda.Calculations;
using VaultPreviewLambda.Models;
using VaultShared.Seasons;
using Xunit;

namespace CharacterDataLambda.Tests;

public class NormalizedProgressCalculatorTests
{
    [Fact]
    public async Task Calculate_ProducesSectionsSlotsAndApiSelectedProgress()
    {
        SeasonRevision revision = SeasonRevision.Create(
            "midnight-s2-r1",
            new SeasonConfiguration(
                "midnight-s2",
                "Midnight Season 2",
                "Season 2",
                "Midnight",
                18,
                [
                    new SeasonActivityDefinition(
                        "raid",
                        "raid",
                        "Raids",
                        "Vault slots",
                        0,
                        [new SeasonSlotDefinition("raid-slot-1", "bosses", 1, "1 boss", 1, new(318, "epic"))],
                        ["wow:journal-instance:1320"]),
                    new SeasonActivityDefinition(
                        "mythic-plus",
                        "mythic-plus",
                        "Mythic+",
                        "Weekly runs",
                        1,
                        [new SeasonSlotDefinition("mythic-plus-slot-1", "runs", 1, "1 run", 1, new(318, "epic"))],
                        []),
                    new SeasonActivityDefinition(
                        "delves",
                        "delves",
                        "Delves",
                        "Weekly completions",
                        2,
                        [new SeasonSlotDefinition("delves-slot-2", "delves", 2, "2 Delves", 1, new(318, "epic"))],
                        []),
                    new SeasonActivityDefinition(
                        "future",
                        "future-activity",
                        "Future Activity",
                        null,
                        3,
                        [],
                        [])
                ]));
        DateTimeOffset resetAt = DateTimeOffset.UtcNow.AddHours(-1);
        BlizzardEncounterResponse encounters = _createEncounterResponse(resetAt);
        IReadOnlyList<BlizzardJournalMetadata> journal =
        [
            new BlizzardJournalMetadata(
                new BlizzardJournalInstance
                {
                    Id = 1320,
                    Name = "The Venomous Abyss",
                    Encounters =
                    [
                        new BlizzardJournalEncounter { Id = 2888, Name = "Nek'zali the Soulcoiler" },
                        new BlizzardJournalEncounter { Id = 2874, Name = "Entombed Sentinels" }
                    ]
                },
                false,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddDays(1),
                DateTimeOffset.UtcNow.AddDays(7))
        ];
        RaiderIoProfileResponse raiderIo = new()
        {
            Class = "Frost Mage",
            WeeklyHighestLevelRuns =
            [
                new RaiderIoDungeonRun { Dungeon = "Lower Run", MythicLevel = 8, ClearTimeMs = 900 },
                new RaiderIoDungeonRun { Dungeon = "Higher Run", MythicLevel = 10, ClearTimeMs = 1200 }
            ]
        };
        FakeDelveBaselineProvider baseline = new(new DelveBaseline(
            revision.Configuration.Id,
            revision.Id,
            revision.RevisionHash,
            new Dictionary<int, int> { [8] = 1 }));

        VaultProgressResponse response = await new VaultProgressCalculator().Calculate(
            "us",
            "nagrand",
            "bixposter",
            revision,
            resetAt,
            DateTimeOffset.UtcNow,
            encounters,
            journal,
            raiderIo,
            new Dictionary<int, int> { [8] = 3 },
            baseline);

        Assert.Equal("frostmage", response.Character.Class);
        Assert.Equal(4, response.Sections.Count);
        Assert.Equal("complete", response.Sections[0].Slots[0].Progress.State);
        Assert.Equal(318, response.Sections[0].Slots[0].Reward.ItemLevel);
        Assert.Single(response.Sections[0].AdditionalItems);
        Assert.Equal("Higher Run +10", response.Sections[1].Slots[0].Items[0].Label);
        Assert.Equal(2, response.Sections[2].Slots[0].Progress.Completed);
        Assert.Equal("unsupported", response.Sections[3].Status);
    }

    [Fact]
    public async Task Calculate_UsesFreshBaselineIdentityAndMarksUnavailableInputs()
    {
        SeasonRevision revision = SeasonRevision.Create(
            "midnight-s2-r1",
            new SeasonConfiguration(
                "midnight-s2",
                "Midnight Season 2",
                "Season 2",
                "Midnight",
                18,
                [new SeasonActivityDefinition(
                    "delves",
                    "delves",
                    "Delves",
                    null,
                    0,
                    [new SeasonSlotDefinition("delves-slot-2", "delves", 2, "2 Delves", 1, new(318, "epic"))],
                    [])]));
        FakeDelveBaselineProvider baseline = new(new DelveBaseline(
            "different-season",
            "different-revision",
            "sha256:different",
            new Dictionary<int, int> { [8] = 99 }));

        VaultProgressResponse response = await new VaultProgressCalculator().Calculate(
            "us",
            "nagrand",
            "bixposter",
            revision,
            DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow,
            null,
            [],
            null,
            new Dictionary<int, int> { [8] = 3 },
            baseline);

        Assert.Equal("available", response.Sections[0].Status);
        Assert.Equal(0, response.Sections[0].Slots[0].Progress.Completed);
    }

    private static BlizzardEncounterResponse _createEncounterResponse(DateTimeOffset resetAt)
    {
        return new BlizzardEncounterResponse
        {
            Expansions =
            [
                new BlizzardExpansion
                {
                    Expansion = new BlizzardBase { Name = "Current Season" },
                    Instances =
                    [
                        new BlizzardInstance
                        {
                            Instance = new BlizzardBase { Id = 1320, Name = "The Venomous Abyss" },
                            Modes =
                            [
                                new BlizzardMode
                                {
                                    Difficulty = new BlizzardType { Type = "heroic", Name = "Heroic" },
                                    Progress = new BlizzardProgress
                                    {
                                        Encounters =
                                        [
                                            new BlizzardEncounter
                                            {
                                                Encounter = new BlizzardBase { Id = 2888 },
                                                LastKillTimestamp = resetAt.ToUnixTimeMilliseconds() + 1000
                                            }
                                        ]
                                    }
                                }
                            ]
                        }
                    ]
                }
            ]
        };
    }

    private sealed class FakeDelveBaselineProvider(DelveBaseline? baseline)
        : ISeasonAwareDelveBaselineProvider
    {
        public Task<DelveBaseline?> GetBaseline(string region, string realm, string character) =>
            Task.FromResult(baseline);

        public Task SaveBaseline(string region, string realm, string character, DelveBaseline value) =>
            Task.CompletedTask;
    }
}
