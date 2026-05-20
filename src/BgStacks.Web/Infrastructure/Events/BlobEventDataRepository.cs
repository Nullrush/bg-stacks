using Azure;
using Azure.Storage.Blobs;
using BgStacks.Web.Domain.Events;

namespace BgStacks.Web.Infrastructure.Events;

public sealed class BlobEventDataRepository : IEventDataRepository
{
    private readonly BlobServiceClient _blobClient;
    private const string ContainerName = "events";

    public BlobEventDataRepository(BlobServiceClient blobClient) => _blobClient = blobClient;

    public async Task<EventData?> GetAsync(EventSlug slug, CancellationToken ct = default)
    {
        var container = _blobClient.GetBlobContainerClient(ContainerName);
        try
        {
            var games = await DownloadTextAsync(container, $"{slug.Value}/games.json", ct);
            var mechanics = await DownloadTextAsync(container, $"{slug.Value}/mechanics.json", ct);
            var categories = await DownloadTextAsync(container, $"{slug.Value}/categories.json", ct);
            return new EventData
            {
                SlugValue = slug.Value,
                GamesJson = games,
                MechanicsJson = mechanics,
                CategoriesJson = categories,
            };
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private static async Task<string> DownloadTextAsync(
        BlobContainerClient container, string blobName, CancellationToken ct)
    {
        var blob = container.GetBlobClient(blobName);
        var response = await blob.DownloadContentAsync(ct);
        return response.Value.Content.ToString();
    }
}
