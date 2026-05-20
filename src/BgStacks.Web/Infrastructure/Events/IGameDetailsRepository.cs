namespace BgStacks.Web.Infrastructure.Events;

public interface IGameDetailsRepository
{
    Task<IReadOnlySet<int>> GetExistingIdsAsync(IEnumerable<int> ids, CancellationToken ct = default);
    Task UpsertAsync(GameDetailsDocument doc, CancellationToken ct = default);
    Task<GameDetailsDocument?> GetAsync(int id, CancellationToken ct = default);
}
