// src/BggSdk/Parsing/ThingParser.cs
using System.Collections.ObjectModel;
using System.Globalization;
using System.Xml.Linq;
using BggSdk.Exceptions;
using BggSdk.Models;

namespace BggSdk.Parsing;

internal static class ThingParser
{
    public static IReadOnlyList<Thing> Parse(string xml)
    {
        try
        {
            return XDocument.Parse(xml).Root!
                .Elements("item")
                .Select(ParseItem)
                .ToList();
        }
        catch (Exception ex) when (ex is not BggApiException)
        {
            throw new BggApiException($"Failed to parse thing XML: {ex.Message}", ex);
        }
    }

    private static Thing ParseItem(XElement e)
    {
        var name = e.Elements("name")
            .First(n => n.Attribute("type")!.Value == "primary")
            .Attribute("value")!.Value;

        var mechanics = e.Elements("link")
            .Where(l => l.Attribute("type")!.Value == "boardgamemechanic")
            .Select(l => l.Attribute("value")!.Value)
            .ToList().AsReadOnly();

        var categories = e.Elements("link")
            .Where(l => l.Attribute("type")!.Value == "boardgamecategory")
            .Select(l => l.Attribute("value")!.Value)
            .ToList().AsReadOnly();

        var (avgRating, bayesRating, userRatings, avgWeight, bggRank, subRanks)
            = ParseStats(e.Element("statistics")?.Element("ratings"));

        var (bestPlayers, recommendedPlayers) = ParsePlayerPoll(e);

        return new Thing(
            Id:                     (int)e.Attribute("id")!,
            Name:                   name,
            Thumbnail:              (string?)e.Element("thumbnail"),
            YearPublished:          NullableIntAttr(e.Element("yearpublished"), "value"),
            MinPlayers:             IntAttr(e.Element("minplayers"), "value"),
            MaxPlayers:             IntAttr(e.Element("maxplayers"), "value"),
            PlayingTime:            IntAttr(e.Element("playingtime"), "value"),
            MinPlayTime:            IntAttr(e.Element("minplaytime"), "value"),
            MaxPlayTime:            IntAttr(e.Element("maxplaytime"), "value"),
            AverageRating:          avgRating,
            BayesRating:            bayesRating,
            UserRatings:            userRatings,
            AverageWeight:          avgWeight,
            BggRank:                bggRank,
            SubRanks:               subRanks,
            Mechanics:              mechanics,
            Categories:             categories,
            BestPlayerCounts:       bestPlayers,
            RecommendedPlayerCounts: recommendedPlayers
        );
    }

    // BGG XML uses verbose rank names; normalize to the compact keys the UI expects.
    // Keys not listed here (thematic, wargames, cgs) already match the UI schema.
    private static readonly Dictionary<string, string> SubRankKeyMap = new()
    {
        ["abstractgames"]  = "abstracts",
        ["childrensgames"] = "childrens",
        ["familygames"]    = "family",
        ["partygames"]     = "party",
        ["strategygames"]  = "strategy",
    };

    private static (double avg, double bayes, int users, double weight, int? bggRank,
                    IReadOnlyDictionary<string, int?> subRanks)
        ParseStats(XElement? ratings)
    {
        if (ratings is null)
            return (0, 0, 0, 0, null, new ReadOnlyDictionary<string, int?>(new Dictionary<string, int?>()));

        int? bggRank = null;
        var subRanks = new Dictionary<string, int?>();

        foreach (var rank in ratings.Element("ranks")?.Elements("rank") ?? [])
        {
            var type  = (string?)rank.Attribute("type");
            var rname = (string?)rank.Attribute("name") ?? "";
            var val   = int.TryParse((string?)rank.Attribute("value"), out var rv) ? rv : (int?)null;

            if (type == "subtype" && rname == "boardgame")
                bggRank = val;
            else if (type == "family")
                subRanks[SubRankKeyMap.TryGetValue(rname, out var mapped) ? mapped : rname] = val;
        }

        return (
            DoubleAttr(ratings.Element("average"),       "value"),
            DoubleAttr(ratings.Element("bayesaverage"),  "value"),
            IntAttr   (ratings.Element("usersrated"),    "value"),
            DoubleAttr(ratings.Element("averageweight"), "value"),
            bggRank,
            new ReadOnlyDictionary<string, int?>(subRanks)
        );
    }

    private static (IReadOnlyList<int> best, IReadOnlyList<int> recommended)
        ParsePlayerPoll(XElement item)
    {
        var poll = item.Elements("poll")
            .FirstOrDefault(p => (string?)p.Attribute("name") == "suggested_numplayers");

        if (poll is null) return ([], []);

        var best        = new List<int>();
        var recommended = new List<int>();

        foreach (var results in poll.Elements("results"))
        {
            if (!int.TryParse((string?)results.Attribute("numplayers"), out var n))
                continue;

            int bestVotes   = 0, recVotes = 0, notRecVotes = 0;
            foreach (var result in results.Elements("result"))
            {
                var votes = IntAttr(result, "numvotes");
                switch ((string?)result.Attribute("value"))
                {
                    case "Best":            bestVotes   = votes; break;
                    case "Recommended":     recVotes    = votes; break;
                    case "Not Recommended": notRecVotes = votes; break;
                }
            }

            if (bestVotes > recVotes && bestVotes > notRecVotes && bestVotes > 0)
            {
                best.Add(n);
                recommended.Add(n);
            }
            else if (recVotes > bestVotes && recVotes > notRecVotes && recVotes > 0)
            {
                recommended.Add(n);
            }
        }

        return (best.AsReadOnly(), recommended.AsReadOnly());
    }

    private static int IntAttr(XElement? el, string attr)
        => el is not null && int.TryParse((string?)el.Attribute(attr), out var v) ? v : 0;

    private static int? NullableIntAttr(XElement? el, string attr)
        => el is not null && int.TryParse((string?)el.Attribute(attr), out var v) ? v : (int?)null;

    private static double DoubleAttr(XElement? el, string attr)
        => el is not null && double.TryParse(
            (string?)el.Attribute(attr),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0.0;
}
