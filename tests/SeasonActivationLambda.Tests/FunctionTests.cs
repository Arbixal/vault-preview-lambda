using SeasonActivationLambda.Request;
using SeasonActivationLambda.Response;
using VaultShared.Seasons;
using Xunit;

namespace SeasonActivationLambda.Tests;

public class FunctionTests
{
    [Fact]
    public async Task Activate_ValidatesHashAndPromotesRevision()
    {
        SeasonRevision revision = SeasonRevision.Create("future-season-r1", _createConfiguration());
        FakeSeasonRevisionStore store = new(revision);
        SeasonActivationLambda.Function function = new(store);

        SeasonActivationResponse response = await function.FunctionHandler(
            new SeasonActivationRequest
            {
                Operation = "activate",
                SeasonId = revision.Configuration.Id,
                RevisionId = revision.Id,
                RevisionHash = revision.RevisionHash
            },
            null!);

        Assert.Equal("activated", response.Status);
        Assert.Equal(revision.Id, response.RevisionId);
        Assert.Equal(revision.RevisionHash, response.RevisionHash);
        Assert.Equal(revision.Id, store.ActivatedRevisionId);
        Assert.Null(store.RolledBackRevisionId);
    }

    [Fact]
    public async Task Rollback_UsesTheRollbackStoreOperation()
    {
        SeasonRevision revision = SeasonRevision.Create("future-season-r1", _createConfiguration());
        FakeSeasonRevisionStore store = new(revision);
        SeasonActivationLambda.Function function = new(store);

        await function.FunctionHandler(
            new SeasonActivationRequest
            {
                Operation = "rollback",
                SeasonId = revision.Configuration.Id,
                RevisionId = revision.Id,
                RevisionHash = revision.RevisionHash
            },
            null!);

        Assert.Equal(revision.Id, store.RolledBackRevisionId);
        Assert.Null(store.ActivatedRevisionId);
    }

    [Fact]
    public async Task Activate_RejectsAHashMismatch()
    {
        SeasonRevision revision = SeasonRevision.Create("future-season-r1", _createConfiguration());
        SeasonActivationLambda.Function function = new(new FakeSeasonRevisionStore(revision));

        await Assert.ThrowsAsync<InvalidDataException>(() => function.FunctionHandler(
            new SeasonActivationRequest
            {
                Operation = "activate",
                SeasonId = revision.Configuration.Id,
                RevisionId = revision.Id,
                RevisionHash = "sha256:0000000000000000000000000000000000000000000000000000000000000000"
            },
            null!));
    }

    private static SeasonConfiguration _createConfiguration()
    {
        return new SeasonConfiguration(
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

    private sealed class FakeSeasonRevisionStore(SeasonRevision revision) : ISeasonRevisionStore
    {
        public string? ActivatedRevisionId { get; private set; }
        public string? RolledBackRevisionId { get; private set; }

        public Task<SeasonRevision?> GetActiveRevision(CancellationToken cancellationToken = default) =>
            Task.FromResult<SeasonRevision?>(revision);

        public Task<SeasonRevision?> GetRevision(
            string seasonId,
            string revisionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SeasonRevision?>(
                revision.Configuration.Id == seasonId && revision.Id == revisionId ? revision : null);

        public Task SaveRevision(SeasonRevision value, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Activate(
            string seasonId,
            string revisionId,
            DateTimeOffset? activatedAt = null,
            CancellationToken cancellationToken = default)
        {
            ActivatedRevisionId = revisionId;
            return Task.CompletedTask;
        }

        public Task Schedule(
            string seasonId,
            string revisionId,
            DateTimeOffset activationAt,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<SeasonSchedule?> GetScheduled(CancellationToken cancellationToken = default) =>
            Task.FromResult<SeasonSchedule?>(null);

        public Task CancelSchedule(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Rollback(
            string seasonId,
            string revisionId,
            DateTimeOffset? activatedAt = null,
            CancellationToken cancellationToken = default)
        {
            RolledBackRevisionId = revisionId;
            return Task.CompletedTask;
        }
    }
}
