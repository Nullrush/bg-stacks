using System.Text.RegularExpressions;

namespace BgStacks.Web.Domain.Events;

public readonly record struct EventSlug
{
    private static readonly Regex ValidPattern =
        new(@"^[a-z0-9]([a-z0-9\-]*[a-z0-9])?$", RegexOptions.Compiled);

    public string Value { get; }

    private EventSlug(string value) => Value = value;

    public static EventSlug From(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var lower = value.ToLowerInvariant();
        if (!ValidPattern.IsMatch(lower))
            throw new ArgumentException($"Invalid event slug: '{value}'", nameof(value));
        return new EventSlug(lower);
    }

    public static bool TryFrom(string? value, out EventSlug slug)
    {
        slug = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var lower = value.ToLowerInvariant();
        if (!ValidPattern.IsMatch(lower)) return false;
        slug = new EventSlug(lower);
        return true;
    }

    public override string ToString() => Value;
}
