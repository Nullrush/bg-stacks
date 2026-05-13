using System.Net;
using System.Security.Claims;
using System.Text.Json;
using BgStacks.Web.Tests.Presentation.Helpers;
using FluentAssertions;

namespace BgStacks.Web.Tests.Presentation.Auth;

public class AuthEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthEndpointsTests(CustomWebApplicationFactory factory)
        => _client = factory.CreateClient();

    [Fact]
    public async Task GetMe_Unauthenticated_ReturnsNullPrincipal()
    {
        TestAuthHandler.CurrentUser = null;

        var response = await _client.GetAsync("/.auth/me");
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        doc.RootElement.GetProperty("clientPrincipal").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetMe_Authenticated_ReturnsClientPrincipal()
    {
        TestAuthHandler.CurrentUser = MakeGoogleUser("google-sub-123", "user@example.com");

        var response = await _client.GetAsync("/.auth/me");
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var principal = doc.RootElement.GetProperty("clientPrincipal");
        principal.GetProperty("identityProvider").GetString().Should().Be("google");
        principal.GetProperty("userDetails").GetString().Should().Be("user@example.com");
        principal.GetProperty("userId").GetString().Should().HaveLength(64);
    }

    private static ClaimsPrincipal MakeGoogleUser(string sub, string email) =>
        new(new ClaimsIdentity(
        [
            new Claim("idp", "google"),
            new Claim(ClaimTypes.NameIdentifier, sub),
            new Claim(ClaimTypes.Email, email),
        ], TestAuthHandler.SchemeName));
}
