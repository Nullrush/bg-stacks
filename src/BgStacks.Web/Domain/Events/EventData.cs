using System.Text.Json.Serialization;

namespace BgStacks.Web.Domain.Events;

public sealed class EventData
{
    [JsonPropertyName("slug")]
    public string SlugValue { get; init; } = "";

    [JsonPropertyName("title")]
    public string Title { get; init; } = "";

    [JsonPropertyName("editTimestamp")]
    public long EditTimestamp { get; init; }

    [JsonPropertyName("gamesJson")]
    public string GamesJson { get; init; } = "";

    [JsonPropertyName("mechanicsJson")]
    public string MechanicsJson { get; init; } = "";

    [JsonPropertyName("categoriesJson")]
    public string CategoriesJson { get; init; } = "";

    [JsonIgnore]
    public EventSlug Slug => EventSlug.From(SlugValue);
}
