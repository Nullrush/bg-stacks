using BgStacks.Web.Domain.Events;

namespace BgStacks.Web.Tests.Presentation.Helpers;

public class InMemoryEventDataRepository : IEventDataRepository
{
    private readonly Dictionary<string, EventData> _store = new();

    public void Seed(EventData data) => _store[data.Slug.Value] = data;

    public Task<EventData?> GetAsync(EventSlug slug, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(slug.Value));
}
