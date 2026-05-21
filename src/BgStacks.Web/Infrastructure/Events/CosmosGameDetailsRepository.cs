using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;

namespace BgStacks.Web.Infrastructure.Events;

public sealed class CosmosGameDetailsRepository : IGameDetailsRepository
{
    private readonly Container _container;

    public CosmosGameDetailsRepository(CosmosClient client, string databaseId)
        => _container = client.GetContainer(databaseId, "game-details");

    public async Task<IReadOnlySet<int>> GetExistingIdsAsync(IEnumerable<int> ids, CancellationToken ct = default)
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

    public async Task<IReadOnlyDictionary<int, GameDetailsDocument>> GetManyAsync(
        IEnumerable<int> ids, CancellationToken ct = default)
    {
        var pairs = ids.Distinct().Select(id => (id.ToString(), new PartitionKey(id.ToString()))).ToList();
        if (pairs.Count == 0) return new Dictionary<int, GameDetailsDocument>();

        var result = new Dictionary<int, GameDetailsDocument>();
        foreach (var chunk in pairs.Chunk(100))
        {
            var response = await _container.ReadManyItemsAsync<GameDetailsDocument>(chunk, cancellationToken: ct);
            foreach (var doc in response)
                if (int.TryParse(doc.Id, out var numId))
                    result[numId] = doc;
        }
        return result;
    }

    public async Task UpsertAsync(GameDetailsDocument doc, CancellationToken ct = default)
        => await _container.UpsertItemAsync(doc, new PartitionKey(doc.Id), cancellationToken: ct);

    public async Task<GameDetailsDocument?> GetAsync(int id, CancellationToken ct = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<GameDetailsDocument>(
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

public sealed class GameDetailsDocument
{
    [JsonPropertyName("id")]          public string Id { get; set; } = "";
    [JsonPropertyName("name")]        public string Name { get; set; } = "";
    [JsonPropertyName("minPlayers")]  public int MinPlayers { get; set; }
    [JsonPropertyName("maxPlayers")]  public int MaxPlayers { get; set; }
    [JsonPropertyName("minTime")]     public int MinTime { get; set; }
    [JsonPropertyName("maxTime")]     public int MaxTime { get; set; }
    [JsonPropertyName("mechanics")]   public List<string> Mechanics { get; set; } = [];
    [JsonPropertyName("categories")]  public List<string> Categories { get; set; } = [];
    [JsonPropertyName("thumbnail")]    public string? Thumbnail { get; set; }
    [JsonPropertyName("yearPublished")] public int? YearPublished { get; set; }

    public static GameDetailsDocument FromThing(int id, string name,
        int minPlayers, int maxPlayers, int minTime, int maxTime,
        IEnumerable<string> mechanics, IEnumerable<string> categories,
        string? thumbnail, int? yearPublished) => new()
    {
        Id = id.ToString(),
        Name = name,
        MinPlayers = minPlayers,
        MaxPlayers = maxPlayers,
        MinTime = minTime,
        MaxTime = maxTime,
        Mechanics = mechanics.ToList(),
        Categories = categories.ToList(),
        Thumbnail = thumbnail,
        YearPublished = yearPublished,
    };
}
