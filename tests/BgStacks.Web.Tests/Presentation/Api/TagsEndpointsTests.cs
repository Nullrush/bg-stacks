using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using BgStacks.Web.Tests.Presentation.Helpers;
using FluentAssertions;

namespace BgStacks.Web.Tests.Presentation.Api;

public class TagsEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public TagsEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetTags_Unauthenticated_Returns401()
    {
        TestAuthHandler.CurrentUser = null;
        var response = await _client.GetAsync("/api/tags");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetTags_AuthenticatedNoData_Returns204()
    {
        TestAuthHandler.CurrentUser = MakeGoogleUser("user-no-tags");
        var response = await _client.GetAsync("/api/tags");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task PutTags_ValidBody_Returns200WithEtag()
    {
        TestAuthHandler.CurrentUser = MakeGoogleUser("user-put-test");
        var body = new { tags = new { want = new[] { 1, 2 }, played = new[] { 3 } }, etag = (string?)null };

        var response = await _client.PutAsJsonAsync("/api/tags", body);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        doc.RootElement.GetProperty("etag").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task PutTags_StaleEtag_Returns409()
    {
        TestAuthHandler.CurrentUser = MakeGoogleUser("user-conflict-test");
        var body = new { tags = new { want = new[] { 1 }, played = Array.Empty<int>() }, etag = "stale-etag" };

        var response = await _client.PutAsJsonAsync("/api/tags", body);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PutTags_MissingBody_Returns400()
    {
        TestAuthHandler.CurrentUser = MakeGoogleUser("user-bad-body");
        var response = await _client.PutAsJsonAsync("/api/tags", new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static ClaimsPrincipal MakeGoogleUser(string sub) =>
        new(new ClaimsIdentity(
        [
            new Claim("idp", "google"),
            new Claim(ClaimTypes.NameIdentifier, sub),
            new Claim(ClaimTypes.Email, $"{sub}@example.com"),
        ], TestAuthHandler.SchemeName));
}
