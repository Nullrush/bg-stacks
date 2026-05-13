using System.Security.Claims;
using BgStacks.Web.Domain.Tags;
using BgStacks.Web.Infrastructure.Auth;
using FluentAssertions;

namespace BgStacks.Web.Tests.Infrastructure.Auth;

public class OAuthUserIdFactoryTests
{
    [Fact]
    public void FromPrincipal_GoogleUser_DerivesUserIdFromProviderAndSub()
    {
        var principal = MakePrincipal("google", "google-sub-123", "user@example.com");

        var userId = OAuthUserIdFactory.FromPrincipal(principal);

        userId.Should().Be(UserId.From("google", "google-sub-123"));
    }

    [Fact]
    public void GetUserDetails_EmailAvailable_ReturnsEmail()
    {
        var principal = MakePrincipal("google", "sub", "user@example.com");

        var details = OAuthUserIdFactory.GetUserDetails(principal);

        details.Should().Be("user@example.com");
    }

    [Fact]
    public void GetUserDetails_NoEmail_ReturnsName()
    {
        var principal = MakePrincipal("discord", "sub", name: "CoolUser#1234");

        var details = OAuthUserIdFactory.GetUserDetails(principal);

        details.Should().Be("CoolUser#1234");
    }

    [Fact]
    public void GetPictureUrl_Google_ReturnsPictureClaim()
    {
        var claims = new[]
        {
            new Claim("idp", "google"),
            new Claim(ClaimTypes.NameIdentifier, "sub"),
            new Claim("picture", "https://lh3.googleusercontent.com/photo.jpg"),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        var url = OAuthUserIdFactory.GetPictureUrl(principal, "google");

        url.Should().Be("https://lh3.googleusercontent.com/photo.jpg");
    }

    [Fact]
    public void GetPictureUrl_Facebook_ConstructsGraphUrl()
    {
        var principal = MakePrincipal("facebook", "fb-user-id-999", "user@example.com");

        var url = OAuthUserIdFactory.GetPictureUrl(principal, "facebook");

        url.Should().Be("https://graph.facebook.com/fb-user-id-999/picture?type=square");
    }

    [Fact]
    public void GetPictureUrl_Discord_ConstructsCdnUrl()
    {
        var claims = new[]
        {
            new Claim("idp", "discord"),
            new Claim(ClaimTypes.NameIdentifier, "discord-id-456"),
            new Claim("avatar", "abc123hash"),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        var url = OAuthUserIdFactory.GetPictureUrl(principal, "discord");

        url.Should().Be("https://cdn.discordapp.com/avatars/discord-id-456/abc123hash.png");
    }

    private static ClaimsPrincipal MakePrincipal(string provider, string sub,
        string? email = null, string? name = null)
    {
        var claims = new List<Claim>
        {
            new("idp", provider),
            new(ClaimTypes.NameIdentifier, sub),
        };
        if (email is not null) claims.Add(new Claim(ClaimTypes.Email, email));
        if (name is not null) claims.Add(new Claim(ClaimTypes.Name, name));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
