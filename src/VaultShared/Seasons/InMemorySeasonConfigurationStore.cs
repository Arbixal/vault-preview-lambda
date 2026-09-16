namespace VaultShared.Seasons;

public interface ISeasonConfigurationStore
{
    IReadOnlyList<SeasonRevision> GetRevisions();
    SeasonRevision? GetRevision(string revisionId);
    SeasonRevision? GetActive(DateTimeOffset now);
    void SaveDraft(SeasonRevision revision);
    void Schedule(string revisionId, DateTimeOffset activationAt);
    void Activate(string revisionId, DateTimeOffset? activatedAt = null);
    void Rollback(string revisionId, DateTimeOffset? activatedAt = null);
}

public sealed class InMemorySeasonConfigurationStore : ISeasonConfigurationStore
{
    private readonly object _lock = new();
    private readonly IDictionary<string, SeasonRevision> _revisions =
        new Dictionary<string, SeasonRevision>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<SeasonRevision> GetRevisions()
    {
        lock (_lock)
        {
            return _revisions.Values.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public SeasonRevision? GetRevision(string revisionId)
    {
        lock (_lock)
        {
            return _revisions.TryGetValue(revisionId, out SeasonRevision? revision)
                ? revision
                : null;
        }
    }

    public SeasonRevision? GetActive(DateTimeOffset now)
    {
        lock (_lock)
        {
            SeasonRevision? scheduled = _revisions.Values
                .Where(x => x.Status == SeasonRevisionStatus.Scheduled &&
                            x.ActivationAt.HasValue &&
                            x.ActivationAt.Value <= now)
                .OrderBy(x => x.ActivationAt)
                .LastOrDefault();

            if (scheduled != null)
                _activate(scheduled.Id, now);

            return _revisions.Values
                .Where(x => x.Status == SeasonRevisionStatus.Active)
                .OrderByDescending(x => x.ActivationAt)
                .FirstOrDefault();
        }
    }

    public void SaveDraft(SeasonRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        SeasonConfigurationValidator.ValidateOrThrow(revision.Configuration);

        if (string.IsNullOrWhiteSpace(revision.Id))
            throw new SeasonConfigurationValidationException(["Revision ID is required."]);

        string expectedHash = SeasonRevisionHasher.Compute(revision.Configuration);
        if (!string.Equals(expectedHash, revision.RevisionHash, StringComparison.Ordinal))
        {
            throw new SeasonConfigurationValidationException(
                [$"Revision '{revision.Id}' has a content hash that does not match its configuration."]);
        }

        if (revision.Status != SeasonRevisionStatus.Draft)
            throw new SeasonConfigurationValidationException(
                [$"Revision '{revision.Id}' must be saved as a draft."]);

        lock (_lock)
        {
            if (_revisions.ContainsKey(revision.Id))
                throw new InvalidOperationException($"Revision '{revision.Id}' already exists.");

            _revisions.Add(revision.Id, revision);
        }
    }

    public void Schedule(string revisionId, DateTimeOffset activationAt)
    {
        if (activationAt <= DateTimeOffset.UtcNow)
            throw new ArgumentOutOfRangeException(nameof(activationAt), "Scheduled activation must be in the future.");

        lock (_lock)
        {
            SeasonRevision revision = _getRevision(revisionId);
            if (revision.Status != SeasonRevisionStatus.Draft)
                throw new InvalidOperationException($"Only draft revision '{revisionId}' can be scheduled.");

            _revisions[revisionId] = revision with
            {
                Status = SeasonRevisionStatus.Scheduled,
                ActivationAt = activationAt
            };
        }
    }

    public void Activate(string revisionId, DateTimeOffset? activatedAt = null)
    {
        lock (_lock)
        {
            _activate(revisionId, activatedAt ?? DateTimeOffset.UtcNow);
        }
    }

    public void Rollback(string revisionId, DateTimeOffset? activatedAt = null)
    {
        lock (_lock)
        {
            SeasonRevision revision = _getRevision(revisionId);
            if (revision.Status != SeasonRevisionStatus.Retired &&
                revision.Status != SeasonRevisionStatus.Active)
            {
                throw new InvalidOperationException(
                    $"Only retired or active revision '{revisionId}' can be restored.");
            }

            _activate(revisionId, activatedAt ?? DateTimeOffset.UtcNow);
        }
    }

    private void _activate(string revisionId, DateTimeOffset activatedAt)
    {
        SeasonRevision revision = _getRevision(revisionId);

        foreach ((string id, SeasonRevision existing) in _revisions.ToList())
        {
            if (existing.Status == SeasonRevisionStatus.Active)
            {
                _revisions[id] = existing with { Status = SeasonRevisionStatus.Retired };
            }
        }

        _revisions[revisionId] = revision with
        {
            Status = SeasonRevisionStatus.Active,
            ActivationAt = activatedAt
        };
    }

    private SeasonRevision _getRevision(string revisionId)
    {
        if (_revisions.TryGetValue(revisionId, out SeasonRevision? revision))
            return revision;

        throw new KeyNotFoundException($"Revision '{revisionId}' does not exist.");
    }
}
