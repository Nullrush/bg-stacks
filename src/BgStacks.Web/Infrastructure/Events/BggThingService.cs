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
        var deduped = objectIds.Distinct().ToList();
        if (deduped.Count == 0) return;

        var existingDetails = await _details.GetExistingIdsAsync(deduped, ct);
        var existingStats = (await _stats.GetManyAsync(deduped, ct)).Keys.ToHashSet();
        var missingDetails = deduped.Where(id => !existingDetails.Contains(id)).ToHashSet();
        var missingStats = deduped.Where(id => !existingStats.Contains(id)).ToHashSet();
        var toFetch = missingDetails.Union(missingStats).ToList();
        if (toFetch.Count == 0) return;

        var things = await _bgg.GetThingsAsync(toFetch, ct);
        foreach (var thing in things)
        {
            if (missingDetails.Contains(thing.Id))
                await _details.UpsertAsync(GameDetailsDocument.FromThing(
                    thing.Id, thing.Name,
                    thing.MinPlayers, thing.MaxPlayers,
                    thing.MinPlayTime, thing.MaxPlayTime,
                    thing.Mechanics, thing.Categories,
                    thing.Thumbnail), ct);

            if (missingStats.Contains(thing.Id))
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
        var ids = items.Select(i => i.ObjectId).ToList();
        var detailMap = await _details.GetManyAsync(ids, ct);
        var statMap = await _stats.GetManyAsync(ids, ct);

        var entries = new List<GameEntry>(items.Count);
        foreach (var (objectId, body) in items)
        {
            if (!detailMap.TryGetValue(objectId, out var detail)) continue;
            statMap.TryGetValue(objectId, out var stat);
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
