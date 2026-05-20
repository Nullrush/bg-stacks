namespace BgStacks.Web.Infrastructure.Events;

public interface IGameStatsRepository
{
    Task UpsertAsync(GameStatsDocument doc, CancellationToken ct = default);
    Task<GameStatsDocument?> GetAsync(int id, CancellationToken ct = default);
}
