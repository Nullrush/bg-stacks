using BgStacks.Web.Application.Tags;
using BgStacks.Web.Infrastructure.Auth;
using DomainTags = BgStacks.Web.Domain.Tags;

namespace BgStacks.Web.Presentation.Api;

public static class TagsEndpoints
{
    public static IEndpointRouteBuilder MapTagsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").RequireAuthorization();

        group.MapGet("/tags", async (HttpContext ctx, TagsService tagsService) =>
        {
            var userId = OAuthUserIdFactory.FromPrincipal(ctx.User);
            var (tags, etag) = await tagsService.GetTagsAsync(userId);
            if (tags is null) return Results.NoContent();
            return Results.Ok(new { tags = new { want = tags.Want, played = tags.Played }, etag });
        });

        group.MapPut("/tags", async (HttpContext ctx, TagsService tagsService, TagsPutRequest? body) =>
        {
            if (body?.Tags is null ||
                body.Tags.Want is null || body.Tags.Played is null)
                return Results.BadRequest(new { error = "invalid body" });

            var userId = OAuthUserIdFactory.FromPrincipal(ctx.User);
            var tags = new DomainTags.Tags(body.Tags.Want, body.Tags.Played);
            try
            {
                var etag = await tagsService.SaveTagsAsync(userId, tags, body.Etag);
                return Results.Ok(new { etag });
            }
            catch (DomainTags.ConflictException)
            {
                return Results.Conflict(new { error = "conflict" });
            }
        });

        return app;
    }
}

public sealed record TagsPutRequest(TagsPayload? Tags, string? Etag);
public sealed record TagsPayload(int[]? Want, int[]? Played);
