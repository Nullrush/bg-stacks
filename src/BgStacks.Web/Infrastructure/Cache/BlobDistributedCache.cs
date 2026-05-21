using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Caching.Distributed;

namespace BgStacks.Web.Infrastructure.Cache;

public sealed class BlobDistributedCache : IDistributedCache
{
    private const string ExpiresKey = "expires";
    private const string SlidingKey = "sliding";
    private const string AbsoluteKey = "absolute";
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
        using var stream = new MemoryStream(value);
        _container.GetBlobClient(key).Upload(stream,
            new BlobUploadOptions { Metadata = BuildMetadata(options) });
    }

    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options,
        CancellationToken token = default)
    {
        using var stream = new MemoryStream(value);
        await _container.GetBlobClient(key).UploadAsync(stream,
            new BlobUploadOptions { Metadata = BuildMetadata(options) }, token);
    }

    public void Remove(string key) => _container.GetBlobClient(key).DeleteIfExists();

    public async Task RemoveAsync(string key, CancellationToken token = default)
        => await _container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: token);

    public void Refresh(string key)
    {
        try
        {
            var blob = _container.GetBlobClient(key);
            var props = blob.GetProperties();
            var metadata = new Dictionary<string, string>(props.Value.Metadata);
            if (!metadata.TryGetValue(SlidingKey, out var s) || !long.TryParse(s, out var slidingTicks))
                return;
            var newExpiry = DateTimeOffset.UtcNow + TimeSpan.FromTicks(slidingTicks);
            if (metadata.TryGetValue(AbsoluteKey, out var a) && long.TryParse(a, out var absoluteTicks))
                newExpiry = newExpiry < new DateTimeOffset(absoluteTicks, TimeSpan.Zero)
                    ? newExpiry : new DateTimeOffset(absoluteTicks, TimeSpan.Zero);
            metadata[ExpiresKey] = newExpiry.UtcTicks.ToString();
            blob.SetMetadata(metadata);
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { }
    }

    public async Task RefreshAsync(string key, CancellationToken token = default)
    {
        try
        {
            var blob = _container.GetBlobClient(key);
            var props = await blob.GetPropertiesAsync(cancellationToken: token);
            var metadata = new Dictionary<string, string>(props.Value.Metadata);
            if (!metadata.TryGetValue(SlidingKey, out var s) || !long.TryParse(s, out var slidingTicks))
                return;
            var newExpiry = DateTimeOffset.UtcNow + TimeSpan.FromTicks(slidingTicks);
            if (metadata.TryGetValue(AbsoluteKey, out var a) && long.TryParse(a, out var absoluteTicks))
                newExpiry = newExpiry < new DateTimeOffset(absoluteTicks, TimeSpan.Zero)
                    ? newExpiry : new DateTimeOffset(absoluteTicks, TimeSpan.Zero);
            metadata[ExpiresKey] = newExpiry.UtcTicks.ToString();
            await blob.SetMetadataAsync(metadata, cancellationToken: token);
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { }
    }

    private static Dictionary<string, string>? BuildMetadata(DistributedCacheEntryOptions options)
    {
        DateTimeOffset? absoluteDeadline = options.AbsoluteExpiration
            ?? (options.AbsoluteExpirationRelativeToNow.HasValue
                ? DateTimeOffset.UtcNow + options.AbsoluteExpirationRelativeToNow.Value
                : (DateTimeOffset?)null);

        DateTimeOffset? expiry = absoluteDeadline
            ?? (options.SlidingExpiration.HasValue
                ? DateTimeOffset.UtcNow + options.SlidingExpiration.Value
                : (DateTimeOffset?)null);

        if (!expiry.HasValue && !options.SlidingExpiration.HasValue)
            return null;

        var metadata = new Dictionary<string, string>();
        if (expiry.HasValue)
            metadata[ExpiresKey] = expiry.Value.UtcTicks.ToString();
        if (options.SlidingExpiration.HasValue)
            metadata[SlidingKey] = options.SlidingExpiration.Value.Ticks.ToString();
        if (absoluteDeadline.HasValue && options.SlidingExpiration.HasValue)
            metadata[AbsoluteKey] = absoluteDeadline.Value.UtcTicks.ToString();
        return metadata;
    }

    private static bool IsExpired(IDictionary<string, string> metadata)
        => metadata.TryGetValue(ExpiresKey, out var s)
            && long.TryParse(s, out var ticks)
            && DateTimeOffset.UtcNow.UtcTicks >= ticks;
}
