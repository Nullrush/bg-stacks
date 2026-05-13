using DomainTags = BgStacks.Web.Domain.Tags;

namespace BgStacks.Web.Tests.Presentation.Helpers;

public class InMemoryTagsRepository : DomainTags.ITagsRepository
{
    private readonly Dictionary<string, (DomainTags.Tags tags, string etag)> _store = new();

    public Task<(DomainTags.Tags? tags, string? etag)> GetAsync(DomainTags.UserId userId, CancellationToken ct = default)
    {
        if (_store.TryGetValue(userId.Value, out var entry))
            return Task.FromResult<(DomainTags.Tags?, string?)>((entry.tags, entry.etag));
        return Task.FromResult<(DomainTags.Tags?, string?)>((null, null));
    }

    public Task<string> SaveAsync(DomainTags.UserTags userTags, string? clientEtag, CancellationToken ct = default)
    {
        var key = userTags.UserId.Value;
        if (clientEtag is not null &&
            (!_store.TryGetValue(key, out var existing) || existing.etag != clientEtag))
            throw new DomainTags.ConflictException();

        var newEtag = Guid.NewGuid().ToString("N");
        _store[key] = (userTags.Tags, newEtag);
        return Task.FromResult(newEtag);
    }
}
