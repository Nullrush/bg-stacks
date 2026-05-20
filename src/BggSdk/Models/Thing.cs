namespace BggSdk.Models;

public sealed record Thing(
    int Id,
    string Name,
    string? Thumbnail,
    int MinPlayers,
    int MaxPlayers,
    int PlayingTime,
    int MinPlayTime,
    int MaxPlayTime,
    double AverageRating,
    double BayesRating,
    int UserRatings,
    double AverageWeight,
    int? BggRank,
    IReadOnlyDictionary<string, int?> SubRanks,
    IReadOnlyList<string> Mechanics,
    IReadOnlyList<string> Categories,
    IReadOnlyList<int> BestPlayerCounts,
    IReadOnlyList<int> RecommendedPlayerCounts
);
