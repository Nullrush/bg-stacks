using Azure.Identity;
using Azure.Storage.Blobs;
using BgStacks.Web.Application.Events;
using BgStacks.Web.Application.Tags;
using BgStacks.Web.Infrastructure;
using BgStacks.Web.Infrastructure.Auth;
using BgStacks.Web.Infrastructure.Events;
using BgStacks.Web.Presentation.Auth;
using BgStacks.Web.Presentation.Events;
using BgStacks.Web.Presentation.Api;

var builder = WebApplication.CreateBuilder(args);

var credential = new DefaultAzureCredential();

builder.Services.AddAzureInfrastructure(builder.Configuration, credential);
builder.Services.AddProxyForwardedHeaders(builder.Configuration);
builder.Services.AddEventDataRateLimiting();
builder.Services.AddBggServices(builder.Configuration, builder.Environment);

builder.Services.AddScoped<TagsService>();
builder.Services.AddScoped<EventService>();
builder.Services.AddScoped<EventDataService>();

builder.Services.AddAuthServices(builder.Configuration, credential);
builder.Services.AddAuthorization();

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
    await app.Services.GetRequiredService<BlobServiceClient>()
        .GetBlobContainerClient("cache")
        .CreateIfNotExistsAsync();

if (app.Environment.IsProduction() || app.Environment.IsEnvironment("Staging"))
    app.UseForwardedHeaders();
app.UseMiddleware<EventMiddleware>();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapTagsEndpoints();
app.MapEventsEndpoints();
app.MapGameDataEndpoints();

if (app.Configuration.GetValue<bool>("Events:PathBasedRouting"))
{
    var slugGroup = app
        .MapGroup("/event/{slug}")
        .AddEndpointFilter<SlugRouteFilter>();

    // Reuses the existing handlers + RequireRateLimiting("event-data") from GamesJsonEndpoint.
    // RouteGroupBuilder implements IEndpointRouteBuilder, so the extension method composes
    // routes under the /event/{slug} prefix automatically.
    slugGroup.MapGameDataEndpoints();

    slugGroup.MapGet("/{**path}", async (HttpContext ctx) =>
    {
        var filePath = Path.Combine(app.Environment.WebRootPath, "index.html");
        if (!File.Exists(filePath)) { ctx.Response.StatusCode = 404; return; }
        ctx.Response.ContentType = "text/html";
        await ctx.Response.SendFileAsync(filePath);
    });
}

// SPA fallback: event subdomain → index.html, root domain → home.html
app.MapFallback(async (HttpContext ctx) =>
{
    var slug = ctx.Items[EventMiddleware.SlugKey];

    // Serve the shell unconditionally: the rate-limited /games.json endpoint gates
    // actual data and returns 404 for unknown slugs. Calling GetEventDataAsync here
    // would couple shell delivery to a live BGG round-trip and bypass rate limiting.
    var file = slug is null ? "home.html" : "index.html";
    var filePath = Path.Combine(app.Environment.WebRootPath, file);
    if (!File.Exists(filePath)) { ctx.Response.StatusCode = 404; return; }
    ctx.Response.ContentType = "text/html";
    await ctx.Response.SendFileAsync(filePath);
});

app.Run();

public partial class Program { }
