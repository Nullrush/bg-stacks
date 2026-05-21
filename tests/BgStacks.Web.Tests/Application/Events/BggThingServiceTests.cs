using System.Net;
using BggSdk;
using BgStacks.Web.Application.Events;
using BgStacks.Web.Infrastructure.Events;
using BgStacks.Web.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BgStacks.Web.Tests.Application.Events;

public class BggThingServiceTests
{
    private const string ThingXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <items termsofuse="">
            <item type="boardgame" id="42">
                <name type="primary" sortindex="1" value="Test Game" />
                <thumbnail>https://example.com/thumb.jpg</thumbnail>
                <minplayers value="2" />
                <maxplayers value="4" />
                <playingtime value="90" />
                <minplaytime value="60" />
                <maxplaytime value="90" />
                <statistics page="1">
                    <ratings>
                        <average value="8.5" />
                        <bayesaverage value="7.9" />
                        <usersrated value="1000" />
                        <averageweight value="3.2" />
                        <ranks>
                            <rank type="subtype" id="1" name="boardgame" friendlyname="Board Game Rank"
                                  value="15" bayesaverage="7.9" />
                            <rank type="family" id="5497" name="strategygames"
                                  friendlyname="Strategy Game Rank" value="5" bayesaverage="7.9" />
                        </ranks>
                    </ratings>
                </statistics>
                <poll name="suggested_numplayers" totalvotes="100">
                    <results numplayers="2">
                        <result value="Best" numvotes="10" />
                        <result value="Recommended" numvotes="60" />
                        <result value="Not Recommended" numvotes="30" />
                    </results>
                    <results numplayers="3">
                        <result value="Best" numvotes="80" />
                        <result value="Recommended" numvotes="15" />
                        <result value="Not Recommended" numvotes="5" />
                    </results>
                    <results numplayers="4">
                        <result value="Best" numvotes="5" />
                        <result value="Recommended" numvotes="20" />
                        <result value="Not Recommended" numvotes="75" />
                    </results>
                </poll>
                <link type="boardgamemechanic" id="100" value="Worker Placement" />
                <link type="boardgamecategory" id="200" value="Strategy" />
            </item>
        </items>
        """;

    private static BggClient MakeBggClient(TestHttpMessageHandler handler)
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://boardgamegeek.com/xmlapi2/")
        };
        return new BggClient(http, retryDelay: TimeSpan.Zero);
    }

    private static HttpResponseMessage Ok(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static BggThingService MakeSut(TestHttpMessageHandler handler,
        IGameDetailsRepository detailsRepo, IGameStatsRepository statsRepo)
        => new(MakeBggClient(handler), detailsRepo, statsRepo,
            Substitute.For<ILogger<BggThingService>>());

    // ── EnsureThingsAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task EnsureThingsAsync_NewId_FetchesFromBggAndUpsertsBoth()
    {
        var detailsRepo = Substitute.For<IGameDetailsRepository>();
        var statsRepo = Substitute.For<IGameStatsRepository>();
        detailsRepo.GetExistingIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<int>());
        statsRepo.GetExistingIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<int>());

        var handler = new TestHttpMessageHandler(Ok(ThingXml));
        var sut = MakeSut(handler, detailsRepo, statsRepo);

        await sut.EnsureThingsAsync([42]);

        handler.RequestCount.Should().Be(1);
        await detailsRepo.Received(1).UpsertAsync(
            Arg.Is<GameDetailsDocument>(d => d.Id == "42"),
            Arg.Any<CancellationToken>());
        await statsRepo.Received(1).UpsertAsync(
            Arg.Is<GameStatsDocument>(s => s.Id == "42"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureThingsAsync_ExistingId_SkipsFetch()
    {
        var detailsRepo = Substitute.For<IGameDetailsRepository>();
        var statsRepo = Substitute.For<IGameStatsRepository>();
        detailsRepo.GetExistingIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<int> { 42 });
        statsRepo.GetExistingIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<int> { 42 });

        var handler = new TestHttpMessageHandler();
        var sut = MakeSut(handler, detailsRepo, statsRepo);

        await sut.EnsureThingsAsync([42]);

        handler.RequestCount.Should().Be(0);
        await detailsRepo.DidNotReceive().UpsertAsync(
            Arg.Any<GameDetailsDocument>(), Arg.Any<CancellationToken>());
        await statsRepo.DidNotReceive().UpsertAsync(
            Arg.Any<GameStatsDocument>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureThingsAsync_DetailsMissingStatExists_UpsertDetailsOnly()
    {
        var detailsRepo = Substitute.For<IGameDetailsRepository>();
        var statsRepo = Substitute.For<IGameStatsRepository>();
        detailsRepo.GetExistingIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<int>());
        statsRepo.GetExistingIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<int> { 42 });

        var handler = new TestHttpMessageHandler(Ok(ThingXml));
        var sut = MakeSut(handler, detailsRepo, statsRepo);

        await sut.EnsureThingsAsync([42]);

        handler.RequestCount.Should().Be(1);
        await detailsRepo.Received(1).UpsertAsync(
            Arg.Is<GameDetailsDocument>(d => d.Id == "42"),
            Arg.Any<CancellationToken>());
        await statsRepo.DidNotReceive().UpsertAsync(
            Arg.Any<GameStatsDocument>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureThingsAsync_DetailsExistStatsMissing_UpsertStatsOnly()
    {
        var detailsRepo = Substitute.For<IGameDetailsRepository>();
        var statsRepo = Substitute.For<IGameStatsRepository>();
        detailsRepo.GetExistingIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<int> { 42 });
        statsRepo.GetExistingIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<int>());

        var handler = new TestHttpMessageHandler(Ok(ThingXml));
        var sut = MakeSut(handler, detailsRepo, statsRepo);

        await sut.EnsureThingsAsync([42]);

        handler.RequestCount.Should().Be(1);
        await detailsRepo.DidNotReceive().UpsertAsync(
            Arg.Any<GameDetailsDocument>(), Arg.Any<CancellationToken>());
        await statsRepo.Received(1).UpsertAsync(
            Arg.Is<GameStatsDocument>(s => s.Id == "42"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureThingsAsync_BggReturnsPartialResults_ProcessesWhatWasReturned()
    {
        var detailsRepo = Substitute.For<IGameDetailsRepository>();
        var statsRepo = Substitute.For<IGameStatsRepository>();
        detailsRepo.GetExistingIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<int>());
        statsRepo.GetExistingIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<int>());

        // BGG returns only one item even though two were requested
        var handler = new TestHttpMessageHandler(Ok(ThingXml));
        var sut = MakeSut(handler, detailsRepo, statsRepo);

        await sut.EnsureThingsAsync([42, 99]);

        // Only id=42 was in the response; id=99 is silently skipped
        await detailsRepo.Received(1).UpsertAsync(
            Arg.Is<GameDetailsDocument>(d => d.Id == "42"),
            Arg.Any<CancellationToken>());
        await detailsRepo.DidNotReceive().UpsertAsync(
            Arg.Is<GameDetailsDocument>(d => d.Id == "99"),
            Arg.Any<CancellationToken>());
    }

    // ── GetGameEntriesAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetGameEntriesAsync_AssemblesFromCosmosAndGeeklistItems()
    {
        var detailsRepo = Substitute.For<IGameDetailsRepository>();
        var statsRepo = Substitute.For<IGameStatsRepository>();

        var detail = new GameDetailsDocument
        {
            Id = "42",
            Name = "Test Game",
            MinPlayers = 2,
            MaxPlayers = 4,
            MinTime = 60,
            MaxTime = 90,
            Mechanics = ["Worker Placement"],
            Categories = ["Strategy"],
            Thumbnail = "https://example.com/thumb.jpg",
        };
        var stat = new GameStatsDocument
        {
            Id = "42",
            AvgRating = 8.5,
            GeekRating = 7.9,
            Votes = 1000,
            Weight = 3.2,
            BggRank = 15,
            SubRanks = new Dictionary<string, int?> { ["strategygames"] = 5 },
            BestPlayers = [3],
            RecommendedPlayers = [2, 3],
        };

        detailsRepo.GetManyAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, GameDetailsDocument> { [42] = detail });
        statsRepo.GetManyAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, GameStatsDocument> { [42] = stat });

        var handler = new TestHttpMessageHandler();
        var sut = MakeSut(handler, detailsRepo, statsRepo);

        var entries = await sut.GetGameEntriesAsync([(42, "A great game about searching for ET.")]);

        entries.Should().HaveCount(1);
        var entry = entries[0];
        entry.Id.Should().Be(42);
        entry.Name.Should().Be("Test Game");
        entry.Players.Should().Be("2-4");
        entry.Time.Should().Be("60-90");
        entry.AvgRating.Should().Be(8.5);
        entry.Body.Should().Be("A great game about searching for ET.");
    }

    [Fact]
    public async Task GetGameEntriesAsync_MissingStats_UsesDefaultStatValues()
    {
        var detailsRepo = Substitute.For<IGameDetailsRepository>();
        var statsRepo = Substitute.For<IGameStatsRepository>();

        var detail = new GameDetailsDocument
        {
            Id = "42",
            Name = "Test Game",
            MinPlayers = 2,
            MaxPlayers = 4,
            MinTime = 60,
            MaxTime = 90,
        };

        detailsRepo.GetManyAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, GameDetailsDocument> { [42] = detail });
        statsRepo.GetManyAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, GameStatsDocument>());

        var handler = new TestHttpMessageHandler();
        var sut = MakeSut(handler, detailsRepo, statsRepo);

        var entries = await sut.GetGameEntriesAsync([(42, "body text")]);

        entries.Should().HaveCount(1);
        var entry = entries[0];
        entry.AvgRating.Should().Be(0);
        entry.GeekRating.Should().Be(0);
        entry.Votes.Should().Be(0);
        entry.Weight.Should().Be(0);
        entry.BggRank.Should().BeNull();
        entry.BestPlayers.Should().BeEmpty();
        entry.RecommendedPlayers.Should().BeEmpty();
    }
}
