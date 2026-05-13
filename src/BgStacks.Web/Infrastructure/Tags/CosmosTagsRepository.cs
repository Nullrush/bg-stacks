using System.Net;
using System.Text.Json.Serialization;
using DomainTags = BgStacks.Web.Domain.Tags;
using Microsoft.Azure.Cosmos;

namespace BgStacks.Web.Infrastructure.Tags;

public sealed class CosmosTagsRepository : DomainTags.ITagsRepository
{
    private readonly Container _container;

    public CosmosTagsRepository(CosmosClient client, string databaseId)
        => _container = client.GetContainer(databaseId, "usertags");

    public async Task<(DomainTags.Tags? tags, string? etag)> GetAsync(DomainTags.UserId userId, CancellationToken ct = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<TagsDocument>(
                "tags", new PartitionKey(userId.Value), cancellationToken: ct);
            var doc = response.Resource;
            return (new DomainTags.Tags(doc.Want, doc.Played), response.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return (null, null);
        }
    }

    public async Task<string> SaveAsync(DomainTags.UserTags userTags, string? clientEtag, CancellationToken ct = default)
    {
        var doc = new TagsDocument
        {
            Id = "tags",
            UserId = userTags.UserId.Value,
            Want = userTags.Tags.Want,
            Played = userTags.Tags.Played,
        };

        try
        {
            ItemResponse<TagsDocument> response;
            if (clientEtag is null)
            {
                response = await _container.CreateItemAsync(
                    doc, new PartitionKey(userTags.UserId.Value), cancellationToken: ct);
            }
            else
            {
                response = await _container.ReplaceItemAsync(
                    doc, "tags", new PartitionKey(userTags.UserId.Value),
                    new ItemRequestOptions { IfMatchEtag = clientEtag },
                    cancellationToken: ct);
            }
            return response.ETag;
        }
        catch (CosmosException ex) when (
            ex.StatusCode == HttpStatusCode.PreconditionFailed ||
            ex.StatusCode == HttpStatusCode.Conflict)
        {
            throw new DomainTags.ConflictException();
        }
    }

    private sealed class TagsDocument
    {
        [JsonPropertyName("id")]     public string Id { get; set; } = "tags";
        [JsonPropertyName("userId")] public string UserId { get; set; } = "";
        [JsonPropertyName("want")]   public int[] Want { get; set; } = [];
        [JsonPropertyName("played")] public int[] Played { get; set; } = [];
    }
}
