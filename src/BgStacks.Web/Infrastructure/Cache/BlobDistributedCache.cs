using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
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
            return IsExpired(response.Value.Details.Metadata) ? null : response.Value.Content.ToArray();
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
            var response = await _container.GetBlobClient(key).DownloadContentAsync(token);
            return IsExpired(response.Value.Details.Metadata) ? null : response.Value.Content.ToArray();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        using var stream = new MemoryStream(value);
        _container.GetBlobClient(key).Upload(stream,
            new BlobUploadOptions { Metadata = BuildMetadata(options), Conditions = new BlobRequestConditions() });
    }

    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options,
        CancellationToken token = default)
    {
        using var stream = new MemoryStream(value);
        await _container.GetBlobClient(key).UploadAsync(stream,
            new BlobUploadOptions { Metadata = BuildMetadata(options), Conditions = new BlobRequestConditions() }, token);
    }

    public void Remove(string key) => _container.GetBlobClient(key).DeleteIfExists();

    public async Task RemoveAsync(string key, CancellationToken token = default)
        => await _container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: token);

    // FusionCache uses absolute durations for L2; Refresh is never called.
    public void Refresh(string key) { }
    public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

    private static Dictionary<string, string>? BuildMetadata(DistributedCacheEntryOptions options)
    {
        DateTimeOffset? expiry = options.AbsoluteExpiration
            ?? (options.AbsoluteExpirationRelativeToNow.HasValue
                ? DateTimeOffset.UtcNow + options.AbsoluteExpirationRelativeToNow.Value
                : (DateTimeOffset?)null);

        return expiry.HasValue
            ? new Dictionary<string, string> { [ExpiresKey] = expiry.Value.UtcTicks.ToString() }
            : null;
    }

    private static bool IsExpired(IDictionary<string, string> metadata)
        => metadata.TryGetValue(ExpiresKey, out var s)
            && long.TryParse(s, out var ticks)
            && DateTimeOffset.UtcNow.UtcTicks >= ticks;
}
