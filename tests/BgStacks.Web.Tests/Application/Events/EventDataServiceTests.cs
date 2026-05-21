using System.Text.Json;
using BgStacks.Web.Application.Events;
using BgStacks.Web.Domain.Events;
using FluentAssertions;
using NSubstitute;

namespace BgStacks.Web.Tests.Application.Events;

public class EventDataSerializationTests
{
    [Fact]
    public void EventData_RoundTrips_ViaSystemTextJson()
    {
        var original = new EventData
        {
            SlugValue = "gw-2026-pnw",
            Title = "Geekway 2026 PnW",
            EditTimestamp = 1700000000L,
            GamesJson = "[{\"id\":1}]",
            MechanicsJson = "[\"Worker Placement\"]",
            CategoriesJson = "[\"Strategy\"]",
        };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<EventData>(json);

        restored!.SlugValue.Should().Be("gw-2026-pnw");
        restored.Title.Should().Be("Geekway 2026 PnW");
        restored.Slug.Value.Should().Be("gw-2026-pnw");
    }
}

public class EventDataServiceTests
{
    private static readonly EventSlug NamedSlug = EventSlug.From("gw-2026-pnw");
    private static readonly EventSlug NumericSlug = EventSlug.From("12345");

    private static EventData MakeData(EventSlug slug) => new()
    {
        SlugValue = slug.Value, Title = "Test", EditTimestamp = 0,
        GamesJson = "[]", MechanicsJson = "[]", CategoriesJson = "[]",
    };

    [Fact]
    public async Task GetEventDataAsync_SlugHasCosmosEventWithGeeklistId_DelegatesToGeeklistService()
    {
        var eventRepo = Substitute.For<IEventRepository>();
        var geeklistService = Substitute.For<IBggGeeklistService>();
        var @event = new Event(NamedSlug, "Geekway 2026 PnW",
            DateOnly.Parse("2026-05-22"), isPublic: true, geeklistId: 99999);
        eventRepo.GetAsync(NamedSlug).Returns(@event);
        var expectedData = MakeData(NamedSlug);
        geeklistService.GetEventDataAsync(99999, NamedSlug, Arg.Any<CancellationToken>()).Returns(expectedData);

        var sut = new EventDataService(eventRepo, geeklistService);

        var result = await sut.GetEventDataAsync(NamedSlug);

        result.Should().Be(expectedData);
        await geeklistService.Received(1).GetEventDataAsync(99999, NamedSlug, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetEventDataAsync_NumericSlug_CallsGeeklistDirectlyWithoutCosmosLookup()
    {
        var eventRepo = Substitute.For<IEventRepository>();
        var geeklistService = Substitute.For<IBggGeeklistService>();
        var expectedData = MakeData(NumericSlug);
        geeklistService.GetEventDataAsync(12345, NumericSlug, Arg.Any<CancellationToken>()).Returns(expectedData);

        var sut = new EventDataService(eventRepo, geeklistService);

        var result = await sut.GetEventDataAsync(NumericSlug);

        result.Should().Be(expectedData);
        await geeklistService.Received(1).GetEventDataAsync(12345, NumericSlug, Arg.Any<CancellationToken>());
        await eventRepo.DidNotReceive().GetAsync(Arg.Any<EventSlug>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetEventDataAsync_NamedSlug_NoCosmosEvent_ReturnsNull()
    {
        var eventRepo = Substitute.For<IEventRepository>();
        var geeklistService = Substitute.For<IBggGeeklistService>();
        eventRepo.GetAsync(NamedSlug).Returns((Event?)null);

        var sut = new EventDataService(eventRepo, geeklistService);

        var result = await sut.GetEventDataAsync(NamedSlug);

        result.Should().BeNull();
        await geeklistService.DidNotReceive()
            .GetEventDataAsync(Arg.Any<int>(), Arg.Any<EventSlug>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetEventDataAsync_NamedSlug_CosmosEventWithNullGeeklistId_ReturnsNull()
    {
        var eventRepo = Substitute.For<IEventRepository>();
        var geeklistService = Substitute.For<IBggGeeklistService>();
        var @event = new Event(NamedSlug, "Some Event",
            DateOnly.Parse("2026-01-01"), isPublic: true, geeklistId: null);
        eventRepo.GetAsync(NamedSlug).Returns(@event);

        var sut = new EventDataService(eventRepo, geeklistService);

        var result = await sut.GetEventDataAsync(NamedSlug);

        result.Should().BeNull();
        await geeklistService.DidNotReceive()
            .GetEventDataAsync(Arg.Any<int>(), Arg.Any<EventSlug>(), Arg.Any<CancellationToken>());
    }
}
