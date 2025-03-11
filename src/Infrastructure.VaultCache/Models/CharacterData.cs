namespace VaultPreview.VaultCache.Models;

public class CharacterData
{
    #region Base Information
    public string? Name { get; set; }
    public string? Realm { get; set; }
    public string? Region { get; set; }
    
    public long LastUpdatedTimestamp { get; set; }

    public DateTimeOffset LastUpdated => DateTimeOffset.FromUnixTimeMilliseconds(LastUpdatedTimestamp);

    public bool NeedsUpdating => !string.IsNullOrEmpty(Name)
                                 && !string.IsNullOrEmpty(Realm)
                                 && !string.IsNullOrEmpty(Region)
                                 && LastUpdated < DateTimeOffset.UtcNow.Subtract(TimeSpan.FromDays(6));
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
        DelvesCompleted[1] = data.GetValueOrDefault(1, 0);
        DelvesCompleted[2] = data.GetValueOrDefault(2, 0);
        DelvesCompleted[3] = data.GetValueOrDefault(3, 0);
        DelvesCompleted[4] = data.GetValueOrDefault(4, 0);
        DelvesCompleted[5] = data.GetValueOrDefault(5, 0);
        DelvesCompleted[6] = data.GetValueOrDefault(6, 0);
        DelvesCompleted[7] = data.GetValueOrDefault(7, 0);
        DelvesCompleted[8] = data.GetValueOrDefault(8, 0);
        DelvesCompleted[9] = data.GetValueOrDefault(9, 0);
        DelvesCompleted[10] = data.GetValueOrDefault(10, 0);
        DelvesCompleted[11] = data.GetValueOrDefault(11, 0);
    }
}