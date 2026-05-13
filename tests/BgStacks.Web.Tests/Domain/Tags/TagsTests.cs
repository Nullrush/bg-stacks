using DomainTags = BgStacks.Web.Domain.Tags;
using FluentAssertions;

namespace BgStacks.Web.Tests.Domain.Tags;

public class TagsTests
{
    [Fact]
    public void Empty_HasNoWantOrPlayed()
    {
        var tags = DomainTags.Tags.Empty;
        tags.Want.Should().BeEmpty();
        tags.Played.Should().BeEmpty();
    }

    [Fact]
    public void Tags_StoresWantAndPlayed()
    {
        var tags = new DomainTags.Tags([1, 2], [3]);
        tags.Want.Should().BeEquivalentTo(new[] { 1, 2 });
        tags.Played.Should().BeEquivalentTo(new[] { 3 });
    }
}
