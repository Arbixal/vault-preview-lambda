using System.Text.Json;
using VaultShared.Seasons;
using Xunit;

namespace VaultShared.Tests;

public class SeasonSourceDefinitionTests
{
    [Theory]
    [InlineData("future-season-r1.json", "future-season-r1", "future-season")]
    [InlineData("midnight-s1-r1.json", "midnight-s1-r1", "midnight-s1")]
    public void ExampleSourceDefinition_MapsToAValidatedImmutableRevision(
        string fixtureName,
        string revisionId,
        string seasonId)
    {
        string sourcePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            fixtureName);
        string sourceJson = File.ReadAllText(sourcePath);
        SeasonConfiguration configuration = JsonSerializer.Deserialize<SeasonConfiguration>(
            sourceJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        SeasonConfigurationValidator.ValidateOrThrow(configuration);
        SeasonRevision revision = SeasonRevision.Create(revisionId, configuration);

        Assert.Equal(seasonId, revision.Configuration.Id);
        Assert.Matches("^sha256:[0-9a-f]{64}$", revision.RevisionHash);
        Assert.Equal(SeasonRevisionStatus.Draft, revision.Status);
    }
}
