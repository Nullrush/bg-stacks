using System.Security.Claims;
using BgStacks.Web.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace BgStacks.Web.Presentation.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/.auth");

        group.MapGet("/login/{provider}", (string provider, string? post_login_redirect_uri, HttpContext ctx) =>
        {
            var redirectUrl = post_login_redirect_uri ?? "/";
            var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
            return Results.Challenge(properties, [provider]);
        });

        group.MapGet("/logout", async (string? post_logout_redirect_uri, HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect(post_logout_redirect_uri ?? "/");
        });

        group.MapGet("/me", (HttpContext ctx) =>
        {
            if (ctx.User.Identity?.IsAuthenticated != true)
                return Results.Ok(new { clientPrincipal = (object?)null });

            var provider = ctx.User.FindFirstValue("idp") ?? "unknown";
            var userId = OAuthUserIdFactory.FromPrincipal(ctx.User);
            var userDetails = OAuthUserIdFactory.GetUserDetails(ctx.User);
            var picture = OAuthUserIdFactory.GetPictureUrl(ctx.User, provider);

            var claims = picture is not null
                ? new[] { new { typ = "picture", val = picture } }
                : [];

            return Results.Ok(new
            {
                clientPrincipal = new
                {
                    userId = userId.Value,
                    userDetails,
                    identityProvider = provider,
                    claims,
                }
            });
        });

        return app;
    }
}
