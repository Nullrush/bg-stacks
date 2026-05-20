// src/BggSdk/Parsing/GeeklistParser.cs
using System.Xml.Linq;
using BggSdk.Models;

namespace BggSdk.Parsing;

internal static class GeeklistParser
{
    public static Geeklist Parse(string xml)
    {
        var root = XDocument.Parse(xml).Root!;

        var items = root.Elements("item")
            .Select(e => new GeeklistItem(
                ItemId:     (int)e.Attribute("id")!,
                ObjectId:   (int)e.Attribute("objectid")!,
                ObjectName: (string)e.Attribute("objectname")!,
                Subtype:    (string)e.Attribute("subtype")!,
                Body:       ((string?)e.Element("body"))?.Trim() ?? string.Empty
            ))
            .ToList();

        return new Geeklist(
            Id:            (int)root.Attribute("id")!,
            Title:         (string)root.Element("title")!,
            Username:      (string)root.Element("username")!,
            EditTimestamp: (long)root.Element("editdate_timestamp")!,
            ItemCount:     (int)root.Element("numitems")!,
            Items:         items
        );
    }
}
