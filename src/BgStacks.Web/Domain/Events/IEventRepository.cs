namespace BgStacks.Web.Domain.Events;

public interface IEventRepository
{
    Task<Event?> GetAsync(EventSlug slug, CancellationToken ct = default);
    Task<IReadOnlyList<Event>> ListPublicAsync(CancellationToken ct = default);
    Task SaveAsync(Event @event, CancellationToken ct = default);
}
