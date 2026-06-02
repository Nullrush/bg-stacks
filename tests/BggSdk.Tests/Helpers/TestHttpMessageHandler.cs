using System.Collections.Concurrent;

namespace BggSdk.Tests.Helpers;

internal sealed class TestHttpMessageHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<HttpResponseMessage> _responses;
    private readonly ConcurrentQueue<string> _requestedPaths = new();

    public IReadOnlyList<string> RequestedPaths => [.. _requestedPaths];
    public int RequestCount => _requestedPaths.Count;

    public TestHttpMessageHandler(params HttpResponseMessage[] responses)
        => _responses = new ConcurrentQueue<HttpResponseMessage>(responses);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.PathAndQuery;
        _requestedPaths.Enqueue(path);
        if (!_responses.TryDequeue(out var response))
            throw new InvalidOperationException(
                $"TestHttpMessageHandler has no queued responses left. Unexpected request to: {path}");
        return Task.FromResult(response);
    }
}
