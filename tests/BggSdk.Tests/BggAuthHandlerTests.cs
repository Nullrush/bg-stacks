using System.Net;
using BggSdk.Tests.Helpers;

namespace BggSdk.Tests;

public class BggAuthHandlerTests
{
    [Fact]
    public async Task SendAsync_AddsBearerTokenHeader()
    {
        HttpRequestMessage? captured = null;
        var inner = new CaptureDelegatingHandler(req => captured = req);
        var handler = new BggAuthHandler("my-secret-token") { InnerHandler = inner };
        var invoker = new HttpMessageInvoker(handler);

        await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://example.com/"),
            CancellationToken.None);

        captured!.Headers.Authorization.Should().NotBeNull();
        captured.Headers.Authorization!.Scheme.Should().Be("Bearer");
        captured.Headers.Authorization.Parameter.Should().Be("my-secret-token");
    }

    [Fact]
    public async Task SendAsync_EmptyToken_DoesNotAddHeader()
    {
        HttpRequestMessage? captured = null;
        var inner = new CaptureDelegatingHandler(req => captured = req);
        var handler = new BggAuthHandler("") { InnerHandler = inner };
        var invoker = new HttpMessageInvoker(handler);

        await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://example.com/"),
            CancellationToken.None);

        captured!.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_WhitespaceToken_DoesNotAddHeader()
    {
        HttpRequestMessage? captured = null;
        var inner = new CaptureDelegatingHandler(req => captured = req);
        var handler = new BggAuthHandler("   ") { InnerHandler = inner };
        var invoker = new HttpMessageInvoker(handler);

        await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://example.com/"),
            CancellationToken.None);

        captured!.Headers.Authorization.Should().BeNull();
    }

    private sealed class CaptureDelegatingHandler : HttpMessageHandler
    {
        private readonly Action<HttpRequestMessage> _capture;

        public CaptureDelegatingHandler(Action<HttpRequestMessage> capture)
            => _capture = capture;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _capture(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
