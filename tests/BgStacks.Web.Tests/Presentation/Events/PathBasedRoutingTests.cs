using System.Net;
using BgStacks.Web.Application.Events;
using BgStacks.Web.Domain.Events;
using BgStacks.Web.Tests.Presentation.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BgStacks.Web.Tests.Presentation.Events;

[Collection("integration")]
public class PathBasedRoutingTests : IClassFixture<CustomWebApplicationFactory>, IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly WebApplicationFactory<Program> _pathBasedFactory;

    public PathBasedRoutingTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _pathBasedFactory = factory.WithWebHostBuilder(b => b.UseSetting("Events:PathBasedRouting", "true"));
    }

    public async ValueTask DisposeAsync() => await _pathBasedFactory.DisposeAsync();

    private HttpClient PathBasedClient() => _pathBasedFactory.CreateClient();

    // ── happy-path data ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GamesJson_ValidSlug_KnownEvent_Returns200()
    {
        _factory.EventRepository.Seed(new Event(
            EventSlug.From("pb-games-test"), "PB Games Test",
            DateOnly.Parse("2026-06-01"), isPublic: true, geeklistId: 7001));
        _factory.BggGeeklistService.Seed(7001, new EventData
        {
            SlugValue = "pb-games-test", Title = "PB Games Test", EditTimestamp = 0,
            GamesJson = "[{\"id\":7}]", MechanicsJson = "[]", CategoriesJson = "[]",
        });

        var response = await PathBasedClient().GetAsync("/event/pb-games-test/games.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("[{\"id\":7}]");
    }

    [Fact]
    public async Task MechanicsJson_ValidSlug_KnownEvent_Returns200()
    {
        _factory.EventRepository.Seed(new Event(
            EventSlug.From("pb-mech-test"), "PB Mech Test",
            DateOnly.Parse("2026-06-01"), isPublic: true, geeklistId: 7002));
        _factory.BggGeeklistService.Seed(7002, new EventData
        {
            SlugValue = "pb-mech-test", Title = "PB Mech Test", EditTimestamp = 0,
            GamesJson = "[]", MechanicsJson = "[\"Deck Building\"]", CategoriesJson = "[]",
        });

        var response = await PathBasedClient().GetAsync("/event/pb-mech-test/mechanics.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("[\"Deck Building\"]");
    }

    [Fact]
    public async Task CategoriesJson_ValidSlug_KnownEvent_Returns200()
    {
        _factory.EventRepository.Seed(new Event(
            EventSlug.From("pb-cat-test"), "PB Cat Test",
            DateOnly.Parse("2026-06-01"), isPublic: true, geeklistId: 7003));
        _factory.BggGeeklistService.Seed(7003, new EventData
        {
            SlugValue = "pb-cat-test", Title = "PB Cat Test", EditTimestamp = 0,
            GamesJson = "[]", MechanicsJson = "[]", CategoriesJson = "[\"Strategy\"]",
        });

        var response = await PathBasedClient().GetAsync("/event/pb-cat-test/categories.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("[\"Strategy\"]");
    }

    // ── validation / not-found cases ────────────────────────────────────────────────────

    [Fact]
    public async Task InvalidSlugFormat_Returns404()
    {
        var response = await PathBasedClient().GetAsync("/event/NOT_VALID!/games.json");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ValidSlugFormat_UnknownEvent_Returns404()
    {
        // slug passes format check but has no entry in Cosmos (event repo)
        var response = await PathBasedClient().GetAsync("/event/totally-unknown-slug/games.json");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── feature-flag off ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PathBasedRoutingDisabled_EventRouteNotRegistered_Returns404()
    {
        // default client — PathBasedRouting not set → route group not registered
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/event/pb-games-test/games.json");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── coexistence with existing subdomain routes ────────────────────────────────────────

    [Fact]
    public async Task PathBasedEnabled_SubdomainRouteStillWorks()
    {
        _factory.EventRepository.Seed(new Event(
            EventSlug.From("pb-coexist-test"), "PB Coexist Test",
            DateOnly.Parse("2026-06-01"), isPublic: true, geeklistId: 7004));
        _factory.BggGeeklistService.Seed(7004, new EventData
        {
            SlugValue = "pb-coexist-test", Title = "PB Coexist Test", EditTimestamp = 0,
            GamesJson = "[{\"id\":99}]", MechanicsJson = "[]", CategoriesJson = "[]",
        });

        var client = PathBasedClient();
        client.DefaultRequestHeaders.Host = "pb-coexist-test.bgstacks.com";

        var response = await client.GetAsync("/games.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
