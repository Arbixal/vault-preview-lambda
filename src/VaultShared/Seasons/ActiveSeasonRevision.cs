namespace VaultShared.Seasons;

public sealed record ActiveSeasonRevision(
    string SeasonId,
    string Revision,
    string RevisionHash,
    int? SourceSeasonId);
