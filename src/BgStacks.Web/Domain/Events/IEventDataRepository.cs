namespace BgStacks.Web.Domain.Events;

public interface IEventDataRepository
{
    Task<EventData?> GetAsync(EventSlug slug, CancellationToken ct = default);
}
