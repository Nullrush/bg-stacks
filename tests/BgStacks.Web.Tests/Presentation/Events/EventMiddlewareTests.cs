using BgStacks.Web.Domain.Events;
using BgStacks.Web.Presentation.Events;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace BgStacks.Web.Tests.Presentation.Events;

public class EventMiddlewareTests
{
    private static EventMiddleware MakeMiddleware(
        string baseDomain = "bgstacks.com", string? devSlug = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Events:BaseDomain"] = baseDomain,
                ["Events:DevFallbackSlug"] = devSlug,
            })
            .Build();
        return new EventMiddleware(_ => Task.CompletedTask, config);
    }

    private static HttpContext MakeContext(string host, string? eventHostHeader = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString(host);
        if (eventHostHeader is not null)
            ctx.Request.Headers["X-Event-Host"] = eventHostHeader;
        return ctx;
    }

    [Fact]
    public async Task EventSubdomain_SetsSlugInItems()
    {
        var middleware = MakeMiddleware();
        var ctx = MakeContext("gw-2026-pnw.bgstacks.com");

        await middleware.InvokeAsync(ctx);

        ctx.Items[EventMiddleware.SlugKey].Should().BeEquivalentTo(EventSlug.From("gw-2026-pnw"));
    }

    [Fact]
    public async Task RootDomain_SetsNullSlug()
    {
        var middleware = MakeMiddleware();
        var ctx = MakeContext("bgstacks.com");

        await middleware.InvokeAsync(ctx);

        ctx.Items[EventMiddleware.SlugKey].Should().BeNull();
    }

    [Fact]
    public async Task Localhost_WithDevFallback_SetsDevSlug()
    {
        var middleware = MakeMiddleware(devSlug: "dev");
        var ctx = MakeContext("localhost:5000");

        await middleware.InvokeAsync(ctx);

        ctx.Items[EventMiddleware.SlugKey].Should().BeEquivalentTo(EventSlug.From("dev"));
    }

    [Fact]
    public async Task Localhost_NoDevFallback_SetsNullSlug()
    {
        var middleware = MakeMiddleware();
        var ctx = MakeContext("localhost:5000");

        await middleware.InvokeAsync(ctx);

        ctx.Items[EventMiddleware.SlugKey].Should().BeNull();
    }

    [Fact]
    public async Task XEventHostHeader_TakesPrecedenceOverHost()
    {
        var middleware = MakeMiddleware();
        var ctx = MakeContext("ca-bgstacks-prod.bgstacks.com", eventHostHeader: "gw-2026-pnw.bgstacks.com");

        await middleware.InvokeAsync(ctx);

        ctx.Items[EventMiddleware.SlugKey].Should().BeEquivalentTo(EventSlug.From("gw-2026-pnw"));
    }
}
