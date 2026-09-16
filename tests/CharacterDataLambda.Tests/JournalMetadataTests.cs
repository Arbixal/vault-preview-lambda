using System.Net;
using System.Text;
using VaultPreview.Blizzard;
using VaultPreview.Blizzard.Models;
using VaultShared;
using VaultShared.Seasons;
using Xunit;

namespace CharacterDataLambda.Tests;

public class JournalMetadataTests
{
    [Fact]
    public void SelectEligibleInstances_UsesPolicyOrderAndRetainsCompleteEncounters()
    {
        SeasonActivityDefinition activity = new(
            "raid",
            "raid",
            "Raids",
            null,
            0,
            [],
            ["wow:journal-instance:1320", "wow:journal-instance:1317"]);
        BlizzardJournalInstance first = new()
        {
            Id = 1317,
            Name = "The Tidebound Grotto",
            Encounters = [new BlizzardJournalEncounter { Id = 2849, Name = "Nymrissa Wavecaller" }]
        };
        BlizzardJournalInstance second = new()
        {
            Id = 1320,
            Name = "The Venomous Abyss",
            Encounters =
            [
                new BlizzardJournalEncounter { Id = 2888, Name = "Nek'zali the Soulcoiler" },
                new BlizzardJournalEncounter { Id = 2874, Name = "Entombed Sentinels" }
            ]
        };
        BlizzardJournalInstance notEligible = new()
        {
            Id = 1302,
            Name = "Manaforge Omega",
            Encounters = [new BlizzardJournalEncounter { Id = 2684, Name = "Plexus Sentinel" }]
        };

        IReadOnlyList<BlizzardJournalInstance> selected = JournalMetadataResolver.SelectEligibleInstances(
            activity,
            [first, notEligible, second]);

        Assert.Equal([1320, 1317], selected.Select(x => x.Id));
        Assert.Equal(2, selected[0].Encounters.Count);
        Assert.Equal("Nek'zali the Soulcoiler", selected[0].Encounters[0].Name);
    }

    [Fact]
    public void GetCharacterInstanceIds_ReturnsInstancesEvenWithoutCharacterKills()
    {
        BlizzardEncounterResponse response = new()
        {
            Expansions =
            [
                new BlizzardExpansion
                {
                    Expansion = new BlizzardBase { Name = "Current Season" },
                    Instances =
                    [
                        new BlizzardInstance { Instance = new BlizzardBase { Id = 1320 } },
                        new BlizzardInstance { Instance = new BlizzardBase { Id = 1317 } },
                        new BlizzardInstance { Instance = new BlizzardBase { Id = 1320 } }
                    ]
                }
            ]
        };

        Assert.Equal([1320, 1317], JournalMetadataResolver.GetCharacterInstanceIds(response));
    }

    [Fact]
    public async Task GetJournalInstance_CachesMetadataForWarmHandler()
    {
        FakeJournalHttpHandler httpHandler = new();
        BlizzardApiHandler handler = new(
            new FakeHttpClientFactory(httpHandler),
            new FakeSecretHandler(),
            new FakeJournalMetadataCache());

        await handler.Connect();
        BlizzardJournalMetadata? first = await handler.GetJournalInstance("us", 1320);
        BlizzardJournalMetadata? second = await handler.GetJournalInstance("us", 1320);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Instance.Id, second.Instance.Id);
        Assert.False(second.IsStale);
        Assert.Equal(1, httpHandler.RequestCount);
        Assert.Contains("static-us", httpHandler.LastRequestUri!.Query);
        Assert.Contains("journal-instance/1320", httpHandler.LastRequestUri.AbsolutePath);
    }

    [Fact]
    public async Task GetJournalInstance_ReturnsNullForMissingUpstreamInstance()
    {
        BlizzardApiHandler handler = new(
            new FakeHttpClientFactory(new FakeJournalHttpHandler(HttpStatusCode.NotFound)),
            new FakeSecretHandler(),
            new FakeJournalMetadataCache());

        await handler.Connect();

        Assert.Null(await handler.GetJournalInstance("us", 999999));
    }

    private sealed class FakeJournalMetadataCache : IJournalMetadataCache
    {
        private readonly IDictionary<string, JournalMetadataCacheEntry> _entries = new Dictionary<string, JournalMetadataCacheEntry>();

        public Task<JournalMetadataCacheEntry?> Get(string region, string staticNamespace, long instanceId)
        {
            string key = $"{region}:{staticNamespace}:{instanceId}";
            return Task.FromResult(_entries.TryGetValue(key, out JournalMetadataCacheEntry? entry) ? entry : null);
        }

        public Task Put(string region, string staticNamespace, long instanceId, JournalMetadataCacheEntry entry)
        {
            _entries[$"{region}:{staticNamespace}:{instanceId}"] = entry;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        private readonly HttpClient _client = new(handler);

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class FakeSecretHandler : ISecretHandler
    {
        public Task<string?> GetSecret(string secretId) => Task.FromResult<string?>(
            secretId == "/Blizzard/Token" ? "token" : null);

        public Task<long> GetSecretAsLong(string secretId) =>
            Task.FromResult(DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds());

        public Task PutSecret<T>(string secretId, T secretValue) => Task.CompletedTask;
    }

    private sealed class FakeJournalHttpHandler(HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ++RequestCount;
            LastRequestUri = request.RequestUri;
            const string content = "{\"id\":1320,\"name\":\"The Venomous Abyss\",\"encounters\":[{\"id\":2888,\"name\":\"Nek'zali the Soulcoiler\"}]}";
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }
    }
}
