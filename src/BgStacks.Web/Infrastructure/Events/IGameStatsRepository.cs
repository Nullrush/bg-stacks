namespace BgStacks.Web.Infrastructure.Events;

public interface IGameStatsRepository
{
    Task<IReadOnlyDictionary<int, GameStatsDocument>> GetManyAsync(IEnumerable<int> ids, CancellationToken ct = default);
    Task UpsertAsync(GameStatsDocument doc, CancellationToken ct = default);
    Task<GameStatsDocument?> GetAsync(int id, CancellationToken ct = default);
}
