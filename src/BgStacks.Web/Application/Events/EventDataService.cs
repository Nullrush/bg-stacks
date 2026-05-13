using BgStacks.Web.Domain.Events;
using Microsoft.Extensions.Caching.Memory;

namespace BgStacks.Web.Application.Events;

public sealed class EventDataService
{
    private readonly IEventDataRepository _repository;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    public EventDataService(IEventDataRepository repository, IMemoryCache cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task<EventData?> GetEventDataAsync(EventSlug slug, CancellationToken ct = default)
    {
        var cacheKey = $"event-data:{slug.Value}";
        if (_cache.TryGetValue(cacheKey, out EventData? cached))
            return cached;

        var data = await _repository.GetAsync(slug, ct);
        if (data is not null)
            _cache.Set(cacheKey, data, CacheTtl);
        return data;
    }
}
