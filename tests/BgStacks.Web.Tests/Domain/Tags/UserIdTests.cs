using BgStacks.Web.Domain.Tags;
using FluentAssertions;

namespace BgStacks.Web.Tests.Domain.Tags;

public class UserIdTests
{
    [Fact]
    public void From_SameProviderAndSub_ReturnsSameValue()
    {
        var a = UserId.From("google", "12345");
        var b = UserId.From("google", "12345");
        a.Value.Should().Be(b.Value);
    }

    [Fact]
    public void From_DifferentProviders_ReturnsDifferentValues()
    {
        var a = UserId.From("google", "12345");
        var b = UserId.From("facebook", "12345");
        a.Value.Should().NotBe(b.Value);
    }

    [Fact]
    public void From_ProducesLowercaseHex64Chars()
    {
        var id = UserId.From("google", "abc");
        id.Value.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]+$");
    }

    [Theory]
    [InlineData("", "abc")]
    [InlineData("google", "")]
    [InlineData(null, "abc")]
    [InlineData("google", null)]
    public void From_EmptyOrNull_Throws(string? provider, string? sub)
    {
        var act = () => UserId.From(provider!, sub!);
        act.Should().Throw<ArgumentException>();
    }
}
