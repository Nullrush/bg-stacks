using BgStacks.Web.Domain.Events;

namespace BgStacks.Web.Application.Events;

public sealed class EventDataService
{
    private readonly IEventRepository _events;
    private readonly IBggGeeklistService _geeklist;

    public EventDataService(IEventRepository events, IBggGeeklistService geeklist)
    {
        _events = events;
        _geeklist = geeklist;
    }

    public async Task<EventData?> GetEventDataAsync(EventSlug slug, CancellationToken ct = default)
    {
        if (int.TryParse(slug.Value, out var numericId))
            return await _geeklist.GetEventDataAsync(numericId, slug, ct);

        var @event = await _events.GetAsync(slug, ct);
        if (@event?.GeeklistId is not int geeklistId)
            return null;

        return await _geeklist.GetEventDataAsync(geeklistId, slug, ct);
    }
}
