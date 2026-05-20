using BgStacks.Web.Infrastructure.Events;

namespace BgStacks.Web.Application.Events;

public interface IBggThingService
{
    Task EnsureThingsAsync(IReadOnlyList<int> objectIds, CancellationToken ct = default);
    Task<IReadOnlyList<GameEntry>> GetGameEntriesAsync(
        IReadOnlyList<(int ObjectId, string Body)> items, CancellationToken ct = default);
}
