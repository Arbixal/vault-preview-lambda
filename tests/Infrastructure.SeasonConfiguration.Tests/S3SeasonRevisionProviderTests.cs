using VaultShared.Seasons;
using VaultPreview.SeasonConfigurationInfrastructure;
using Xunit;
using SeasonConfigurationModel = VaultShared.Seasons.SeasonConfiguration;

namespace Infrastructure.SeasonConfiguration.Tests;

public class S3SeasonRevisionProviderTests
{
    [Fact]
    public void IsValid_AcceptsRevisionWithMatchingValidatedHash()
    {
        SeasonConfigurationModel configuration = _createConfiguration();
        SeasonRevision revision = SeasonRevision.Create("future-r1", configuration);
        SeasonRevisionDocument document = new()
        {
            Id = revision.Id,
            Configuration = revision.Configuration,
            RevisionHash = revision.RevisionHash
        };

        Assert.True(S3SeasonRevisionProvider.IsValid(document));
    }

    [Fact]
    public void IsValid_RejectsHashMismatchAndInvalidConfiguration()
    {
        SeasonRevision revision = SeasonRevision.Create("future-r1", _createConfiguration());
        SeasonRevisionDocument hashMismatch = new()
        {
            Id = revision.Id,
            Configuration = revision.Configuration,
            RevisionHash = "sha256:0000000000000000000000000000000000000000000000000000000000000000"
        };
        SeasonRevisionDocument invalidConfiguration = new()
        {
            Id = revision.Id,
            Configuration = revision.Configuration with { DisplayName = string.Empty },
            RevisionHash = revision.RevisionHash
        };

        Assert.False(S3SeasonRevisionProvider.IsValid(hashMismatch));
        Assert.False(S3SeasonRevisionProvider.IsValid(invalidConfiguration));
    }

    [Fact]
    public void GetRevisionKey_UsesTheVersionedSeasonConfigurationPrefix()
    {
        Assert.Equal(
            "season-config/v1/revisions/future-season/future-r1.json",
            S3SeasonRevisionProvider.GetRevisionKey("future-season", "future-r1"));
    }

    [Fact]
    public void SelectActivePointer_DoesNotPromotePendingFutureActivation()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ActiveSeasonPointer pending = new()
        {
            SeasonId = "future-season",
            Revision = "future-r1",
            RevisionHash = "sha256:0000000000000000000000000000000000000000000000000000000000000000",
            ActivationAt = now.AddHours(1)
        };

        Assert.Null(S3SeasonRevisionProvider.SelectActivePointer(pending, now));
        Assert.Same(pending, S3SeasonRevisionProvider.SelectActivePointer(pending, now.AddHours(1)));
    }

    private static SeasonConfigurationModel _createConfiguration()
    {
        return new SeasonConfigurationModel(
            "future-season",
            "Future Season",
            "Future",
            "Future Expansion",
            null,
            [
                new SeasonActivityDefinition(
                    "future-activity",
                    "future-kind",
                    "Future Activity",
                    null,
                    0,
                    [
                        new SeasonSlotDefinition(
                            "future-slot",
                            "activities",
                            3,
                            "3 activities",
                            1,
                            new SeasonRewardDefinition(500, "epic"))
                    ],
                    [])
            ]);
    }
}
