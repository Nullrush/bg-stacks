// tests/BggSdk.Tests/BggClientTests.cs
using System.Net;
using BggSdk.Exceptions;
using BggSdk.Tests.Helpers;

namespace BggSdk.Tests;

public class BggClientTests
{
    private const string MinimalGeeklistXml = """
        <geeklist id="999" termsofuse="...">
            <editdate_timestamp>1000</editdate_timestamp>
            <numitems>0</numitems>
            <username>user</username>
            <title>My List</title>
        </geeklist>
        """;

    private const string MinimalThingXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <items termsofuse="...">
            <item type="boardgame" id="42">
                <name type="primary" sortindex="1" value="Test Game" />
                <minplayers value="2" /><maxplayers value="4" />
                <playingtime value="60" /><minplaytime value="60" /><maxplaytime value="60" />
            </item>
        </items>
        """;

    private static BggClient MakeClient(TestHttpMessageHandler handler, TimeSpan? retryDelay = null)
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://boardgamegeek.com/xmlapi2/")
        };
        return new BggClient(http, retryDelay ?? TimeSpan.Zero);
    }

    private static HttpResponseMessage Ok(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    // ── Geeklist ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetGeeklistAsync_OkResponse_ReturnsGeeklist()
    {
        var handler = new TestHttpMessageHandler(Ok(MinimalGeeklistXml));

        var result = await MakeClient(handler).GetGeeklistAsync(999);

        result.Id.Should().Be(999);
        result.Title.Should().Be("My List");
    }

    [Fact]
    public async Task GetGeeklistAsync_RequestsCorrectPath()
    {
        var handler = new TestHttpMessageHandler(Ok(MinimalGeeklistXml));

        await MakeClient(handler).GetGeeklistAsync(358871);

        handler.RequestedPaths[0].Should().Contain("geeklist/358871");
    }

    [Fact]
    public async Task GetGeeklistAsync_429_ThrowsRateLimitException()
    {
        var handler = new TestHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        await FluentActions
            .Awaiting(() => MakeClient(handler).GetGeeklistAsync(1))
            .Should().ThrowAsync<BggRateLimitException>();
    }

    [Fact]
    public async Task GetGeeklistAsync_202ThenOk_RetriesAndReturnsResult()
    {
        var handler = new TestHttpMessageHandler(
            new HttpResponseMessage((HttpStatusCode)202),
            Ok(MinimalGeeklistXml));

        var result = await MakeClient(handler).GetGeeklistAsync(1);

        result.Id.Should().Be(999);
        handler.RequestCount.Should().Be(2);
    }

    [Fact]
    public async Task GetGeeklistAsync_Repeated202_ThrowsRetryException()
    {
        var responses = Enumerable.Range(0, 5)
            .Select(_ => new HttpResponseMessage((HttpStatusCode)202))
            .ToArray();
        var handler = new TestHttpMessageHandler(responses);

        await FluentActions
            .Awaiting(() => MakeClient(handler).GetGeeklistAsync(1))
            .Should().ThrowAsync<BggRetryException>();

        handler.RequestCount.Should().Be(5);
    }

    // ── Thing ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetThingsAsync_OkResponse_ReturnsThings()
    {
        var handler = new TestHttpMessageHandler(Ok(MinimalThingXml));

        var result = await MakeClient(handler).GetThingsAsync([42]);

        result.Should().ContainSingle().Which.Id.Should().Be(42);
    }

    [Fact]
    public async Task GetThingsAsync_IncludesStatsParam()
    {
        var handler = new TestHttpMessageHandler(Ok(MinimalThingXml));

        await MakeClient(handler).GetThingsAsync([1]);

        handler.RequestedPaths[0].Should().Contain("stats=1");
    }

    [Fact]
    public async Task GetThingsAsync_FewerThan20Ids_SingleRequest()
    {
        var handler = new TestHttpMessageHandler(Ok(MinimalThingXml));

        await MakeClient(handler).GetThingsAsync([1, 2, 3]);

        handler.RequestCount.Should().Be(1);
        handler.RequestedPaths[0].Should().Contain("1,2,3");
    }

    [Fact]
    public async Task GetThingsAsync_ExactlyTwentyIds_SingleRequest()
    {
        var handler = new TestHttpMessageHandler(Ok(MinimalThingXml));

        await MakeClient(handler).GetThingsAsync(Enumerable.Range(1, 20));

        handler.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task GetThingsAsync_TwentyOneIds_TwoRequests()
    {
        var handler = new TestHttpMessageHandler(
            Ok(MinimalThingXml),
            Ok(MinimalThingXml));

        await MakeClient(handler).GetThingsAsync(Enumerable.Range(1, 21));

        handler.RequestCount.Should().Be(2);
    }

    [Fact]
    public async Task GetThingsAsync_TwentyOneIds_SecondChunkHasOneId()
    {
        var handler = new TestHttpMessageHandler(
            Ok(MinimalThingXml),
            Ok(MinimalThingXml));

        await MakeClient(handler).GetThingsAsync(Enumerable.Range(1, 21));

        // First path should have 20 comma-separated ids (19 commas)
        handler.RequestedPaths[0].Count(c => c == ',').Should().Be(19);
        // Second path should have 1 id (0 commas in the id list)
        handler.RequestedPaths[1].Should().Contain("id=21");
    }
}
