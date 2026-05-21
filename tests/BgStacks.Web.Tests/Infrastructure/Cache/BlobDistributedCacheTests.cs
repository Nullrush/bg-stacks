using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BgStacks.Web.Infrastructure.Cache;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using NSubstitute;

namespace BgStacks.Web.Tests.Infrastructure.Cache;

public class BlobDistributedCacheTests
{
    private static (BlobDistributedCache sut, BlobClient blobClient) MakeWithBlob(
        byte[] content, IDictionary<string, string>? metadata = null)
    {
        var details = BlobsModelFactory.BlobDownloadDetails(metadata: metadata ?? new Dictionary<string, string>());
        var downloadResult = BlobsModelFactory.BlobDownloadResult(
            content: BinaryData.FromBytes(content), details: details);

        var blobClient = Substitute.For<BlobClient>();
        blobClient.DownloadContentAsync(Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(downloadResult, Substitute.For<Response>()));

        var container = Substitute.For<BlobContainerClient>();
        container.GetBlobClient(Arg.Any<string>()).Returns(blobClient);

        var serviceClient = Substitute.For<BlobServiceClient>();
        serviceClient.GetBlobContainerClient("cache").Returns(container);

        return (new BlobDistributedCache(serviceClient), blobClient);
    }

    private static BlobDistributedCache MakeWithNotFound()
    {
        var blobClient = Substitute.For<BlobClient>();
        blobClient.DownloadContentAsync(Arg.Any<CancellationToken>())
            .Returns<Response<BlobDownloadResult>>(_ => throw new RequestFailedException(404, "Not Found"));

        var container = Substitute.For<BlobContainerClient>();
        container.GetBlobClient(Arg.Any<string>()).Returns(blobClient);

        var serviceClient = Substitute.For<BlobServiceClient>();
        serviceClient.GetBlobContainerClient("cache").Returns(container);

        return new BlobDistributedCache(serviceClient);
    }

    [Fact]
    public async Task GetAsync_BlobNotFound_ReturnsNull()
    {
        var sut = MakeWithNotFound();
        var result = await sut.GetAsync("missing-key");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_NonExpiredBlob_ReturnsData()
    {
        var data = new byte[] { 1, 2, 3 };
        var futureExpiry = DateTimeOffset.UtcNow.AddHours(1);
        var metadata = new Dictionary<string, string>
        {
            ["expires"] = futureExpiry.UtcTicks.ToString()
        };

        var (sut, blobClient) = MakeWithBlob(data, metadata);
        var result = await sut.GetAsync("test-key");

        result.Should().BeEquivalentTo(data);
        await blobClient.DidNotReceiveWithAnyArgs().DeleteIfExistsAsync();
    }

    [Fact]
    public async Task GetAsync_ExpiredBlob_ReturnsNullWithoutDeleting()
    {
        var data = new byte[] { 1, 2, 3 };
        var pastExpiry = DateTimeOffset.UtcNow.AddHours(-1);
        var metadata = new Dictionary<string, string>
        {
            ["expires"] = pastExpiry.UtcTicks.ToString()
        };

        var (sut, blobClient) = MakeWithBlob(data, metadata);
        var result = await sut.GetAsync("test-key");

        result.Should().BeNull();
        // No delete call — expired entries return null without any storage mutation.
        await blobClient.DidNotReceiveWithAnyArgs().DeleteIfExistsAsync();
    }

    [Fact]
    public async Task GetAsync_NoExpiryMetadata_ReturnsData()
    {
        var data = new byte[] { 42 };
        var (sut, _) = MakeWithBlob(data);

        var result = await sut.GetAsync("no-expiry-key");

        result.Should().BeEquivalentTo(data);
    }

    [Fact]
    public void Refresh_IsNoOp()
    {
        // Refresh is a no-op; FusionCache uses absolute durations for L2 and never calls it.
        var sut = MakeWithNotFound();
        var act = () => sut.Refresh("any-key");
        act.Should().NotThrow();
    }

    [Fact]
    public async Task RefreshAsync_IsNoOp()
    {
        var sut = MakeWithNotFound();
        await sut.RefreshAsync("any-key");
    }
}
