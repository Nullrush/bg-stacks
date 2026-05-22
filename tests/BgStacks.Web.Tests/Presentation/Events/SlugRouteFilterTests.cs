using BgStacks.Web.Domain.Events;
using BgStacks.Web.Presentation.Events;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace BgStacks.Web.Tests.Presentation.Events;

public class SlugRouteFilterTests
{
    private static FakeFilterContext MakeContext(string? slugRouteValue)
    {
        var httpContext = new DefaultHttpContext();
        if (slugRouteValue is not null)
            httpContext.Request.RouteValues["slug"] = slugRouteValue;
        return new FakeFilterContext(httpContext);
    }

    [Fact]
    public async Task ValidSlug_SetsItemsAndCallsNext()
    {
        var nextCalled = false;
        EndpointFilterDelegate next = ctx =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };
        var filter = new SlugRouteFilter();
        var context = MakeContext("geekway-2026");

        await filter.InvokeAsync(context, next);

        nextCalled.Should().BeTrue();
        context.HttpContext.Items[EventMiddleware.SlugKey]
            .Should().BeEquivalentTo(EventSlug.From("geekway-2026"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("NOT-VALID!")]
    [InlineData("-bad-start")]
    [InlineData("has space")]
    public async Task InvalidSlugFormat_ReturnsNotFound_DoesNotCallNext(string slugValue)
    {
        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };
        var filter = new SlugRouteFilter();
        var context = MakeContext(slugValue);

        var result = await filter.InvokeAsync(context, next);

        nextCalled.Should().BeFalse();
        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.NotFound>();
    }

    [Fact]
    public async Task MissingSlugRouteValue_ReturnsNotFound()
    {
        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };
        var filter = new SlugRouteFilter();
        var context = MakeContext(slugRouteValue: null);   // no route value at all

        var result = await filter.InvokeAsync(context, next);

        nextCalled.Should().BeFalse();
        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.NotFound>();
    }

    [Fact]
    public async Task MixedCaseSlug_StoresNormalizedSlugInItems()
    {
        var filter = new SlugRouteFilter();
        var context = MakeContext("Geekway-2026");     // mixed case

        await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>(Results.Ok()));

        context.HttpContext.Items[EventMiddleware.SlugKey]
            .Should().BeEquivalentTo(EventSlug.From("geekway-2026"));
    }
}

// EndpointFilterInvocationContext is abstract — create a minimal fake for unit tests.
internal sealed class FakeFilterContext(HttpContext httpContext) : EndpointFilterInvocationContext
{
    public override HttpContext HttpContext => httpContext;
    public override IList<object?> Arguments => [];
    public override T GetArgument<T>(int index) => default!;
}
