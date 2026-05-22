using BgStacks.Web.Domain.Events;

namespace BgStacks.Web.Tests.Presentation.Helpers;

public class InMemoryEventRepository : IEventRepository
{
    private readonly Dictionary<string, Event> _store = new();

    public void Clear() => _store.Clear();

    public void Seed(Event @event) => _store[@event.Slug.Value] = @event;

    public Task<Event?> GetAsync(EventSlug slug, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(slug.Value));

    public Task<IReadOnlyList<Event>> ListPublicAsync(CancellationToken ct = default)
    {
        IReadOnlyList<Event> results = _store.Values
            .Where(e => e.IsPublic)
            .OrderByDescending(e => e.EventDate)
            .ToList();
        return Task.FromResult(results);
    }

    public Task SaveAsync(Event @event, CancellationToken ct = default)
    {
        _store[@event.Slug.Value] = @event;
        return Task.CompletedTask;
    }
}
