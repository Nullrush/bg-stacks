// src/BggSdk/Parsing/GeeklistParser.cs
using System.Xml.Linq;
using BggSdk.Exceptions;
using BggSdk.Models;

namespace BggSdk.Parsing;

internal static class GeeklistParser
{
    public static Geeklist Parse(string xml)
    {
        try
        {
            var root = XDocument.Parse(xml).Root!;

            var items = root.Elements("item")
                .Select(e => new GeeklistItem(
                    ItemId:     (int)e.Attribute("id")!,
                    ObjectId:   (int)e.Attribute("objectid")!,
                    ObjectName: e.Attribute("objectname")!.Value,
                    Subtype:    e.Attribute("subtype")!.Value,
                    Body:       ((string?)e.Element("body"))?.Trim() ?? string.Empty
                ))
                .ToList().AsReadOnly();

            return new Geeklist(
                Id:            (int)root.Attribute("id")!,
                Title:         root.Element("title")!.Value,
                Username:      root.Element("username")!.Value,
                EditTimestamp: (long)root.Element("editdate_timestamp")!,
                ItemCount:     (int)root.Element("numitems")!,
                Items:         items
            );
        }
        catch (Exception ex) when (ex is not BggApiException)
        {
            throw new BggApiException($"Failed to parse geeklist XML: {ex.Message}", ex);
        }
    }
}
