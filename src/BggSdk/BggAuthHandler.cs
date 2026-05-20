using System.Net.Http.Headers;

namespace BggSdk;

public sealed class BggAuthHandler : DelegatingHandler
{
    private readonly string _token;

    public BggAuthHandler(string bearerToken) => _token = bearerToken;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return base.SendAsync(request, cancellationToken);
    }
}
