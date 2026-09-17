namespace VaultShared.Seasons;

public sealed record SeasonSchedule(
    string SeasonId,
    string RevisionId,
    string RevisionHash,
    DateTimeOffset ActivationAt);
