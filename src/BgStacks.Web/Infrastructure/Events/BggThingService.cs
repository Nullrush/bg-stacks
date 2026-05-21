using BggSdk;
using BgStacks.Web.Application.Events;
using Microsoft.Extensions.Logging;

namespace BgStacks.Web.Infrastructure.Events;

public sealed class BggThingService : IBggThingService
{
    private readonly BggClient _bgg;
    private readonly IGameDetailsRepository _details;
    private readonly IGameStatsRepository _stats;
    private readonly ILogger<BggThingService> _logger;

    public BggThingService(BggClient bgg, IGameDetailsRepository details, IGameStatsRepository stats,
        ILogger<BggThingService> logger)
    {
        _bgg = bgg;
        _details = details;
        _stats = stats;
        _logger = logger;
    }

    public async Task EnsureThingsAsync(IReadOnlyList<int> objectIds, CancellationToken ct = default)
    {
        var deduped = objectIds.Distinct().ToList();
        if (deduped.Count == 0) return;

        var detailsTask = _details.GetExistingIdsAsync(deduped, ct);
        var statsTask = _stats.GetExistingIdsAsync(deduped, ct);
        var existingDetails = await detailsTask;
        var existingStats = await statsTask;

        var missingDetails = deduped.Where(id => !existingDetails.Contains(id)).ToHashSet();
        var missingStats = deduped.Where(id => !existingStats.Contains(id)).ToHashSet();
        var toFetch = missingDetails.Union(missingStats).ToList();
        if (toFetch.Count == 0) return;

        var things = await _bgg.GetThingsAsync(toFetch, ct);
        if (things.Count < toFetch.Count)
            _logger.LogWarning("BGG returned {Returned} of {Requested} requested things",
                things.Count, toFetch.Count);

        foreach (var thing in things)
        {
            // Details and stats are written once and intentionally not refreshed — stale stats
            // are tolerated in exchange for avoiding unnecessary BGG API calls and Cosmos writes.
            if (missingDetails.Contains(thing.Id))
                await _details.UpsertAsync(GameDetailsDocument.FromThing(
                    thing.Id, thing.Name,
                    thing.MinPlayers, thing.MaxPlayers,
                    thing.MinPlayTime, thing.MaxPlayTime,
                    thing.Mechanics, thing.Categories,
                    thing.Thumbnail, thing.YearPublished), ct);

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
        var detailMapTask = _details.GetManyAsync(ids, ct);
        var statMapTask = _stats.GetManyAsync(ids, ct);
        var detailMap = await detailMapTask;
        var statMap = await statMapTask;

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
            YearPublished = detail.YearPublished,
            Description = body,
        };
    }
}
