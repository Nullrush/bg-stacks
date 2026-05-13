using BgStacks.Web.Application.Events;

namespace BgStacks.Web.Presentation.Api;

public static class EventsEndpoints
{
    public static IEndpointRouteBuilder MapEventsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/events", async (EventService eventService, IConfiguration config) =>
        {
            var baseDomain = config["Events:BaseDomain"] ?? "bgstacks.com";
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var events = await eventService.ListPublicEventsAsync();
            var result = events.Select(e => new
            {
                slug = e.Slug.Value,
                name = e.Name,
                eventDate = e.EventDate.ToString("yyyy-MM-dd"),
                isUpcoming = e.IsUpcoming(today),
                url = $"https://{e.Slug.Value}.{baseDomain}/",
            });
            return Results.Ok(result);
        });

        return app;
    }
}
