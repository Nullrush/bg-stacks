namespace BgStacks.Web.Domain.Events;

public sealed class Event
{
    public EventSlug Slug { get; }
    public string Name { get; }
    public DateOnly EventDate { get; }
    public bool IsPublic { get; }

    public Event(EventSlug slug, string name, DateOnly eventDate, bool isPublic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Slug = slug;
        Name = name;
        EventDate = eventDate;
        IsPublic = isPublic;
    }

    public bool IsUpcoming(DateOnly today) => EventDate >= today;
}
