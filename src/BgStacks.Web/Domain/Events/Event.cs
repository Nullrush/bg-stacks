namespace BgStacks.Web.Domain.Events;

public sealed class Event
{
    public EventSlug Slug { get; }
    public string Name { get; }
    public DateOnly EventDate { get; }
    public bool IsPublic { get; }
    public int? GeeklistId { get; }

    public Event(EventSlug slug, string name, DateOnly eventDate, bool isPublic, int? geeklistId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (int.TryParse(slug.Value, out _))
            throw new ArgumentException("Event slug must not be purely numeric — numeric subdomains are reserved for direct geeklist ID lookups.", nameof(slug));
        Slug = slug;
        Name = name;
        EventDate = eventDate;
        IsPublic = isPublic;
        GeeklistId = geeklistId;
    }

    public bool IsUpcoming(DateOnly today) => EventDate >= today;
}
