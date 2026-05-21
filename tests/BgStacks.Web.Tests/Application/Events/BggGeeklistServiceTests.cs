using System.Net;
using System.Text.Json;
using BggSdk;
using BgStacks.Web.Application.Events;
using BgStacks.Web.Domain.Events;
using BgStacks.Web.Infrastructure.Events;
using BgStacks.Web.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ZiggyCreatures.Caching.Fusion;

namespace BgStacks.Web.Tests.Application.Events;

public class BggGeeklistServiceTests
{
    private static readonly EventSlug TestSlug = EventSlug.From("12345");

    private static string MakeGeeklistXml(long ts, int objectId = 42) => $"""
        <geeklist id="12345" termsofuse="">
            <editdate_timestamp>{ts}</editdate_timestamp>
            <numitems>1</numitems>
            <username>testuser</username>
            <title>Test Geeklist</title>
            <item id="1" objecttype="thing" objectid="{objectId}" objectname="Test Game"
                  subtype="boardgame">
                <body>Donated by someone</body>
            </item>
        </geeklist>
        """;

    private static IFusionCache MakeInMemoryCache()
    {
        var services = new ServiceCollection();
        services.AddFusionCache();
        return services.BuildServiceProvider().GetRequiredService<IFusionCache>();
    }

    private static BggClient MakeBggClient(TestHttpMessageHandler handler)
    {
        var http = new HttpClient(handler)
            { BaseAddress = new Uri("https://boardgamegeek.com/xmlapi2/") };
        return new BggClient(http, retryDelay: TimeSpan.Zero);
    }

    private static GameEntry MakeGameEntry(int id) => new()
    {
        Id = id, Name = "Test Game", Players = "2-4", MinPlayers = 2, MaxPlayers = 4,
        Time = "60-90", MinTime = 60, MaxTime = 90,
        AvgRating = 8.5, GeekRating = 7.9, Votes = 1000, Weight = 3.2, BggRank = 15,
        SubRanks = new Dictionary<string, int?>(), Mechanics = ["Worker Placement"],
        Categories = ["Strategy"], BestPlayers = [], RecommendedPlayers = [],
        Body = "Donated by someone",
    };

    [Fact]
    public async Task GetEventDataAsync_FirstCall_FetchesGeeklistAndAssembles()
    {
        var handler = new TestHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(MakeGeeklistXml(1000)) });
        var bgg = MakeBggClient(handler);
        var thingService = Substitute.For<IBggThingService>();
        thingService.GetGameEntriesAsync(Arg.Any<IReadOnlyList<(int, string)>>(), Arg.Any<CancellationToken>())
            .Returns([MakeGameEntry(42)]);
        var cache = MakeInMemoryCache();
        var sut = new BggGeeklistService(bgg, thingService, cache, checkIntervalMinutes: 30);

        var result = await sut.GetEventDataAsync(12345, TestSlug);

        result.Should().NotBeNull();
        result!.Title.Should().Be("Test Geeklist");
        result.EditTimestamp.Should().Be(1000L);
        result.SlugValue.Should().Be("12345");
        var games = JsonSerializer.Deserialize<JsonElement[]>(result.GamesJson)!;
        games.Should().HaveCount(1);
        games[0].GetProperty("id").GetInt32().Should().Be(42);
        await thingService.Received(1).EnsureThingsAsync(
            Arg.Is<IReadOnlyList<int>>(ids => ids.Contains(42)), Arg.Any<CancellationToken>());
        result.MechanicsJson.Should().Contain("Worker Placement");
        result.CategoriesJson.Should().Contain("Strategy");
    }

    [Fact]
    public async Task GetEventDataAsync_SecondCallWithinTtl_DoesNotHitBgg()
    {
        var handler = new TestHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(MakeGeeklistXml(1000)) });
        var bgg = MakeBggClient(handler);
        var thingService = Substitute.For<IBggThingService>();
        thingService.GetGameEntriesAsync(Arg.Any<IReadOnlyList<(int, string)>>(), Arg.Any<CancellationToken>())
            .Returns([MakeGameEntry(42)]);
        var cache = MakeInMemoryCache();
        var sut = new BggGeeklistService(bgg, thingService, cache, checkIntervalMinutes: 30);

        await sut.GetEventDataAsync(12345, TestSlug);
        var result = await sut.GetEventDataAsync(12345, TestSlug);

        handler.RequestCount.Should().Be(1);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetEventDataAsync_BggApiException_ReturnsNull()
    {
        var handler = new TestHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.NotFound));
        var bgg = MakeBggClient(handler);
        var thingService = Substitute.For<IBggThingService>();
        var cache = MakeInMemoryCache();
        var sut = new BggGeeklistService(bgg, thingService, cache, checkIntervalMinutes: 30);

        var result = await sut.GetEventDataAsync(12345, TestSlug);

        result.Should().BeNull();
        await thingService.DidNotReceive().EnsureThingsAsync(
            Arg.Any<IReadOnlyList<int>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetEventDataAsync_BggApiException_SecondCallHitsCache()
    {
        var handler = new TestHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.NotFound),
            new HttpResponseMessage(HttpStatusCode.NotFound));
        var bgg = MakeBggClient(handler);
        var thingService = Substitute.For<IBggThingService>();
        var cache = MakeInMemoryCache();
        var sut = new BggGeeklistService(bgg, thingService, cache, checkIntervalMinutes: 30);

        await sut.GetEventDataAsync(12345, TestSlug);
        await sut.GetEventDataAsync(12345, TestSlug);

        handler.RequestCount.Should().Be(1);
    }
}
