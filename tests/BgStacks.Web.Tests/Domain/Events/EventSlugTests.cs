using BgStacks.Web.Domain.Events;
using FluentAssertions;

namespace BgStacks.Web.Tests.Domain.Events;

public class EventSlugTests
{
    [Theory]
    [InlineData("gw-2026-pnw")]
    [InlineData("geekwaywest2026")]
    [InlineData("ab")]
    public void From_ValidSlug_Succeeds(string value)
    {
        var slug = EventSlug.From(value);
        slug.Value.Should().Be(value);
    }

    [Fact]
    public void From_UpperCase_NormalizesToLower()
    {
        var slug = EventSlug.From("GW-2026-PNW");
        slug.Value.Should().Be("gw-2026-pnw");
    }

    [Theory]
    [InlineData("")]
    [InlineData("-startswithdash")]
    [InlineData("endswith-")]
    [InlineData("has spaces")]
    [InlineData("has!special")]
    public void From_InvalidSlug_Throws(string value)
    {
        var act = () => EventSlug.From(value);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryFrom_ValidSlug_ReturnsTrueAndSlug()
    {
        var result = EventSlug.TryFrom("gw-2026-pnw", out var slug);
        result.Should().BeTrue();
        slug.Value.Should().Be("gw-2026-pnw");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-bad")]
    public void TryFrom_InvalidSlug_ReturnsFalse(string? value)
    {
        var result = EventSlug.TryFrom(value, out _);
        result.Should().BeFalse();
    }
}
