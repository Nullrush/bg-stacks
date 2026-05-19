using System.Net;
using System.Text.Json.Serialization;
using BgStacks.Web.Domain.Events;
using Microsoft.Azure.Cosmos;

namespace BgStacks.Web.Infrastructure.Events;

public sealed class CosmosEventRepository : IEventRepository
{
    private readonly Container _container;

    public CosmosEventRepository(CosmosClient client, string databaseId)
        => _container = client.GetContainer(databaseId, "events");

    public async Task<Event?> GetAsync(EventSlug slug, CancellationToken ct = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<EventDocument>(
                slug.Value, new PartitionKey(slug.Value), cancellationToken: ct);
            return ToEvent(response.Resource);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<Event>> ListPublicAsync(CancellationToken ct = default)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.isPublic = true ORDER BY c.eventDate DESC");
        using var feed = _container.GetItemQueryIterator<EventDocument>(query);
        var results = new List<Event>();
        while (feed.HasMoreResults)
        {
            var page = await feed.ReadNextAsync(ct);
            results.AddRange(page.Select(ToEvent));
        }
        return results;
    }

    public async Task SaveAsync(Event @event, CancellationToken ct = default)
    {
        var doc = new EventDocument
        {
            Id = @event.Slug.Value,
            Slug = @event.Slug.Value,
            Name = @event.Name,
            EventDate = @event.EventDate.ToString("yyyy-MM-dd"),
            IsPublic = @event.IsPublic,
            GeeklistId = @event.GeeklistId,
        };
        await _container.UpsertItemAsync(doc, new PartitionKey(@event.Slug.Value), cancellationToken: ct);
    }

    private static Event ToEvent(EventDocument doc) => new(
        EventSlug.From(doc.Slug),
        doc.Name,
        DateOnly.Parse(doc.EventDate),
        doc.IsPublic,
        doc.GeeklistId);

    private sealed class EventDocument
    {
        [JsonPropertyName("id")]          public string Id { get; set; } = "";
        [JsonPropertyName("slug")]        public string Slug { get; set; } = "";
        [JsonPropertyName("name")]        public string Name { get; set; } = "";
        [JsonPropertyName("eventDate")]   public string EventDate { get; set; } = "";
        [JsonPropertyName("isPublic")]    public bool IsPublic { get; set; }
        [JsonPropertyName("geeklistId")] public int? GeeklistId { get; set; }
    }
}
