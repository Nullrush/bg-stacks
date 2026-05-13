using BgStacks.Web.Application.Tags;
using DomainTags = BgStacks.Web.Domain.Tags;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BgStacks.Web.Tests.Application.Tags;

public class TagsServiceTests
{
    private readonly DomainTags.ITagsRepository _repo = Substitute.For<DomainTags.ITagsRepository>();
    private readonly TagsService _sut;
    private readonly DomainTags.UserId _userId = DomainTags.UserId.From("google", "user1");

    public TagsServiceTests() => _sut = new TagsService(_repo);

    [Fact]
    public async Task GetTagsAsync_RepositoryReturnsData_ReturnsIt()
    {
        var tags = new DomainTags.Tags([1, 2], [3]);
        _repo.GetAsync(_userId).Returns((tags, "etag123"));

        var (result, etag) = await _sut.GetTagsAsync(_userId);

        result.Should().Be(tags);
        etag.Should().Be("etag123");
    }

    [Fact]
    public async Task GetTagsAsync_NoData_ReturnsNull()
    {
        _repo.GetAsync(_userId).Returns(((DomainTags.Tags?)null, (string?)null));

        var (result, etag) = await _sut.GetTagsAsync(_userId);

        result.Should().BeNull();
        etag.Should().BeNull();
    }

    [Fact]
    public async Task SaveTagsAsync_DelegatesToRepository()
    {
        var tags = new DomainTags.Tags([1], [2]);
        _repo.SaveAsync(Arg.Any<DomainTags.UserTags>(), "etag1").Returns("etag2");

        var result = await _sut.SaveTagsAsync(_userId, tags, "etag1");

        result.Should().Be("etag2");
        await _repo.Received(1).SaveAsync(
            Arg.Is<DomainTags.UserTags>(ut => ut.UserId == _userId && ut.Tags == tags), "etag1");
    }

    [Fact]
    public async Task SaveTagsAsync_ConflictException_Propagates()
    {
        _repo.SaveAsync(Arg.Any<DomainTags.UserTags>(), Arg.Any<string?>()).ThrowsAsync(new DomainTags.ConflictException());

        var act = async () => await _sut.SaveTagsAsync(_userId, DomainTags.Tags.Empty, "stale");

        await act.Should().ThrowAsync<DomainTags.ConflictException>();
    }
}
