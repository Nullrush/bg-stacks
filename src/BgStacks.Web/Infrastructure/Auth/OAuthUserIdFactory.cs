using System.Security.Claims;
using BgStacks.Web.Domain.Tags;

namespace BgStacks.Web.Infrastructure.Auth;

public static class OAuthUserIdFactory
{
    public static UserId FromPrincipal(ClaimsPrincipal principal)
    {
        var provider = principal.FindFirstValue("idp")
            ?? throw new InvalidOperationException("Identity provider claim 'idp' not found.");
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("NameIdentifier claim not found.");
        return UserId.From(provider, sub);
    }

    public static string? GetUserDetails(ClaimsPrincipal principal)
        => principal.FindFirstValue(ClaimTypes.Email)
           ?? principal.FindFirstValue(ClaimTypes.Name);

    public static string? GetPictureUrl(ClaimsPrincipal principal, string provider) =>
        provider switch
        {
            "google" => principal.FindFirstValue("picture"),
            "facebook" => principal.FindFirstValue(ClaimTypes.NameIdentifier) is { } fbId
                ? $"https://graph.facebook.com/{fbId}/picture?type=square"
                : null,
            "discord" => GetDiscordAvatarUrl(principal),
            _ => null,
        };

    private static string? GetDiscordAvatarUrl(ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var avatar = principal.FindFirstValue("avatar");
        return id is not null && avatar is not null
            ? $"https://cdn.discordapp.com/avatars/{id}/{avatar}.png"
            : null;
    }
}
