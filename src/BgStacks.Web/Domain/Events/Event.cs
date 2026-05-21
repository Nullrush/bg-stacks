namespace BgStacks.Web.Domain.Events;

public sealed class Event
{
    public EventSlug Slug { get; }
    public string Name { get; }
    public DateOnly EventDate { get; }
    public bool IsPublic { get; }
    public int? GeeklistId { get; }

    // Use for deserialization / rehydration from storage — does not enforce write-time invariants.
    public Event(EventSlug slug, string name, DateOnly eventDate, bool isPublic, int? geeklistId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Slug = slug;
        Name = name;
        EventDate = eventDate;
        IsPublic = isPublic;
        GeeklistId = geeklistId;
    }

    // Use when creating a new event. Numeric slugs are reserved for direct geeklist ID routing.
    public static Event Create(EventSlug slug, string name, DateOnly eventDate, bool isPublic, int? geeklistId = null)
    {
        if (int.TryParse(slug.Value, out _))
            throw new ArgumentException("Event slug must not be purely numeric — numeric subdomains are reserved for direct geeklist ID lookups.", nameof(slug));
        return new Event(slug, name, eventDate, isPublic, geeklistId);
    }

    public bool IsUpcoming(DateOnly today) => EventDate >= today;
}
