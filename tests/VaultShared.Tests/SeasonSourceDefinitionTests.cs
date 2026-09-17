using System.Text.Json;
using VaultShared.Seasons;
using Xunit;

namespace VaultShared.Tests;

public class SeasonSourceDefinitionTests
{
    [Fact]
    public void ExampleSourceDefinition_MapsToAValidatedImmutableRevision()
    {
        string sourcePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "future-season-r1.json");
        string sourceJson = File.ReadAllText(sourcePath);
        SeasonConfiguration configuration = JsonSerializer.Deserialize<SeasonConfiguration>(
            sourceJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        SeasonConfigurationValidator.ValidateOrThrow(configuration);
        SeasonRevision revision = SeasonRevision.Create("future-season-r1", configuration);

        Assert.Equal("future-season", revision.Configuration.Id);
        Assert.Matches("^sha256:[0-9a-f]{64}$", revision.RevisionHash);
        Assert.Equal(SeasonRevisionStatus.Draft, revision.Status);
    }
}
