using BgStacks.Web.Application.Events;
using BgStacks.Web.Domain.Events;
using FluentAssertions;
using NSubstitute;

namespace BgStacks.Web.Tests.Application.Events;

public class EventServiceTests
{
    private readonly IEventRepository _repo = Substitute.For<IEventRepository>();
    private readonly EventService _sut;

    public EventServiceTests() => _sut = new EventService(_repo);

    [Fact]
    public async Task ListPublicEventsAsync_DelegatesToRepository()
    {
        var events = new List<Event>
        {
            new(EventSlug.From("gw-2026-pnw"), "GW 2026", new DateOnly(2026, 6, 1), true)
        };
        _repo.ListPublicAsync().Returns(events);

        var result = await _sut.ListPublicEventsAsync();

        result.Should().BeEquivalentTo(events);
    }

    [Fact]
    public async Task GetEventAsync_DelegatesToRepository()
    {
        var slug = EventSlug.From("gw-2026-pnw");
        var ev = new Event(slug, "GW 2026", new DateOnly(2026, 6, 1), true);
        _repo.GetAsync(slug).Returns(ev);

        var result = await _sut.GetEventAsync(slug);

        result.Should().Be(ev);
    }
}
