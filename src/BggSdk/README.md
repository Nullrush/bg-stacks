# BggSdk

A C# client for the [BoardGameGeek XML API v2](https://boardgamegeek.com/wiki/page/BGG_XML_API2).

Supports the **Geeklist** and **Thing** endpoints with:

- Strongly-typed record models
- Automatic batching of Thing requests (BGG limit: 20 IDs per request)
- Transparent retry on HTTP 202 "accepted, try again" responses
- `BggRateLimitException` on HTTP 429
- `AddBggClient()` extension for ASP.NET Core DI / `IHttpClientFactory`
- `BggAuthHandler` DelegatingHandler for manual wiring

## Installation

```shell
dotnet add package BggSdk
```

## Wiring up in ASP.NET Core

The recommended approach uses `AddBggClient()`, which registers `BggClient` as a
typed HTTP client and injects your Bearer token via `BggAuthHandler`:

```csharp
// Program.cs
builder.Services.AddBggClient(
    builder.Configuration["Bgg:BearerToken"] ?? "");
```

Then inject `BggClient` wherever you need it:

```csharp
public class MyService(BggClient bgg)
{
    public async Task<string> GetTitleAsync(int geeklistId)
    {
        var list = await bgg.GetGeeklistAsync(geeklistId);
        return list.Title;
    }
}
```

`AddBggClient` returns an `IHttpClientBuilder`, so you can chain Polly or other
delegating handlers:

```csharp
builder.Services
    .AddBggClient(token)
    .AddPolicyHandler(GetRetryPolicy());
```

## Wiring up without DI

```csharp
using BggSdk;

var handler = new BggAuthHandler("YOUR_BGG_TOKEN")
{
    InnerHandler = new HttpClientHandler()
};
var httpClient = new HttpClient(handler)
{
    BaseAddress = new Uri("https://boardgamegeek.com/xmlapi2/")
};
var client = new BggClient(httpClient);
```

## Usage

```csharp
// Fetch a geeklist
var geeklist = await client.GetGeeklistAsync(358871);
Console.WriteLine($"{geeklist.Title} — {geeklist.Items.Count} items");

// Fetch enriched data for all games (auto-batched into ≤20-ID chunks)
var ids = geeklist.Items.Select(i => i.ObjectId);
var things = await client.GetThingsAsync(ids);
foreach (var thing in things)
    Console.WriteLine($"{thing.Name} — rank {thing.BggRank?.ToString() ?? "unranked"}");
```

## Models

### `Geeklist`

| Property | Type | Notes |
|---|---|---|
| `Id` | `int` | BGG geeklist ID |
| `Title` | `string` | Geeklist title |
| `Username` | `string` | Owner's BGG username |
| `EditTimestamp` | `long` | Unix timestamp of last edit — use for cache invalidation |
| `ItemCount` | `int` | Declared item count (from `<numitems>` element, not `Items.Count`) |
| `Items` | `IReadOnlyList<GeeklistItem>` | |

### `GeeklistItem`

| Property | Type | Notes |
|---|---|---|
| `ItemId` | `int` | BGG item ID (internal to the geeklist) |
| `ObjectId` | `int` | BGG thing ID — use this for `GetThingsAsync` |
| `ObjectName` | `string` | Game name as stored in the geeklist |
| `Subtype` | `string` | e.g. `"boardgame"` |
| `Body` | `string` | Curator notes; empty string if absent |

### `Thing`

| Property | Type | Notes |
|---|---|---|
| `Id` | `int` | BGG thing ID |
| `Name` | `string` | Primary name |
| `Thumbnail` | `string?` | Thumbnail URL; `null` if not available |
| `MinPlayers` / `MaxPlayers` | `int` | Player count range |
| `PlayingTime` / `MinPlayTime` / `MaxPlayTime` | `int` | Minutes |
| `AverageRating` | `double` | Community average rating |
| `BayesRating` | `double` | BGG Geek Rating; `0.0` if not yet issued |
| `UserRatings` | `int` | Number of ratings |
| `AverageWeight` | `double` | Complexity weight (1–5) |
| `BggRank` | `int?` | Overall BGG rank; `null` if unranked |
| `SubRanks` | `IReadOnlyDictionary<string, int?>` | Family rank keyed by BGG name (e.g. `"strategygames"`); `null` value = Not Ranked |
| `Mechanics` | `IReadOnlyList<string>` | |
| `Categories` | `IReadOnlyList<string>` | |
| `BestPlayerCounts` | `IReadOnlyList<int>` | Counts where "Best" vote is plurality winner |
| `RecommendedPlayerCounts` | `IReadOnlyList<int>` | Counts where "Best" or "Recommended" is plurality winner; always a superset of `BestPlayerCounts` |

## Authentication

Register your application at <https://boardgamegeek.com/applications> to receive a Bearer token. Pass it to `BggAuthHandler`.

## License

MIT
