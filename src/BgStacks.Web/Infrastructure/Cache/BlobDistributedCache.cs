using Azure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Caching.Distributed;

namespace BgStacks.Web.Infrastructure.Cache;

public sealed class BlobDistributedCache : IDistributedCache
{
    private readonly BlobContainerClient _container;

    public BlobDistributedCache(BlobServiceClient blobService)
        => _container = blobService.GetBlobContainerClient("cache");

    public byte[]? Get(string key)
    {
        try
        {
            var blob = _container.GetBlobClient(key);
            var response = blob.DownloadContent();
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
        using var stream = new MemoryStream(value);
        blob.Upload(stream, overwrite: true);
    }

    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options,
        CancellationToken token = default)
    {
        var blob = _container.GetBlobClient(key);
        using var stream = new MemoryStream(value);
        await blob.UploadAsync(stream, overwrite: true, cancellationToken: token);
    }

    public void Remove(string key)
    {
        try { _container.GetBlobClient(key).Delete(); }
        catch (RequestFailedException ex) when (ex.Status == 404) { }
    }

    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        try { await _container.GetBlobClient(key).DeleteAsync(cancellationToken: token); }
        catch (RequestFailedException ex) when (ex.Status == 404) { }
    }

    public void Refresh(string key) { }
    public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
}
