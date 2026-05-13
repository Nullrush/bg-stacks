using DomainTags = BgStacks.Web.Domain.Tags;

namespace BgStacks.Web.Application.Tags;

public sealed class TagsService
{
    private readonly DomainTags.ITagsRepository _repository;

    public TagsService(DomainTags.ITagsRepository repository) => _repository = repository;

    public Task<(DomainTags.Tags? tags, string? etag)> GetTagsAsync(DomainTags.UserId userId, CancellationToken ct = default)
        => _repository.GetAsync(userId, ct);

    public Task<string> SaveTagsAsync(DomainTags.UserId userId, DomainTags.Tags tags, string? clientEtag, CancellationToken ct = default)
        => _repository.SaveAsync(new DomainTags.UserTags(userId, tags), clientEtag, ct);
}
