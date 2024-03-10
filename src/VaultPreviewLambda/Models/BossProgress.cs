using System.Text.Json.Serialization;

namespace VaultPreviewLambda.Models;

public class BossProgress : Dictionary<string, bool>
{
    public BossProgress()
    {
        Add("mythic", false);
        Add("heroic", false);
        Add("normal", false);
        Add("lfr", false);
    }
}