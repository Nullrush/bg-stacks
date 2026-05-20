// src/BggSdk/BggClient.cs
using BggSdk.Exceptions;
using BggSdk.Models;
using BggSdk.Parsing;

namespace BggSdk;

public sealed class BggClient
{
    private readonly HttpClient _http;
    private readonly TimeSpan _retryDelay;

    /// <summary>
    /// Creates a BggClient. The supplied <paramref name="httpClient"/> must have
    /// its BaseAddress set to the API root, e.g. https://boardgamegeek.com/xmlapi2/.
    /// Use <see cref="BggAuthHandler"/> to inject a Bearer token.
    /// </summary>
    public BggClient(HttpClient httpClient, TimeSpan? retryDelay = null)
    {
        _http        = httpClient;
        _retryDelay  = retryDelay ?? TimeSpan.FromSeconds(5);
    }

    public async Task<Geeklist> GetGeeklistAsync(int id, CancellationToken ct = default)
    {
        var response = await SendWithRetryAsync($"geeklist/{id}", ct);
        var xml = await response.Content.ReadAsStringAsync(ct);
        return GeeklistParser.Parse(xml);
    }

    /// <summary>
    /// Fetches enriched data for the specified game IDs. Automatically batches requests
    /// into chunks of at most 20 IDs (BGG API limit).
    /// Results are returned in the order BGG provides them within each batch,
    /// which is not guaranteed to match the input ordering.
    /// </summary>
    public async Task<IReadOnlyList<Thing>> GetThingsAsync(
        IEnumerable<int> ids, CancellationToken ct = default)
    {
        var results = new List<Thing>();
        foreach (var chunk in ids.Chunk(20))
        {
            var response = await SendWithRetryAsync(
                $"thing?id={string.Join(",", chunk)}&stats=1", ct);
            var xml = await response.Content.ReadAsStringAsync(ct);
            results.AddRange(ThingParser.Parse(xml));
        }
        return results;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        string path, CancellationToken ct, int maxAttempts = 5)
    {
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var response = await _http.GetAsync(path, ct);

            if ((int)response.StatusCode == 429)
            {
                response.Dispose();
                throw new BggRateLimitException();
            }

            if ((int)response.StatusCode != 202)
            {
                response.EnsureSuccessStatusCode();
                return response;
            }

            response.Dispose();
            if (attempt < maxAttempts - 1)
                await Task.Delay(_retryDelay, ct);
        }

        throw new BggRetryException(maxAttempts);
    }
}
