﻿using System.Text.Json.Serialization;

namespace VaultPreviewLambda.Models;

public class CharacterProgress
{
    [JsonPropertyName("player_class")] public string? PlayerClass { get; set; } = null;
    [JsonPropertyName("raid")] public IDictionary<string, BossProgress> Raid { get; set; } = new Dictionary<string, BossProgress>();
    [JsonPropertyName("dungeons")] public IList<DungeonRun> Dungeons { get; set; } = new List<DungeonRun>();
    [JsonPropertyName("delves")] public IDictionary<int, int> Delves { get; set; } = new Dictionary<int, int>();

    [JsonPropertyName("season")] public int? Season { get; set; } = null;
}