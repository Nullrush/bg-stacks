using BgStacks.Web.Domain.Events;

namespace BgStacks.Web.Application.Events;

public interface IBggGeeklistService
{
    Task<EventDataResult> GetEventDataAsync(int geeklistId, EventSlug slug, CancellationToken ct = default);
}
