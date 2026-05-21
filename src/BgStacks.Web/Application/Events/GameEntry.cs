using System.Text.Json.Serialization;

namespace BgStacks.Web.Application.Events;

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
    [JsonPropertyName("yearPublished")]       public int? YearPublished { get; init; }
    [JsonPropertyName("description")]         public string Description { get; init; } = "";
}
