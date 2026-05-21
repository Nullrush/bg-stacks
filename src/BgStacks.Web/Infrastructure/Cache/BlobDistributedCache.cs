using Azure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Caching.Distributed;

namespace BgStacks.Web.Infrastructure.Cache;

public sealed class BlobDistributedCache : IDistributedCache
{
    private const string ExpiresKey = "expires";
    private readonly BlobContainerClient _container;

    public BlobDistributedCache(BlobServiceClient blobService)
        => _container = blobService.GetBlobContainerClient("cache");

    public byte[]? Get(string key)
    {
        try
        {
            var response = _container.GetBlobClient(key).DownloadContent();
            if (IsExpired(response.Value.Details.Metadata))
            {
                _container.GetBlobClient(key).DeleteIfExists();
                return null;
            }
            return response.Value.Content.ToArray();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        try
        {
            var blob = _container.GetBlobClient(key);
            var response = await blob.DownloadContentAsync(token);
            if (IsExpired(response.Value.Details.Metadata))
            {
                await blob.DeleteIfExistsAsync(cancellationToken: token);
                return null;
            }
            return response.Value.Content.ToArray();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        var blob = _container.GetBlobClient(key);
        var metadata = BuildMetadata(options);
        using var stream = new MemoryStream(value);
        blob.Upload(stream, overwrite: true);
        if (metadata is not null)
            blob.SetMetadata(metadata);
    }

    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options,
        CancellationToken token = default)
    {
        var blob = _container.GetBlobClient(key);
        var metadata = BuildMetadata(options);
        using var stream = new MemoryStream(value);
        await blob.UploadAsync(stream, overwrite: true, cancellationToken: token);
        if (metadata is not null)
            await blob.SetMetadataAsync(metadata, cancellationToken: token);
    }

    public void Remove(string key) => _container.GetBlobClient(key).DeleteIfExists();

    public async Task RemoveAsync(string key, CancellationToken token = default)
        => await _container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: token);

    public void Refresh(string key) { }
    public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

    private static Dictionary<string, string>? BuildMetadata(DistributedCacheEntryOptions options)
    {
        DateTimeOffset? expiry = options.AbsoluteExpiration
            ?? (options.AbsoluteExpirationRelativeToNow.HasValue
                ? DateTimeOffset.UtcNow + options.AbsoluteExpirationRelativeToNow.Value
                : null);
        return expiry.HasValue
            ? new Dictionary<string, string> { [ExpiresKey] = expiry.Value.UtcTicks.ToString() }
            : null;
    }

    private static bool IsExpired(IDictionary<string, string> metadata)
        => metadata.TryGetValue(ExpiresKey, out var s)
            && long.TryParse(s, out var ticks)
            && DateTimeOffset.UtcNow.UtcTicks >= ticks;
}
