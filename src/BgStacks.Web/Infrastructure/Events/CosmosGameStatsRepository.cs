using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;

namespace BgStacks.Web.Infrastructure.Events;

public sealed class CosmosGameStatsRepository : IGameStatsRepository
{
    private readonly Container _container;

    public CosmosGameStatsRepository(CosmosClient client, string databaseId)
        => _container = client.GetContainer(databaseId, "game-stats");

    public async Task<IReadOnlySet<int>> GetExistingIdsAsync(
        IEnumerable<int> ids, CancellationToken ct = default)
    {
        var pairs = ids.Distinct().Select(id => (id.ToString(), new PartitionKey(id.ToString()))).ToList();
        if (pairs.Count == 0) return new HashSet<int>();

        var found = new HashSet<int>();
        foreach (var chunk in pairs.Chunk(100))
        {
            var response = await _container.ReadManyItemsAsync<IdProjection>(chunk, cancellationToken: ct);
            foreach (var item in response)
                if (int.TryParse(item.Id, out var numId))
                    found.Add(numId);
        }
        return found;
    }

    public async Task<IReadOnlyDictionary<int, GameStatsDocument>> GetManyAsync(
        IEnumerable<int> ids, CancellationToken ct = default)
    {
        var pairs = ids.Distinct().Select(id => (id.ToString(), new PartitionKey(id.ToString()))).ToList();
        if (pairs.Count == 0) return new Dictionary<int, GameStatsDocument>();

        var result = new Dictionary<int, GameStatsDocument>();
        foreach (var chunk in pairs.Chunk(100))
        {
            var response = await _container.ReadManyItemsAsync<GameStatsDocument>(chunk, cancellationToken: ct);
            foreach (var doc in response)
                if (int.TryParse(doc.Id, out var numId))
                    result[numId] = doc;
        }
        return result;
    }

    public async Task UpsertAsync(GameStatsDocument doc, CancellationToken ct = default)
        => await _container.UpsertItemAsync(doc, new PartitionKey(doc.Id), cancellationToken: ct);

    public async Task<GameStatsDocument?> GetAsync(int id, CancellationToken ct = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<GameStatsDocument>(
                id.ToString(), new PartitionKey(id.ToString()), cancellationToken: ct);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private sealed class IdProjection
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
    }
}

public sealed class GameStatsDocument
{
    [JsonPropertyName("id")]                  public string Id { get; set; } = "";
    [JsonPropertyName("avgRating")]           public double AvgRating { get; set; }
    [JsonPropertyName("geekRating")]          public double GeekRating { get; set; }
    [JsonPropertyName("votes")]              public int Votes { get; set; }
    [JsonPropertyName("weight")]             public double Weight { get; set; }
    [JsonPropertyName("bggRank")]            public int? BggRank { get; set; }
    [JsonPropertyName("subRanks")]           public Dictionary<string, int?> SubRanks { get; set; } = new();
    [JsonPropertyName("bestPlayers")]        public List<int> BestPlayers { get; set; } = [];
    [JsonPropertyName("recommendedPlayers")] public List<int> RecommendedPlayers { get; set; } = [];

    public static GameStatsDocument FromThing(int id,
        double avgRating, double geekRating, int votes, double weight,
        int? bggRank, IReadOnlyDictionary<string, int?> subRanks,
        IEnumerable<int> bestPlayers, IEnumerable<int> recommendedPlayers) => new()
    {
        Id = id.ToString(),
        AvgRating = avgRating,
        GeekRating = geekRating,
        Votes = votes,
        Weight = weight,
        BggRank = bggRank,
        SubRanks = new Dictionary<string, int?>(subRanks),
        BestPlayers = bestPlayers.ToList(),
        RecommendedPlayers = recommendedPlayers.ToList(),
    };
}
