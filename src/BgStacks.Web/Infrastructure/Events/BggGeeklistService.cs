using System.Collections.Concurrent;
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
    // Runs the background work. Production: fire-and-forget via Task.Run.
    // Tests: inject an awaiting runner so the first call blocks until the fetch completes.
    private readonly Func<Func<Task>, Task> _backgroundRunner;
    // Static so it's shared across all scoped instances — prevents duplicate background fetches
    // when multiple requests arrive before the first one populates the cache.
    private static readonly ConcurrentDictionary<int, byte> _inFlight = new();

    public BggGeeklistService(BggClient bgg, IBggThingService things, IFusionCache cache,
        int checkIntervalMinutes = 30, Func<Func<Task>, Task>? backgroundRunner = null)
    {
        _bgg = bgg;
        _things = things;
        _cache = cache;
        _checkIntervalMinutes = checkIntervalMinutes;
        _backgroundRunner = backgroundRunner ?? (f => { _ = Task.Run(f); return Task.CompletedTask; });
    }

    public async Task<EventDataResult> GetEventDataAsync(int geeklistId, EventSlug slug,
        CancellationToken ct = default)
    {
        var checkInterval = TimeSpan.FromMinutes(Math.Max(_checkIntervalMinutes, 1));
        var cacheKey = $"bgg-event:v1:{geeklistId}";

        // Fast path: fresh value in cache.
        var fresh = await _cache.TryGetAsync<EventData?>(cacheKey, token: ct);
        if (fresh.HasValue)
            return ToResult(fresh.Value, slug);

        // Stale (fail-safe protected) value — serve it and revalidate in background.
        var stale = await _cache.TryGetAsync<EventData?>(
            cacheKey, options => options.IsFailSafeEnabled = true, ct);
        if (stale.HasValue)
        {
            _ = EnsureBackgroundFetch(geeklistId, cacheKey, checkInterval);
            return ToResult(stale.Value, slug);
        }

        // Nothing cached — start background fetch and tell the client to wait.
        await EnsureBackgroundFetch(geeklistId, cacheKey, checkInterval);
        return new EventDataResult(null, IsLoading: true);
    }

    private Task EnsureBackgroundFetch(int geeklistId, string cacheKey, TimeSpan checkInterval)
    {
        if (_inFlight.TryAdd(geeklistId, 0))
            return _backgroundRunner(() => BackgroundFetchAsync(geeklistId, cacheKey, checkInterval));
        return Task.CompletedTask;
    }

    private async Task BackgroundFetchAsync(int geeklistId, string cacheKey, TimeSpan checkInterval)
    {
        try
        {
            await _cache.GetOrSetAsync<EventData?>(
                cacheKey,
                (ctx, token) => FetchAsync(geeklistId, ctx, token),
                new FusionCacheEntryOptions
                {
                    Duration = checkInterval,
                    IsFailSafeEnabled = true,
                    FailSafeMaxDuration = TimeSpan.FromHours(24),
                    FailSafeThrottleDuration = TimeSpan.FromSeconds(30),
                },
                CancellationToken.None);
        }
        catch { }
        finally { _inFlight.TryRemove(geeklistId, out _); }
    }

    private async Task<EventData?> FetchAsync(int geeklistId,
        FusionCacheFactoryExecutionContext<EventData?> ctx, CancellationToken token)
    {
        var stale = ctx.HasStaleValue ? ctx.StaleValue.GetValueOrDefault() : null;

        BggSdk.Models.Geeklist geeklist;
        try
        {
            geeklist = await _bgg.GetGeeklistAsync(geeklistId, token);
        }
        catch (BggRetryException)
        {
            if (ctx.HasStaleValue) return ctx.StaleValue.GetValueOrDefault();
            ctx.Options.Duration = TimeSpan.FromSeconds(15);
            ctx.Options.IsFailSafeEnabled = false;
            return null;
        }
        catch (BggApiException ex)
        {
            var statusCode = (ex.InnerException as HttpRequestException)?.StatusCode;
            if (statusCode != HttpStatusCode.NotFound) throw;
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
            GeeklistId = geeklistId,
            EditTimestamp = geeklist.EditTimestamp,
            GamesJson = JsonSerializer.Serialize(entries),
            MechanicsJson = JsonSerializer.Serialize(allMechanics),
            CategoriesJson = JsonSerializer.Serialize(allCategories),
        };
    }

    private static EventDataResult ToResult(EventData? value, EventSlug slug)
    {
        if (value is null) return new EventDataResult(null);
        return new EventDataResult(value.SlugValue == slug.Value ? value : value with { SlugValue = slug.Value });
    }
}
