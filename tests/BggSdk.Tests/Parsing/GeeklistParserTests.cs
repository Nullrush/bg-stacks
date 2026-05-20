// tests/BggSdk.Tests/Parsing/GeeklistParserTests.cs
using BggSdk.Parsing;

namespace BggSdk.Tests.Parsing;

public class GeeklistParserTests
{
    private const string SampleXml = """
        <geeklist id="358871" termsofuse="https://boardgamegeek.com/xmlapi/termsofuse">
            <postdate>Wed, 04 Jun 2025 22:59:42 +0000</postdate>
            <postdate_timestamp>1749077982</postdate_timestamp>
            <editdate>Fri, 11 Jul 2025 23:43:50 +0000</editdate>
            <editdate_timestamp>1752277430</editdate_timestamp>
            <thumbs>28</thumbs>
            <numitems>142</numitems>
            <username>valthalion</username>
            <title>Geekway 2026 Prime PnW Lineup</title>
            <description>Some description</description>
            <item id="11852169"
                  objecttype="thing"
                  subtype="boardgame"
                  objectid="424573"
                  objectname="Pergola"
                  username="valthalion"
                  postdate="Wed, 04 Jun 2025 22:58:25 +0000"
                  editdate="Wed, 04 Jun 2025 22:58:25 +0000"
                  thumbs="9"
                  imageid="0">
                <body>Provided by Geekway</body>
            </item>
            <item id="11962184"
                  objecttype="thing"
                  subtype="boardgame"
                  objectid="402024"
                  objectname="Conservas"
                  username="valthalion"
                  postdate="Sun, 20 Jul 2025 02:54:35 +0000"
                  editdate="Sun, 20 Jul 2025 02:54:35 +0000"
                  thumbs="14"
                  imageid="0">
                <body>Provided by Geekway

        First time ever for a solo game.</body>
            </item>
        </geeklist>
        """;

    [Fact]
    public void Parse_ExtractsGeeklistMetadata()
    {
        var result = GeeklistParser.Parse(SampleXml);

        result.Id.Should().Be(358871);
        result.Title.Should().Be("Geekway 2026 Prime PnW Lineup");
        result.Username.Should().Be("valthalion");
        result.EditTimestamp.Should().Be(1752277430L);
        result.ItemCount.Should().Be(142);
    }

    [Fact]
    public void Parse_ExtractsItems()
    {
        var result = GeeklistParser.Parse(SampleXml);

        result.Items.Should().HaveCount(2);
        result.Items[0].ItemId.Should().Be(11852169);
        result.Items[0].ObjectId.Should().Be(424573);
        result.Items[0].ObjectName.Should().Be("Pergola");
        result.Items[0].Subtype.Should().Be("boardgame");
    }

    [Fact]
    public void Parse_ExtractsSingleLineBody()
    {
        var result = GeeklistParser.Parse(SampleXml);

        result.Items[0].Body.Should().Be("Provided by Geekway");
    }

    [Fact]
    public void Parse_ExtractsMultiLineBody()
    {
        var result = GeeklistParser.Parse(SampleXml);

        result.Items[1].Body.Should().StartWith("Provided by Geekway");
        result.Items[1].Body.Should().Contain("First time ever for a solo game.");
    }

    [Fact]
    public void Parse_MissingBody_ReturnsEmptyString()
    {
        const string xml = """
            <geeklist id="1" termsofuse="...">
                <editdate_timestamp>0</editdate_timestamp>
                <numitems>1</numitems>
                <username>user</username>
                <title>Test</title>
                <item id="1" objecttype="thing" subtype="boardgame"
                      objectid="100" objectname="Game" username="user"
                      postdate="" editdate="" thumbs="0" imageid="0" />
            </geeklist>
            """;

        var result = GeeklistParser.Parse(xml);

        result.Items[0].Body.Should().BeEmpty();
    }
}
