using BgStacks.Web.Application.Events;
using BgStacks.Web.Domain.Events;

namespace BgStacks.Web.Tests.Presentation.Helpers;

public class InMemoryBggGeeklistService : IBggGeeklistService
{
    private readonly Dictionary<int, EventData> _store = new();

    public void Seed(int geeklistId, EventData data) => _store[geeklistId] = data;

    public Task<EventData?> GetEventDataAsync(int geeklistId, EventSlug slug,
        CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(geeklistId));
}
