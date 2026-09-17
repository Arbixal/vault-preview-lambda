using VaultShared.Seasons;

namespace VaultPreview.VaultCache.Models;

public class CharacterData
{
    #region Base Information
    public string? Name { get; set; }
    public string? Realm { get; set; }
    public string? Region { get; set; }
    
    public long LastUpdatedTimestamp { get; set; }

    public string? SeasonId { get; set; }
    public string? SeasonRevision { get; set; }
    public string? SeasonRevisionHash { get; set; }

    public bool HasSeasonAwareBaseline => !string.IsNullOrWhiteSpace(SeasonId) &&
                                          !string.IsNullOrWhiteSpace(SeasonRevision) &&
                                          !string.IsNullOrWhiteSpace(SeasonRevisionHash);

    public DateTimeOffset LastUpdated => DateTimeOffset.FromUnixTimeMilliseconds(LastUpdatedTimestamp);

    public bool IsValid => !string.IsNullOrEmpty(Name)
                           && !string.IsNullOrEmpty(Realm)
                           && !string.IsNullOrEmpty(Region);

    public string FullName => $"{Name}-{Realm} ({Region})";
    #endregion
    
    #region Delve Information

    /// <summary>
    /// Data will look something like:
    ///  "DelvesCompleted": {
    ///     "1": 1,
    ///     "2": 1,
    ///     "3": 1,
    ///     "4": 1,
    ///     "5": 1,
    ///     "6": 1,
    ///     "7": 1,
    ///     "8": 64,
    ///     "9": 2,
    ///     "10": 2,
    ///     "11": 3
    ///  }
    /// </summary>
    public Dictionary<int, int> DelvesCompleted { get; set; } = new()
    {
        [1] = 0,
        [2] = 0,
        [3] = 0,
        [4] = 0,
        [5] = 0,
        [6] = 0,
        [7] = 0,
        [8] = 0,
        [9] = 0,
        [10] = 0,
        [11] = 0,
    };
    
    #endregion


    public CharacterData() { }

    public CharacterData(string? name, string? realm, string? region)
    {
        Name = name;
        Realm = realm;
        Region = region;
    }

    public void SetDelveData(Dictionary<int, int> data)
    {
        DelvesCompleted = data.ToDictionary(x => x.Key, x => x.Value);
    }

    public void SetDelveBaseline(Dictionary<int, int> data, ActiveSeasonRevision revision)
    {
        SetDelveData(data);
        SeasonId = revision.SeasonId;
        SeasonRevision = revision.Revision;
        SeasonRevisionHash = revision.RevisionHash;
    }
}
