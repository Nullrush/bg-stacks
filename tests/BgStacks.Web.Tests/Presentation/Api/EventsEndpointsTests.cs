using System.Net;
using System.Text.Json;
using BgStacks.Web.Domain.Events;
using BgStacks.Web.Tests.Presentation.Helpers;
using FluentAssertions;

namespace BgStacks.Web.Tests.Presentation.Api;

[Collection("integration")]
public class EventsEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public EventsEndpointsTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetEvents_ReturnsPublicEventsOnly()
    {
        _factory.EventRepository.Seed(new Event(
            EventSlug.From("gw-2026-pnw"), "Geekway 2026 PnW",
            new DateOnly(2026, 6, 12), isPublic: true));
        _factory.EventRepository.Seed(new Event(
            EventSlug.From("private-event"), "Private Event",
            new DateOnly(2026, 7, 1), isPublic: false));

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/events");
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var events = doc.RootElement.EnumerateArray().ToList();
        events.Should().HaveCount(1);
        events[0].GetProperty("slug").GetString().Should().Be("gw-2026-pnw");
    }

    [Fact]
    public async Task GetEvents_SetsIsUpcomingCorrectly()
    {
        var pastDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10);
        _factory.EventRepository.Seed(new Event(
            EventSlug.From("past-event"), "Past Event", pastDate, isPublic: true));

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/events");
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var ev = doc.RootElement.EnumerateArray().First(e =>
            e.GetProperty("slug").GetString() == "past-event");
        ev.GetProperty("isUpcoming").GetBoolean().Should().BeFalse();
    }
}
