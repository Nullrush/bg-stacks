// tests/BggSdk.Tests/Parsing/ThingParserTests.cs
using BggSdk.Parsing;

namespace BggSdk.Tests.Parsing;

public class ThingParserTests
{
    // Full thing XML with stats=1 response structure.
    // Poll data chosen to produce:
    //   BestPlayerCounts = [3]
    //   RecommendedPlayerCounts = [2, 3, 4]
    // Rationale:
    //   1 player: NR wins (90) → skip both lists
    //   2 players: Rec wins (80) → recommended only
    //   3 players: Best wins (100) → best + recommended
    //   4 players: Rec wins (70) → recommended only
    //   "4+" is non-integer → skipped entirely
    private const string SingleThingXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <items termsofuse="https://boardgamegeek.com/xmlapi/termsofuse">
            <item type="boardgame" id="418059">
                <thumbnail>https://cf.geekdo-images.com/example.jpg</thumbnail>
                <name type="primary" sortindex="1" value="SETI: Search for Extraterrestrial Intelligence" />
                <name type="alternate" sortindex="1" value="SETI alternate name" />
                <minplayers value="1" />
                <maxplayers value="4" />
                <playingtime value="160" />
                <minplaytime value="40" />
                <maxplaytime value="160" />
                <poll name="suggested_numplayers" title="User Suggested Number of Players" totalvotes="150">
                    <results numplayers="1">
                        <result value="Best" numvotes="10" />
                        <result value="Recommended" numvotes="50" />
                        <result value="Not Recommended" numvotes="90" />
                    </results>
                    <results numplayers="2">
                        <result value="Best" numvotes="20" />
                        <result value="Recommended" numvotes="80" />
                        <result value="Not Recommended" numvotes="50" />
                    </results>
                    <results numplayers="3">
                        <result value="Best" numvotes="100" />
                        <result value="Recommended" numvotes="40" />
                        <result value="Not Recommended" numvotes="10" />
                    </results>
                    <results numplayers="4">
                        <result value="Best" numvotes="60" />
                        <result value="Recommended" numvotes="70" />
                        <result value="Not Recommended" numvotes="20" />
                    </results>
                    <results numplayers="4+">
                        <result value="Best" numvotes="0" />
                        <result value="Recommended" numvotes="1" />
                        <result value="Not Recommended" numvotes="45" />
                    </results>
                </poll>
                <statistics>
                    <ratings>
                        <usersrated value="19245" />
                        <average value="8.42" />
                        <bayesaverage value="8.03" />
                        <ranks>
                            <rank type="subtype" id="1" name="boardgame"
                                  friendlyname="Board Game Rank" value="12" bayesaverage="8.03" />
                            <rank type="family" id="5497" name="strategygames"
                                  friendlyname="Strategy Game Rank" value="5" bayesaverage="8.03" />
                            <rank type="family" id="5499" name="thematic"
                                  friendlyname="Thematic Rank" value="Not Ranked" bayesaverage="Not Ranked" />
                        </ranks>
                        <averageweight value="3.83" />
                    </ratings>
                </statistics>
                <link type="boardgamecategory" id="1071" value="Science Fiction" />
                <link type="boardgamemechanic" id="2040" value="Worker Placement" />
                <link type="boardgamemechanic" id="2041" value="Hand Management" />
                <link type="boardgamefamily" id="123" value="Some Family" />
            </item>
        </items>
        """;

    [Fact]
    public void Parse_ExtractsId()
    {
        var result = ThingParser.Parse(SingleThingXml);
        result[0].Id.Should().Be(418059);
    }

    [Fact]
    public void Parse_ExtractsPrimaryName_IgnoresAlternates()
    {
        var result = ThingParser.Parse(SingleThingXml);
        result[0].Name.Should().Be("SETI: Search for Extraterrestrial Intelligence");
    }

    [Fact]
    public void Parse_ExtractsThumbnail()
    {
        var result = ThingParser.Parse(SingleThingXml);
        result[0].Thumbnail.Should().Be("https://cf.geekdo-images.com/example.jpg");
    }

    [Fact]
    public void Parse_MissingThumbnail_ReturnsNull()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <items termsofuse="...">
                <item type="boardgame" id="1">
                    <name type="primary" sortindex="1" value="Game" />
                    <minplayers value="1" /><maxplayers value="2" />
                    <playingtime value="30" /><minplaytime value="30" /><maxplaytime value="30" />
                </item>
            </items>
            """;

        var result = ThingParser.Parse(xml);
        result[0].Thumbnail.Should().BeNull();
    }

    [Fact]
    public void Parse_ExtractsPlayerCounts()
    {
        var result = ThingParser.Parse(SingleThingXml);
        result[0].MinPlayers.Should().Be(1);
        result[0].MaxPlayers.Should().Be(4);
    }

    [Fact]
    public void Parse_ExtractsPlayTimes()
    {
        var result = ThingParser.Parse(SingleThingXml);
        result[0].PlayingTime.Should().Be(160);
        result[0].MinPlayTime.Should().Be(40);
        result[0].MaxPlayTime.Should().Be(160);
    }

    [Fact]
    public void Parse_ExtractsRatings()
    {
        var result = ThingParser.Parse(SingleThingXml);
        result[0].AverageRating.Should().BeApproximately(8.42, 0.001);
        result[0].BayesRating.Should().BeApproximately(8.03, 0.001);
        result[0].UserRatings.Should().Be(19245);
        result[0].AverageWeight.Should().BeApproximately(3.83, 0.001);
    }

    [Fact]
    public void Parse_ExtractsBggRank()
    {
        var result = ThingParser.Parse(SingleThingXml);
        result[0].BggRank.Should().Be(12);
    }

    [Fact]
    public void Parse_ExtractsSubRanks_RankedValueIsInt()
    {
        var result = ThingParser.Parse(SingleThingXml);
        result[0].SubRanks["strategygames"].Should().Be(5);
    }

    [Fact]
    public void Parse_ExtractsSubRanks_NotRankedIsNull()
    {
        var result = ThingParser.Parse(SingleThingXml);
        result[0].SubRanks["thematic"].Should().BeNull();
    }

    [Fact]
    public void Parse_ExtractsMechanics()
    {
        var result = ThingParser.Parse(SingleThingXml);
        result[0].Mechanics.Should().BeEquivalentTo(new[] { "Worker Placement", "Hand Management" });
    }

    [Fact]
    public void Parse_ExtractsCategories_ExcludesOtherLinkTypes()
    {
        var result = ThingParser.Parse(SingleThingXml);
        result[0].Categories.Should().ContainSingle().Which.Should().Be("Science Fiction");
        result[0].Categories.Should().NotContain("Some Family");
    }

    [Fact]
    public void Parse_PlayerPoll_BestWinner_AppearsInBothLists()
    {
        var result = ThingParser.Parse(SingleThingXml);
        result[0].BestPlayerCounts.Should().Contain(3);
        result[0].RecommendedPlayerCounts.Should().Contain(3);
    }

    [Fact]
    public void Parse_PlayerPoll_RecommendedWinner_AppearsOnlyInRecommended()
    {
        var result = ThingParser.Parse(SingleThingXml);
        result[0].RecommendedPlayerCounts.Should().Contain(2);
        result[0].BestPlayerCounts.Should().NotContain(2);
        result[0].RecommendedPlayerCounts.Should().Contain(4);
        result[0].BestPlayerCounts.Should().NotContain(4);
    }

    [Fact]
    public void Parse_PlayerPoll_NotRecommendedWinner_AbsentFromBothLists()
    {
        var result = ThingParser.Parse(SingleThingXml);
        result[0].BestPlayerCounts.Should().NotContain(1);
        result[0].RecommendedPlayerCounts.Should().NotContain(1);
    }

    [Fact]
    public void Parse_PlayerPoll_SkipsNonIntegerPlayerCounts()
    {
        // "4+" appears in the XML — it must not cause an exception or appear in results
        var result = ThingParser.Parse(SingleThingXml);
        result[0].RecommendedPlayerCounts.Should().NotContain(5);
        result[0].BestPlayerCounts.Should().BeEquivalentTo(new[] { 3 });
        result[0].RecommendedPlayerCounts.Should().BeEquivalentTo(new[] { 2, 3, 4 });
    }

    [Fact]
    public void Parse_ReturnsMultipleThings()
    {
        const string multiXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <items termsofuse="...">
                <item type="boardgame" id="1">
                    <name type="primary" sortindex="1" value="Game A" />
                    <minplayers value="2" /><maxplayers value="4" />
                    <playingtime value="60" /><minplaytime value="60" /><maxplaytime value="60" />
                </item>
                <item type="boardgame" id="2">
                    <name type="primary" sortindex="1" value="Game B" />
                    <minplayers value="1" /><maxplayers value="6" />
                    <playingtime value="90" /><minplaytime value="90" /><maxplaytime value="90" />
                </item>
            </items>
            """;

        var result = ThingParser.Parse(multiXml);

        result.Should().HaveCount(2);
        result[0].Id.Should().Be(1);
        result[0].Name.Should().Be("Game A");
        result[1].Id.Should().Be(2);
        result[1].Name.Should().Be("Game B");
    }
}
