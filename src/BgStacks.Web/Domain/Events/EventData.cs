namespace BgStacks.Web.Domain.Events;

public sealed class EventData
{
    public EventSlug Slug { get; }
    public string GamesJson { get; }
    public string MechanicsJson { get; }
    public string CategoriesJson { get; }

    public EventData(EventSlug slug, string gamesJson, string mechanicsJson, string categoriesJson)
    {
        Slug = slug;
        GamesJson = gamesJson;
        MechanicsJson = mechanicsJson;
        CategoriesJson = categoriesJson;
    }
}
