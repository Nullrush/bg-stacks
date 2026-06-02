using BgStacks.Web.Application.Events;
using BgStacks.Web.Domain.Events;

namespace BgStacks.Web.Presentation.Events;

public static class GamesJsonEndpoint
{
    public static IEndpointRouteBuilder MapGameDataEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/event.json", async (HttpContext ctx, EventDataService service) =>
        {
            if (ctx.Items[EventMiddleware.SlugKey] is not EventSlug slug)
                return Results.NotFound();
            var data = await service.GetEventDataAsync(slug, ctx.RequestAborted);
            return data is null
                ? Results.NotFound()
                : Results.Ok(new { title = data.Title, geeklistId = data.GeeklistId });
        }).RequireRateLimiting("event-data");

        app.MapGet("/games.json", (HttpContext ctx, EventDataService service) =>
            ServeEventFile(ctx, service, data => data.GamesJson)).RequireRateLimiting("event-data");

        app.MapGet("/mechanics.json", (HttpContext ctx, EventDataService service) =>
            ServeEventFile(ctx, service, data => data.MechanicsJson)).RequireRateLimiting("event-data");

        app.MapGet("/categories.json", (HttpContext ctx, EventDataService service) =>
            ServeEventFile(ctx, service, data => data.CategoriesJson)).RequireRateLimiting("event-data");

        return app;
    }

    private static async Task<IResult> ServeEventFile(
        HttpContext ctx, EventDataService service, Func<EventData, string> selector)
    {
        if (ctx.Items[EventMiddleware.SlugKey] is not EventSlug slug)
            return Results.NotFound();

        var data = await service.GetEventDataAsync(slug, ctx.RequestAborted);
        return data is null
            ? Results.NotFound()
            : Results.Content(selector(data), "application/json");
    }
}
