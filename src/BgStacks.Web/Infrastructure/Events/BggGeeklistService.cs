using System.Net;
using System.Text.Json;
using BggSdk;
using BggSdk.Exceptions;
using BgStacks.Web.Application.Events;
using BgStacks.Web.Domain.Events;
using ZiggyCreatures.Caching.Fusion;

namespace BgStacks.Web.Infrastructure.Events;

public sealed class BggGeeklistService : IBggGeeklistService
{
    private readonly BggClient _bgg;
    private readonly IBggThingService _things;
    private readonly IFusionCache _cache;
    private readonly int _checkIntervalMinutes;

    public BggGeeklistService(BggClient bgg, IBggThingService things, IFusionCache cache,
        int checkIntervalMinutes = 30)
    {
        _bgg = bgg;
        _things = things;
        _cache = cache;
        _checkIntervalMinutes = checkIntervalMinutes;
    }

    public async Task<EventData?> GetEventDataAsync(int geeklistId, EventSlug slug,
        CancellationToken ct = default)
    {
        var checkInterval = TimeSpan.FromMinutes(Math.Max(_checkIntervalMinutes, 1));
        var cacheKey = $"bgg-event:v1:{geeklistId}";

        var data = await _cache.GetOrSetAsync<EventData?>(
            cacheKey,
            async (ctx, token) =>
            {
                var stale = ctx.HasStaleValue ? ctx.StaleValue.GetValueOrDefault() : null;

                BggSdk.Models.Geeklist geeklist;
                try
                {
                    geeklist = await _bgg.GetGeeklistAsync(geeklistId, token);
                }
                catch (BggApiException ex)
                {
                    var statusCode = (ex.InnerException as HttpRequestException)?.StatusCode;
                    if (statusCode != HttpStatusCode.NotFound)
                        throw;
                    ctx.Options.Duration = TimeSpan.FromMinutes(5);
                    ctx.Options.IsFailSafeEnabled = false;
                    return null;
                }

                if (stale is not null && geeklist.EditTimestamp == stale.EditTimestamp)
                    return stale;

                var objectIds = geeklist.Items.Select(i => i.ObjectId).Distinct().ToList();
                await _things.EnsureThingsAsync(objectIds, token);

                var itemTuples = geeklist.Items
                    .Select(i => (ObjectId: i.ObjectId, Body: i.Body))
                    .ToList();
                var entries = await _things.GetGameEntriesAsync(itemTuples, token);

                var allMechanics = entries.SelectMany(e => e.Mechanics).Distinct().OrderBy(m => m).ToList();
                var allCategories = entries.SelectMany(e => e.Categories).Distinct().OrderBy(c => c).ToList();

                return new EventData
                {
                    SlugValue = geeklistId.ToString(),
                    Title = geeklist.Title,
                    EditTimestamp = geeklist.EditTimestamp,
                    GamesJson = JsonSerializer.Serialize(entries),
                    MechanicsJson = JsonSerializer.Serialize(allMechanics),
                    CategoriesJson = JsonSerializer.Serialize(allCategories),
                };
            },
            new FusionCacheEntryOptions
            {
                Duration = checkInterval,
                IsFailSafeEnabled = true,
                FailSafeMaxDuration = TimeSpan.FromHours(24),
                FailSafeThrottleDuration = TimeSpan.FromSeconds(30),
            },
            ct);

        return data is null ? null :
            data.SlugValue == slug.Value ? data : data with { SlugValue = slug.Value };
    }
}
