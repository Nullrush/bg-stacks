using BgStacks.Web.Application.Events;
using BgStacks.Web.Domain.Events;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace BgStacks.Web.Tests.Application.Events;

public class EventDataServiceTests
{
    private readonly IEventDataRepository _repo = Substitute.For<IEventDataRepository>();
    private readonly EventDataService _sut;
    private static readonly EventSlug Slug = EventSlug.From("gw-2026-pnw");
    private static readonly EventData Data = new(Slug, "{}", "[]", "[]");

    public EventDataServiceTests()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        _sut = new EventDataService(_repo, cache);
    }

    [Fact]
    public async Task GetEventDataAsync_CacheMiss_FetchesFromRepo()
    {
        _repo.GetAsync(Slug).Returns(Data);

        var result = await _sut.GetEventDataAsync(Slug);

        result.Should().Be(Data);
        await _repo.Received(1).GetAsync(Slug);
    }

    [Fact]
    public async Task GetEventDataAsync_CacheHit_DoesNotCallRepo()
    {
        _repo.GetAsync(Slug).Returns(Data);
        await _sut.GetEventDataAsync(Slug);

        await _sut.GetEventDataAsync(Slug);

        await _repo.Received(1).GetAsync(Slug);
    }

    [Fact]
    public async Task GetEventDataAsync_NotFound_ReturnsNull()
    {
        _repo.GetAsync(Slug).Returns((EventData?)null);

        var result = await _sut.GetEventDataAsync(Slug);

        result.Should().BeNull();
    }
}
