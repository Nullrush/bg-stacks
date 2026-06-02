using BgStacks.Web.Application.Events;
using BgStacks.Web.Domain.Events;

namespace BgStacks.Web.Tests.Presentation.Helpers;

public class InMemoryBggGeeklistService : IBggGeeklistService
{
    private readonly Dictionary<int, EventData> _store = new();

    public void Seed(int geeklistId, EventData data) => _store[geeklistId] = data;

    public Task<EventDataResult> GetEventDataAsync(int geeklistId, EventSlug slug,
        CancellationToken ct = default)
    {
        var data = _store.GetValueOrDefault(geeklistId);
        return Task.FromResult(new EventDataResult(data));
    }
}
