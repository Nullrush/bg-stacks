using System.Net;
using BgStacks.Web.Domain.Events;
using BgStacks.Web.Tests.Presentation.Helpers;
using FluentAssertions;

namespace BgStacks.Web.Tests.Presentation.Events;

[Collection("integration")]
public class GamesJsonEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public GamesJsonEndpointTests(CustomWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task GetGamesJson_NumericSlug_ReturnsGamesJson()
    {
        // Numeric slug "12345" resolves directly as geeklist ID 12345 (no Cosmos Event needed)
        _factory.BggGeeklistService.Seed(12345, new EventData
        {
            SlugValue = "12345",
            Title = "Test List",
            EditTimestamp = 0,
            GamesJson = "[{\"id\":1}]",
            MechanicsJson = "[]",
            CategoriesJson = "[]",
        });

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Host = "12345.bgstacks.com";

        var response = await client.GetAsync("/games.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("[{\"id\":1}]");
    }

    [Fact]
    public async Task GetGamesJson_NonNumericSlug_NoCosmosEvent_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Host = "unknown-event.bgstacks.com";

        var response = await client.GetAsync("/games.json");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetGamesJson_RootDomain_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Host = "bgstacks.com";

        var response = await client.GetAsync("/games.json");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetGamesJson_SlugWithCosmosEvent_ReturnsGamesJson()
    {
        // Named slug maps to geeklist ID via Cosmos Event
        var slug = EventSlug.From("gw-2026-pnw");
        _factory.EventRepository.Seed(new Event(slug, "Geekway 2026 PnW",
            DateOnly.Parse("2026-05-22"), isPublic: true, geeklistId: 99999));
        _factory.BggGeeklistService.Seed(99999, new EventData
        {
            SlugValue = "gw-2026-pnw",
            Title = "Geekway 2026 PnW",
            EditTimestamp = 0,
            GamesJson = "[{\"id\":42}]",
            MechanicsJson = "[]",
            CategoriesJson = "[]",
        });

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Host = "gw-2026-pnw.bgstacks.com";

        var response = await client.GetAsync("/games.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("[{\"id\":42}]");
    }
}
