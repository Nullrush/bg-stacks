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
        var @event = await _events.GetAsync(slug, ct);
        int geeklistId;

        if (@event?.GeeklistId is int id)
            geeklistId = id;
        else if (int.TryParse(slug.Value, out var numericId))
            geeklistId = numericId;
        else
            return null;

        return await _geeklist.GetEventDataAsync(geeklistId, slug, ct);
    }
}
