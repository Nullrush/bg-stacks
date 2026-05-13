using BgStacks.Web.Domain.Events;

namespace BgStacks.Web.Application.Events;

public sealed class EventService
{
    private readonly IEventRepository _repository;

    public EventService(IEventRepository repository) => _repository = repository;

    public Task<IReadOnlyList<Event>> ListPublicEventsAsync(CancellationToken ct = default)
        => _repository.ListPublicAsync(ct);

    public Task<Event?> GetEventAsync(EventSlug slug, CancellationToken ct = default)
        => _repository.GetAsync(slug, ct);
}
