using System.Text.Json;
using VaultPreviewLambda.Models;
using Xunit;

namespace VaultPreviewLambda.Tests;

public class ContractFixtureTests
{
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] _requiredFixtureNames =
    [
        "app-config",
        "current-season",
        "empty-progress",
        "partial-progress",
        "unavailable-section",
        "stale-metadata",
        "unknown-activity-kind",
        "future-season",
        "legacy-flat-response-minimal",
        "legacy-flat-response-full"
    ];

    [Fact]
    public void ManifestFixturesHaveTheExpectedVersionedResponseShape()
    {
        string fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Contract");
        using JsonDocument manifest = _read(fixtureDirectory, "manifest.json");
        HashSet<string> fixtureNames = [];

        foreach (JsonElement fixture in manifest.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            string name = fixture.GetProperty("name").GetString()!;
            fixtureNames.Add(name);
            string path = fixture.GetProperty("path").GetString()!;
            string responseType = fixture.GetProperty("responseType").GetString()!;
            using JsonDocument document = _read(fixtureDirectory, path);

            if (responseType == "LegacyProgressResponse")
            {
                _assertLegacyResponse(document.RootElement);
                continue;
            }

            JsonElement root = document.RootElement;
            Assert.Equal(JsonValueKind.Object, root.ValueKind);
            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());

            if (responseType == "AppConfigResponse")
            {
                AppConfigResponse appConfig = JsonSerializer.Deserialize<AppConfigResponse>(
                    root.GetRawText(),
                    _serializerOptions)!;
                Assert.NotNull(appConfig);
                Assert.True(root.TryGetProperty("activeSeason", out JsonElement activeSeason));
                _assertSeasonSnapshot(activeSeason);
                Assert.Equal(appConfig.ActiveSeason.Id, activeSeason.GetProperty("id").GetString());
                continue;
            }

            Assert.Equal("CharacterProgressResponse", responseType);
            VaultProgressResponse characterResponse = JsonSerializer.Deserialize<VaultProgressResponse>(
                root.GetRawText(),
                _serializerOptions)!;
            Assert.NotNull(characterResponse);
            _assertCharacterResponse(root);
            Assert.Equal(characterResponse.Season.Id, root.GetProperty("season").GetProperty("id").GetString());
            Assert.Equal(characterResponse.Sections.Count, root.GetProperty("sections").GetArrayLength());
        }

        Assert.All(_requiredFixtureNames, name => Assert.Contains(name, fixtureNames));
    }

    private static void _assertLegacyResponse(JsonElement root)
    {
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.True(root.TryGetProperty("bixposter-nagrand", out JsonElement character));
        Assert.Equal(JsonValueKind.Object, character.ValueKind);
        Assert.True(character.TryGetProperty("raid", out JsonElement raid));
        Assert.Equal(JsonValueKind.Object, raid.ValueKind);
        Assert.True(character.TryGetProperty("dungeons", out JsonElement dungeons));
        Assert.Equal(JsonValueKind.Array, dungeons.ValueKind);
        Assert.True(character.TryGetProperty("delves", out JsonElement delves));
        Assert.Equal(JsonValueKind.Object, delves.ValueKind);
        Assert.True(character.TryGetProperty("season", out JsonElement season));
        Assert.Equal(JsonValueKind.Number, season.ValueKind);
    }

    private static void _assertCharacterResponse(JsonElement root)
    {
        JsonElement character = root.GetProperty("character");
        Assert.False(string.IsNullOrWhiteSpace(character.GetProperty("region").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(character.GetProperty("realm").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(character.GetProperty("name").GetString()));
        _assertSeasonSnapshot(root.GetProperty("season"));
        Assert.True(root.GetProperty("progressPeriod").TryGetProperty("resetAt", out _));
        Assert.True(root.GetProperty("progressPeriod").TryGetProperty("asOf", out _));

        foreach (JsonElement section in root.GetProperty("sections").EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(section.GetProperty("id").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(section.GetProperty("kind").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(section.GetProperty("status").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(section.GetProperty("freshness").GetString()));
            Assert.Equal(JsonValueKind.Array, section.GetProperty("slots").ValueKind);
        }
    }

    private static void _assertSeasonSnapshot(JsonElement season)
    {
        Assert.False(string.IsNullOrWhiteSpace(season.GetProperty("id").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(season.GetProperty("revision").GetString()));
        Assert.Matches(
            "^sha256:[0-9a-f]{64}$",
            season.GetProperty("revisionHash").GetString()!);
    }

    private static JsonDocument _read(string directory, string fileName) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, fileName)));
}
