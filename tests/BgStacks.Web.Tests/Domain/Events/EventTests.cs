using BgStacks.Web.Domain.Events;
using FluentAssertions;

namespace BgStacks.Web.Tests.Domain.Events;

public class EventTests
{
    private static readonly EventSlug Slug = EventSlug.From("gw-2026-pnw");

    [Fact]
    public void IsUpcoming_EventDateTodayOrFuture_ReturnsTrue()
    {
        var today = new DateOnly(2026, 6, 1);
        var ev = new Event(Slug, "Test Event", today, true);
        ev.IsUpcoming(today).Should().BeTrue();
        ev.IsUpcoming(today.AddDays(-1)).Should().BeTrue();
    }

    [Fact]
    public void IsUpcoming_EventDateInPast_ReturnsFalse()
    {
        var today = new DateOnly(2026, 6, 1);
        var ev = new Event(Slug, "Test Event", new DateOnly(2026, 5, 1), true);
        ev.IsUpcoming(today).Should().BeFalse();
    }

    [Fact]
    public void Constructor_EmptyName_Throws()
    {
        var act = () => new Event(Slug, "", new DateOnly(2026, 6, 1), true);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("42")]
    [InlineData("12345")]
    [InlineData("0")]
    public void Constructor_NumericSlug_Throws(string numericSlug)
    {
        var slug = EventSlug.From(numericSlug);
        var act = () => new Event(slug, "Some Event", new DateOnly(2026, 6, 1), true);
        act.Should().Throw<ArgumentException>().WithMessage("*numeric*");
    }
}
