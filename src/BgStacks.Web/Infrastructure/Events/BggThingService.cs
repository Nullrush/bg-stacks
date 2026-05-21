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
        var detailTasks = items.Select(item => _details.GetAsync(item.ObjectId, ct)).ToArray();
        var statTasks = items.Select(item => _stats.GetAsync(item.ObjectId, ct)).ToArray();
        var details = await Task.WhenAll(detailTasks);
        var stats = await Task.WhenAll(statTasks);

        var entries = new List<GameEntry>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            if (details[i] is null) continue;
            entries.Add(BuildEntry(items[i].ObjectId, details[i]!, stats[i], items[i].Body));
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
