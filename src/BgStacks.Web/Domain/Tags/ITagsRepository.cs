namespace BgStacks.Web.Domain.Tags;

public interface ITagsRepository
{
    Task<(Tags? tags, string? etag)> GetAsync(UserId userId, CancellationToken ct = default);
    Task<string> SaveAsync(UserTags userTags, string? clientEtag, CancellationToken ct = default);
}
