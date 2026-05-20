namespace BggSdk.Models;

public sealed record Geeklist(
    int Id,
    string Title,
    string Username,
    long EditTimestamp,
    int ItemCount,
    IReadOnlyList<GeeklistItem> Items
);

public sealed record GeeklistItem(
    int ItemId,
    int ObjectId,
    string ObjectName,
    string Subtype,
    string Body
);
