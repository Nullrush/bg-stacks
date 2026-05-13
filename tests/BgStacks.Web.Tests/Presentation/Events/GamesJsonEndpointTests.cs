using System.Net;
using BgStacks.Web.Domain.Events;
using BgStacks.Web.Tests.Presentation.Helpers;
using FluentAssertions;

namespace BgStacks.Web.Tests.Presentation.Events;

public class GamesJsonEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public GamesJsonEndpointTests(CustomWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task GetGamesJson_KnownEventSlug_ReturnsJson()
    {
        var slug = EventSlug.From("gw-2026-pnw");
        _factory.EventDataRepository.Seed(
            new EventData(slug, "[{\"id\":1}]", "[]", "[]"));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Host = "gw-2026-pnw.bgstacks.com";

        var response = await client.GetAsync("/games.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("[{\"id\":1}]");
    }

    [Fact]
    public async Task GetGamesJson_RootDomain_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Host = "bgstacks.com";

        var response = await client.GetAsync("/games.json");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
