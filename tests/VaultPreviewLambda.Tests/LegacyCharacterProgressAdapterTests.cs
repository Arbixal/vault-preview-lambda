using System.Text.Json;
using VaultPreviewLambda.Legacy;
using VaultPreviewLambda.Models;
using Xunit;

namespace VaultPreviewLambda.Tests;

public class LegacyCharacterProgressAdapterTests
{
    [Fact]
    public void Adapt_MatchesFullLegacyGoldenFixture()
    {
        string fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "legacy-flat-response-full.json");
        string fixtureJson = File.ReadAllText(fixturePath);
        IDictionary<string, CharacterProgress> expected =
            JsonSerializer.Deserialize<Dictionary<string, CharacterProgress>>(fixtureJson)!;
        CharacterProgress expectedCharacter = expected["bixposter-nagrand"];
        IReadOnlyDictionary<string, long> encounterIdBySlug = new Dictionary<string, long>
        {
            ["nek'zali-the-soulcoiler"] = 2888,
            ["entombed-sentinels"] = 2874,
            ["vashnik-the-malignant"] = 2882,
            ["the-lost-explorers"] = 2894,
            ["sszorak"] = 2871,
            ["the-twin-fangs"] = 2887,
            ["the-coiled-altar"] = 2883,
            ["ula'tek"] = 2895,
            ["nymrissa-wavecaller"] = 2849
        };

        List<ProgressItem> raidItems = expectedCharacter.Raid
            .Select(boss => new ProgressItem
            {
                Id = $"wow:journal-encounter:{encounterIdBySlug[boss.Key]}",
                Label = boss.Key,
                Progress = new ProgressData
                {
                    Dimensions = boss.Value.Select(difficulty => new ProgressDimension
                    {
                        Id = difficulty.Key,
                        State = difficulty.Value ? "complete" : "incomplete",
                        Completed = difficulty.Value
                    }).ToList()
                }
            })
            .ToList();
        List<ProgressItem> dungeonItems = expectedCharacter.Dungeons
            .Select((run, index) => new ProgressItem
            {
                Id = $"raiderio:run:{index}",
                Label = $"{run.Name} +{run.Level}",
                Progress = new ProgressData { Value = run.Level },
                Tooltip = new Tooltip { Title = run.Name }
            })
            .ToList();
        List<ProgressItem> delveItems = expectedCharacter.Delves
            .Where(x => x.Value > 0)
            .Select(x => new ProgressItem
            {
                Id = $"wow:delve-level:{x.Key}",
                Progress = new ProgressData { Value = x.Key, Completed = x.Value }
            })
            .ToList();

        VaultProgressResponse normalized = new()
        {
            Character = new CharacterIdentity
            {
                Region = "us",
                Realm = "nagrand",
                Name = "bixposter",
                Class = expectedCharacter.PlayerClass
            },
            Season = new SeasonSnapshot { SourceSeasonId = expectedCharacter.Season },
            Sections =
            [
                new VaultSection
                {
                    Kind = "raid",
                    Slots = [new VaultSlot { Items = raidItems }]
                },
                new VaultSection
                {
                    Kind = "mythic-plus",
                    Slots = [new VaultSlot { Items = dungeonItems }]
                },
                new VaultSection
                {
                    Kind = "delves",
                    Slots = [new VaultSlot { Items = delveItems }]
                }
            ]
        };

        IDictionary<string, CharacterProgress> actual = LegacyCharacterProgressAdapter.Adapt(normalized);

        CharacterProgress actualCharacter = actual["bixposter-nagrand"];
        _assertLegacyCharacterEqual(expectedCharacter, actualCharacter);
    }

    [Fact]
    public void Adapt_PreservesLegacyKeyAndMapsNormalizedProgress()
    {
        VaultProgressResponse response = new()
        {
            Character = new CharacterIdentity
            {
                Region = "us",
                Realm = "nagrand",
                Name = "bixposter",
                Class = "shaman"
            },
            Season = new SeasonSnapshot { SourceSeasonId = 18 },
            Sections =
            [
                new VaultSection
                {
                    Id = "raid",
                    Kind = "raid",
                    Slots =
                    [
                        new VaultSlot
                        {
                            Items =
                            [
                                new ProgressItem
                                {
                                    Id = "wow:journal-encounter:2888",
                                    Progress = new ProgressData
                                    {
                                        Dimensions =
                                        [
                                            new ProgressDimension { Id = "heroic", State = "complete", Completed = true },
                                            new ProgressDimension { Id = "world", State = "complete", Completed = true }
                                        ]
                                    }
                                }
                            ]
                        }
                    ]
                },
                new VaultSection
                {
                    Id = "mythic-plus",
                    Kind = "mythic-plus",
                    Slots =
                    [
                        new VaultSlot
                        {
                            Items =
                            [
                                new ProgressItem
                                {
                                    Label = "The Dawnbreaker +8",
                                    Progress = new ProgressData { Value = 8 }
                                }
                            ]
                        }
                    ]
                },
                new VaultSection
                {
                    Id = "delves",
                    Kind = "delves",
                    Slots =
                    [
                        new VaultSlot
                        {
                            Items =
                            [
                                new ProgressItem
                                {
                                    Id = "wow:delve-level:8",
                                    Progress = new ProgressData { Value = 8, Completed = 2 }
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        IDictionary<string, CharacterProgress> legacy = LegacyCharacterProgressAdapter.Adapt(response);
        CharacterProgress result = legacy["bixposter-nagrand"];

        Assert.Equal("shaman", result.PlayerClass);
        Assert.Equal(18, result.Season);
        Assert.Equal(9, result.Raid.Count);
        Assert.True(result.Raid["nek'zali-the-soulcoiler"]["heroic"]);
        Assert.DoesNotContain("world", result.Raid["nek'zali-the-soulcoiler"].Keys);
        Assert.Equal(8, result.Dungeons.Single().Level);
        Assert.Equal("The Dawnbreaker", result.Dungeons.Single().Name);
        Assert.Equal(2, result.Delves[8]);
        Assert.Equal(11, result.Delves.Count);
    }

    [Fact]
    public void Adapt_UnknownSeasonKeepsLegacyRaidEmpty()
    {
        VaultProgressResponse response = new()
        {
            Character = new CharacterIdentity { Region = "us", Realm = "realm", Name = "character" },
            Season = new SeasonSnapshot { SourceSeasonId = 999 },
            Sections =
            [
                new VaultSection
                {
                    Kind = "raid",
                    Slots =
                    [
                        new VaultSlot
                        {
                            Items =
                            [new ProgressItem { Id = "wow:journal-encounter:2888" }]
                        }
                    ]
                }
            ]
        };

        CharacterProgress result = LegacyCharacterProgressAdapter
            .Adapt(response)["character-realm"];

        Assert.Empty(result.Raid);
    }

    private static void _assertLegacyCharacterEqual(
        CharacterProgress expected,
        CharacterProgress actual)
    {
        Assert.Equal(expected.PlayerClass, actual.PlayerClass);
        Assert.Equal(expected.Season, actual.Season);
        Assert.Equal(expected.Raid.Keys, actual.Raid.Keys);
        foreach ((string slug, BossProgress expectedBoss) in expected.Raid)
        {
            Assert.Equal(expectedBoss.Keys, actual.Raid[slug].Keys);
            foreach ((string difficulty, bool completed) in expectedBoss)
                Assert.Equal(completed, actual.Raid[slug][difficulty]);
        }

        Assert.Equal(expected.Dungeons.Count, actual.Dungeons.Count);
        for (int index = 0; index < expected.Dungeons.Count; ++index)
        {
            Assert.Equal(expected.Dungeons[index].Level, actual.Dungeons[index].Level);
            Assert.Equal(expected.Dungeons[index].Name, actual.Dungeons[index].Name);
        }

        Assert.Equal(expected.Delves.Keys, actual.Delves.Keys);
        foreach ((int level, int completed) in expected.Delves)
            Assert.Equal(completed, actual.Delves[level]);
    }
}
