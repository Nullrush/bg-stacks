using System.Net;

namespace BggSdk.Tests.Helpers;

internal sealed class TestHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses;

    public List<string> RequestedPaths { get; } = [];
    public int RequestCount => RequestedPaths.Count;

    public TestHttpMessageHandler(params HttpResponseMessage[] responses)
        => _responses = new Queue<HttpResponseMessage>(responses);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.PathAndQuery;
        RequestedPaths.Add(path);
        if (_responses.Count == 0)
            throw new InvalidOperationException(
                $"TestHttpMessageHandler has no queued responses left. Unexpected request to: {path}");
        return Task.FromResult(_responses.Dequeue());
    }
}
