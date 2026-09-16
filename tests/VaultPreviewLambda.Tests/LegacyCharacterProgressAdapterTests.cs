using VaultPreviewLambda.Legacy;
using VaultPreviewLambda.Models;
using Xunit;

namespace VaultPreviewLambda.Tests;

public class LegacyCharacterProgressAdapterTests
{
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
                                            new ProgressDimension { Id = "heroic", State = "complete", Completed = true }
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
}
