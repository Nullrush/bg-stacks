using System.Text.Json.Serialization;
using BggSdk;
using BgStacks.Web.Application.Events;

namespace BgStacks.Web.Infrastructure.Events;

public sealed class BggThingService : IBggThingService
{
    private readonly BggClient _bgg;
    private readonly IGameDetailsRepository _details;
    private readonly IGameStatsRepository _stats;

    public BggThingService(BggClient bgg, IGameDetailsRepository details, IGameStatsRepository stats)
    {
        _bgg = bgg;
        _details = details;
        _stats = stats;
    }

    public async Task EnsureThingsAsync(IReadOnlyList<int> objectIds, CancellationToken ct = default)
    {
        var existing = await _details.GetExistingIdsAsync(objectIds, ct);
        var missing = objectIds.Where(id => !existing.Contains(id)).ToList();
        if (missing.Count == 0) return;

        var things = await _bgg.GetThingsAsync(missing, ct);
        foreach (var thing in things)
        {
            await _details.UpsertAsync(GameDetailsDocument.FromThing(
                thing.Id, thing.Name,
                thing.MinPlayers, thing.MaxPlayers,
                thing.MinPlayTime, thing.MaxPlayTime,
                thing.Mechanics, thing.Categories,
                thing.Thumbnail), ct);

            await _stats.UpsertAsync(GameStatsDocument.FromThing(
                thing.Id,
                thing.AverageRating, thing.BayesRating, thing.UserRatings, thing.AverageWeight,
                thing.BggRank, thing.SubRanks,
                thing.BestPlayerCounts, thing.RecommendedPlayerCounts), ct);
        }
    }

    public async Task<IReadOnlyList<GameEntry>> GetGameEntriesAsync(
        IReadOnlyList<(int ObjectId, string Body)> items, CancellationToken ct = default)
    {
        var entries = new List<GameEntry>(items.Count);
        foreach (var (objectId, body) in items)
        {
            var detail = await _details.GetAsync(objectId, ct);
            if (detail is null) continue;
            var stat = await _stats.GetAsync(objectId, ct);
            entries.Add(BuildEntry(objectId, detail, stat, body));
        }
        return entries;
    }

    private static GameEntry BuildEntry(int id, GameDetailsDocument detail,
        GameStatsDocument? stat, string body)
    {
        var minP = detail.MinPlayers;
        var maxP = detail.MaxPlayers;
        var minT = detail.MinTime;
        var maxT = detail.MaxTime;

        return new GameEntry
        {
            Id = id,
            Name = detail.Name,
            Players = minP == maxP ? $"{minP}" : $"{minP}-{maxP}",
            MinPlayers = minP,
            MaxPlayers = maxP,
            BestPlayers = stat?.BestPlayers ?? [],
            RecommendedPlayers = stat?.RecommendedPlayers ?? [],
            Weight = stat?.Weight ?? 0,
            Time = minT == maxT ? $"{minT}" : $"{minT}-{maxT}",
            MinTime = minT,
            MaxTime = maxT,
            AvgRating = stat?.AvgRating ?? 0,
            GeekRating = stat?.GeekRating ?? 0,
            Votes = stat?.Votes ?? 0,
            BggRank = stat?.BggRank,
            SubRanks = stat?.SubRanks ?? new Dictionary<string, int?>(),
            Mechanics = detail.Mechanics,
            Categories = detail.Categories,
            Thumbnail = detail.Thumbnail,
            Body = body,
        };
    }
}

public sealed class GameEntry
{
    [JsonPropertyName("id")]                  public int Id { get; init; }
    [JsonPropertyName("name")]               public string Name { get; init; } = "";
    [JsonPropertyName("players")]            public string Players { get; init; } = "";
    [JsonPropertyName("minPlayers")]         public int MinPlayers { get; init; }
    [JsonPropertyName("maxPlayers")]         public int MaxPlayers { get; init; }
    [JsonPropertyName("bestPlayers")]        public IReadOnlyList<int> BestPlayers { get; init; } = [];
    [JsonPropertyName("recommendedPlayers")] public IReadOnlyList<int> RecommendedPlayers { get; init; } = [];
    [JsonPropertyName("weight")]             public double Weight { get; init; }
    [JsonPropertyName("time")]               public string Time { get; init; } = "";
    [JsonPropertyName("minTime")]            public int MinTime { get; init; }
    [JsonPropertyName("maxTime")]            public int MaxTime { get; init; }
    [JsonPropertyName("avgRating")]          public double AvgRating { get; init; }
    [JsonPropertyName("geekRating")]         public double GeekRating { get; init; }
    [JsonPropertyName("votes")]              public int Votes { get; init; }
    [JsonPropertyName("bggRank")]            public int? BggRank { get; init; }
    [JsonPropertyName("subRanks")]           public IReadOnlyDictionary<string, int?> SubRanks { get; init; } = new Dictionary<string, int?>();
    [JsonPropertyName("mechanics")]          public IReadOnlyList<string> Mechanics { get; init; } = [];
    [JsonPropertyName("categories")]         public IReadOnlyList<string> Categories { get; init; } = [];
    [JsonPropertyName("thumbnail")]          public string? Thumbnail { get; init; }
    [JsonPropertyName("body")]               public string Body { get; init; } = "";
}
