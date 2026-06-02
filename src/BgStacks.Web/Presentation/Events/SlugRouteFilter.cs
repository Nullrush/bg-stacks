using BgStacks.Web.Domain.Events;

namespace BgStacks.Web.Presentation.Events;

// No constructor injection — endpoint filters are instantiated once at route-build time
// (effectively singleton-scoped per endpoint). If a future version needs scoped services
// such as IEventRepository, resolve them from context.HttpContext.RequestServices inside
// InvokeAsync — do not add constructor parameters.
public sealed class SlugRouteFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var rawSlug = context.HttpContext.GetRouteValue("slug")?.ToString();
        if (!EventSlug.TryFrom(rawSlug, out var slug))
            return Results.NotFound();

        context.HttpContext.Items[EventMiddleware.SlugKey] = slug;
        return await next(context);
    }
}
