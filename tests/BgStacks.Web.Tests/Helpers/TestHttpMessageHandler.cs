namespace BgStacks.Web.Tests.Helpers;

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
        RequestedPaths.Add(request.RequestUri!.PathAndQuery);
        if (_responses.Count == 0)
            throw new InvalidOperationException(
                $"No queued response for: {request.RequestUri!.PathAndQuery}");
        return Task.FromResult(_responses.Dequeue());
    }
}
