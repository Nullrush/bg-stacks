using System.Globalization;
using System.Text.Json.Serialization;
using Azure.Identity;
using Microsoft.Azure.Cosmos;

// ── Args ─────────────────────────────────────────────────────────────────────
string? csvPath = null;
string? connectionString = Environment.GetEnvironmentVariable("AZURE_COSMOS_CONNECTION_STRING")
                        ?? Environment.GetEnvironmentVariable("Cosmos__ConnectionString");
string? endpoint = null;
string databaseId = "bgstacks";
bool dryRun = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--connection-string": connectionString = args[++i]; break;
        case "--endpoint":          endpoint = args[++i]; break;
        case "--database":          databaseId = args[++i]; break;
        case "--dry-run":           dryRun = true; break;
        default:
            if (csvPath is null) csvPath = args[i];
            break;
    }
}

if (csvPath is null)
{
    Console.Error.WriteLine("Usage: dotnet run -- <csv-path> [--connection-string <cs>] [--database <db>] [--dry-run]");
    return 1;
}

// ── Parse CSV ────────────────────────────────────────────────────────────────
Console.WriteLine($"Parsing {csvPath}...");
var rows = ParseCsv(csvPath);
Console.WriteLine($"  {rows.Count:N0} rows");

if (dryRun) Console.WriteLine("  [dry-run mode — no writes will occur]");

// ── Connect ──────────────────────────────────────────────────────────────────
if (connectionString is null && endpoint is null)
{
    if (dryRun)
    {
        Console.WriteLine("\n[dry-run] No connection string — CSV parsed OK, skipping Cosmos delta check.");
        Console.WriteLine("  Provide --connection-string to see what would be updated.");
        return 0;
    }
    Console.Error.WriteLine("Provide --connection-string <cs> or --endpoint <url>, or set AZURE_COSMOS_CONNECTION_STRING");
    return 1;
}

CosmosClient cosmos;
var clientOptions = new CosmosClientOptions
{
    MaxRetryAttemptsOnRateLimitedRequests = 9,
    MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(30),
};
if (connectionString is not null)
    cosmos = new CosmosClient(connectionString, clientOptions);
else
    cosmos = new CosmosClient(endpoint!, new DefaultAzureCredential(), clientOptions);

using (cosmos)
{
    await UpdateStatsAsync(cosmos.GetContainer(databaseId, "game-stats"), rows, dryRun);
    await UpdateDetailsAsync(cosmos.GetContainer(databaseId, "game-details"), rows, dryRun);
}

Console.WriteLine("\nDone.");
return 0;

// ── Phases ────────────────────────────────────────────────────────────────────

static async Task UpdateStatsAsync(Container container, Dictionary<int, CsvRow> rows, bool dryRun)
{
    Console.WriteLine("\nPhase 1: game-stats");
    Console.WriteLine("  Scanning existing documents...");

    var existing = await ScanAsync<SlimStatsDoc>(container, "SELECT c.id, c.votes FROM c");
    Console.WriteLine($"  {existing.Count:N0} documents in Cosmos");

    var toUpdate = rows.Values
        .Where(r => existing.TryGetValue(r.Id, out var s) && r.UsersRated > s.Votes)
        .ToList();
    int alreadyCurrent = rows.Values.Count(r => existing.ContainsKey(r.Id) && existing[r.Id].Votes >= r.UsersRated);
    int notInCosmos = rows.Values.Count(r => !existing.ContainsKey(r.Id));

    Console.WriteLine($"  {toUpdate.Count:N0} to update  |  {alreadyCurrent:N0} already current  |  {notInCosmos:N0} not in Cosmos (skipped)");

    if (dryRun || toUpdate.Count == 0) return;

    Console.Write("  Patching");
    int done = 0;
    await Parallel.ForEachAsync(toUpdate, new ParallelOptions { MaxDegreeOfParallelism = 20 }, async (row, ct) =>
    {
        await container.PatchItemAsync<object>(
            row.Id.ToString(), new PartitionKey(row.Id.ToString()),
            [
                PatchOperation.Set("/avgRating",  row.Average),
                PatchOperation.Set("/geekRating", row.BayesAverage),
                PatchOperation.Set("/votes",      row.UsersRated),
                PatchOperation.Set("/bggRank",    row.Rank),
                PatchOperation.Set("/subRanks",   row.SubRanks),
            ],
            cancellationToken: ct);
        if (Interlocked.Increment(ref done) % 50 == 0) Console.Write('.');
    });
    Console.WriteLine($" {done:N0} patched");
}

static async Task UpdateDetailsAsync(Container container, Dictionary<int, CsvRow> rows, bool dryRun)
{
    Console.WriteLine("\nPhase 2: game-details (yearPublished backfill)");
    Console.WriteLine("  Scanning for missing yearPublished...");

    var existing = await ScanAsync<SlimDetailsDoc>(container, "SELECT c.id, c.yearPublished FROM c");
    Console.WriteLine($"  {existing.Count:N0} documents in Cosmos");

    var toUpdate = rows.Values
        .Where(r => existing.TryGetValue(r.Id, out var d) && d.YearPublished is null && r.YearPublished is not null)
        .ToList();
    Console.WriteLine($"  {toUpdate.Count:N0} missing yearPublished to fill");

    if (dryRun || toUpdate.Count == 0) return;

    Console.Write("  Patching");
    int done = 0;
    await Parallel.ForEachAsync(toUpdate, new ParallelOptions { MaxDegreeOfParallelism = 20 }, async (row, ct) =>
    {
        await container.PatchItemAsync<object>(
            row.Id.ToString(), new PartitionKey(row.Id.ToString()),
            [PatchOperation.Set("/yearPublished", row.YearPublished)],
            cancellationToken: ct);
        if (Interlocked.Increment(ref done) % 50 == 0) Console.Write('.');
    });
    Console.WriteLine($" {done:N0} patched");
}

// ── Helpers ───────────────────────────────────────────────────────────────────

static async Task<Dictionary<int, T>> ScanAsync<T>(Container container, string sql) where T : IHasStringId
{
    var result = new Dictionary<int, T>();
    using var iter = container.GetItemQueryIterator<T>(new QueryDefinition(sql));
    while (iter.HasMoreResults)
    {
        var page = await iter.ReadNextAsync();
        foreach (var doc in page)
            if (int.TryParse(doc.Id, out var id))
                result[id] = doc;
    }
    return result;
}

static Dictionary<int, CsvRow> ParseCsv(string path)
{
    var result = new Dictionary<int, CsvRow>();
    using var reader = new StreamReader(path);

    var header = SplitCsvLine(reader.ReadLine()!);
    var idx = header.Select((h, i) => (h, i)).ToDictionary(x => x.h, x => x.i);

    string? line;
    while ((line = reader.ReadLine()) is not null)
    {
        if (string.IsNullOrWhiteSpace(line)) continue;
        var f = SplitCsvLine(line);
        if (!int.TryParse(f[idx["id"]], out var id)) continue;

        // Only include sub-ranks where the game actually holds that rank (mirrors BGG API behaviour).
        var subRanks = new Dictionary<string, int?>();
        void AddSubRank(string key, string col)
        {
            if (int.TryParse(f[idx[col]], out var v)) subRanks[key] = v;
        }
        AddSubRank("abstracts", "abstracts_rank");
        AddSubRank("cgs",       "cgs_rank");
        AddSubRank("childrens", "childrensgames_rank");
        AddSubRank("family",    "familygames_rank");
        AddSubRank("party",     "partygames_rank");
        AddSubRank("strategy",  "strategygames_rank");
        AddSubRank("thematic",  "thematic_rank");
        AddSubRank("wargames",  "wargames_rank");

        result[id] = new CsvRow(
            Id:            id,
            Rank:          int.TryParse(f[idx["rank"]], out var rank) ? rank : null,
            BayesAverage:  double.TryParse(f[idx["bayesaverage"]], NumberStyles.Float, CultureInfo.InvariantCulture, out var bay) ? bay : 0,
            Average:       double.TryParse(f[idx["average"]], NumberStyles.Float, CultureInfo.InvariantCulture, out var avg) ? avg : 0,
            UsersRated:    int.TryParse(f[idx["usersrated"]], out var votes) ? votes : 0,
            YearPublished: int.TryParse(f[idx["yearpublished"]], out var yr) ? yr : null,
            SubRanks:      subRanks
        );
    }
    return result;
}

static string[] SplitCsvLine(string line)
{
    var fields = new List<string>();
    int pos = 0;
    while (pos <= line.Length)
    {
        string field;
        if (pos < line.Length && line[pos] == '"')
        {
            pos++; // skip opening "
            var sb = new System.Text.StringBuilder();
            while (pos < line.Length)
            {
                char c = line[pos++];
                if (c == '"')
                {
                    if (pos < line.Length && line[pos] == '"') { sb.Append('"'); pos++; } // escaped ""
                    else break; // closing "
                }
                else sb.Append(c);
            }
            field = sb.ToString();
        }
        else
        {
            int start = pos;
            while (pos < line.Length && line[pos] != ',') pos++;
            field = line[start..pos];
        }
        fields.Add(field);
        if (pos >= line.Length) break;
        pos++; // skip comma
    }
    return [.. fields];
}

// ── Types ─────────────────────────────────────────────────────────────────────

interface IHasStringId { string Id { get; } }

record CsvRow(
    int Id,
    int? Rank,
    double BayesAverage,
    double Average,
    int UsersRated,
    int? YearPublished,
    Dictionary<string, int?> SubRanks);

sealed class SlimStatsDoc : IHasStringId
{
    [JsonPropertyName("id")]    public string Id { get; set; } = "";
    [JsonPropertyName("votes")] public int Votes { get; set; }
}

sealed class SlimDetailsDoc : IHasStringId
{
    [JsonPropertyName("id")]             public string Id { get; set; } = "";
    [JsonPropertyName("yearPublished")]  public int? YearPublished { get; set; }
}
