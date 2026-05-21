using Azure.Identity;
using Azure.Storage.Blobs;
using BggSdk;
using Microsoft.AspNetCore.DataProtection;
using BgStacks.Web.Application.Events;
using BgStacks.Web.Application.Tags;
using BgStacks.Web.Domain.Events;
using BgStacks.Web.Domain.Tags;
using BgStacks.Web.Infrastructure.Auth;
using BgStacks.Web.Infrastructure.Cache;
using BgStacks.Web.Infrastructure.Events;
using BgStacks.Web.Infrastructure.Tags;
using BgStacks.Web.Presentation.Api;
using BgStacks.Web.Presentation.Auth;
using BgStacks.Web.Presentation.Events;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Caching.Distributed;
using ZiggyCreatures.Caching.Fusion;

var builder = WebApplication.CreateBuilder(args);

var credential = new DefaultAzureCredential();

// ── Cosmos DB ──────────────────────────────────────────────────────────────
var cosmosDatabaseId = builder.Configuration["Cosmos:DatabaseId"] ?? "bgstacks";
var cosmosConnStr = builder.Configuration["Cosmos:ConnectionString"];
var cosmosOptions = new CosmosClientOptions
{
    UseSystemTextJsonSerializerWithOptions = new System.Text.Json.JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    },
};
var cosmosClient = cosmosConnStr is not null
    ? new CosmosClient(cosmosConnStr, cosmosOptions)
    : new CosmosClient(builder.Configuration["Cosmos:Endpoint"]!, credential, cosmosOptions);
builder.Services.AddSingleton(cosmosClient);

// ── Blob Storage ───────────────────────────────────────────────────────────
var blobConnStr = builder.Configuration["Blob:ConnectionString"];
var blobClient = blobConnStr is not null
    ? new BlobServiceClient(blobConnStr)
    : new BlobServiceClient(new Uri(builder.Configuration["Blob:ServiceUri"]!), credential);
builder.Services.AddSingleton(blobClient);

// ── Domain Repositories ────────────────────────────────────────────────────
builder.Services.AddScoped<ITagsRepository>(sp =>
    new CosmosTagsRepository(sp.GetRequiredService<CosmosClient>(), cosmosDatabaseId));
builder.Services.AddScoped<IEventRepository>(sp =>
    new CosmosEventRepository(sp.GetRequiredService<CosmosClient>(), cosmosDatabaseId));
builder.Services.AddScoped<IGameDetailsRepository>(sp =>
    new CosmosGameDetailsRepository(sp.GetRequiredService<CosmosClient>(), cosmosDatabaseId));
builder.Services.AddScoped<IGameStatsRepository>(sp =>
    new CosmosGameStatsRepository(sp.GetRequiredService<CosmosClient>(), cosmosDatabaseId));

// ── Caching ────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IDistributedCache, BlobDistributedCache>();
builder.Services.AddFusionCache()
    .WithSystemTextJsonSerializer()
    .WithRegisteredDistributedCache();

// ── BGG Client & Services ──────────────────────────────────────────────────
var bggApiToken = builder.Configuration["Bgg:ApiToken"];
if (string.IsNullOrEmpty(bggApiToken) && builder.Environment.IsProduction())
    throw new InvalidOperationException("Bgg:ApiToken configuration is required.");
builder.Services.AddBggClient(bggApiToken ?? "");
builder.Services.AddScoped<IBggThingService>(sp =>
    new BggThingService(
        sp.GetRequiredService<BggClient>(),
        sp.GetRequiredService<IGameDetailsRepository>(),
        sp.GetRequiredService<IGameStatsRepository>()));
builder.Services.AddScoped<IBggGeeklistService>(sp =>
    new BggGeeklistService(
        sp.GetRequiredService<BggClient>(),
        sp.GetRequiredService<IBggThingService>(),
        sp.GetRequiredService<IFusionCache>(),
        builder.Configuration.GetValue("Bgg:CacheCheckIntervalMinutes", 30)));

// ── Application Services ───────────────────────────────────────────────────
builder.Services.AddScoped<TagsService>();
builder.Services.AddScoped<EventService>();
builder.Services.AddScoped<EventDataService>();

// ── Authentication ─────────────────────────────────────────────────────────
var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.LoginPath = "/.auth/login/google";
    options.Events.OnRedirectToLogin = ctx =>
    {
        ctx.Response.StatusCode = 401;
        return Task.CompletedTask;
    };
});

var googleClientId = builder.Configuration["Auth:Google:ClientId"];
if (!string.IsNullOrEmpty(googleClientId))
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = builder.Configuration["Auth:Google:ClientSecret"]!;
        options.Events.OnCreatingTicket = ctx =>
        {
            if (ctx.User.TryGetProperty("picture", out var pic) && pic.ValueKind == System.Text.Json.JsonValueKind.String)
                ctx.Identity!.AddClaim(new System.Security.Claims.Claim("picture", pic.GetString()!));
            ctx.Identity!.AddClaim(new System.Security.Claims.Claim("idp", "google"));
            return Task.CompletedTask;
        };
    });

var facebookClientId = builder.Configuration["Auth:Facebook:ClientId"];
if (!string.IsNullOrEmpty(facebookClientId))
    authBuilder.AddFacebook(options =>
    {
        options.ClientId = facebookClientId;
        options.ClientSecret = builder.Configuration["Auth:Facebook:ClientSecret"]!;
        options.Events.OnCreatingTicket = ctx =>
        {
            ctx.Identity!.AddClaim(new System.Security.Claims.Claim("idp", "facebook"));
            return Task.CompletedTask;
        };
    });

var discordClientId = builder.Configuration["Auth:Discord:ClientId"];
if (!string.IsNullOrEmpty(discordClientId))
    authBuilder.AddDiscord(options =>
    {
        options.ClientId = discordClientId;
        options.ClientSecret = builder.Configuration["Auth:Discord:ClientSecret"]!;
        options.Scope.Add("identify");
        options.Scope.Add("email");
        options.Events.OnCreatingTicket = ctx =>
        {
            if (ctx.User.TryGetProperty("avatar", out var avatar) && avatar.ValueKind == System.Text.Json.JsonValueKind.String)
                ctx.Identity!.AddClaim(new System.Security.Claims.Claim("avatar", avatar.GetString()!));
            ctx.Identity!.AddClaim(new System.Security.Claims.Claim("idp", "discord"));
            return Task.CompletedTask;
        };
    });

// ── Data Protection ────────────────────────────────────────────────────────
var dpBlobUri = builder.Configuration["DataProtection:BlobUri"];
var dpKeyUri = builder.Configuration["DataProtection:KeyVaultKeyUri"];
if (dpBlobUri is not null && dpKeyUri is not null)
{
    var dpBlob = new Azure.Storage.Blobs.BlobClient(new Uri(dpBlobUri), credential);
    builder.Services.AddDataProtection()
        .PersistKeysToAzureBlobStorage(dpBlob)
        .ProtectKeysWithAzureKeyVault(new Uri(dpKeyUri), credential);
}

builder.Services.AddAuthorization();

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
    await app.Services.GetRequiredService<BlobServiceClient>()
        .GetBlobContainerClient("cache")
        .CreateIfNotExistsAsync();

app.UseMiddleware<EventMiddleware>();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapTagsEndpoints();
app.MapEventsEndpoints();
app.MapGameDataEndpoints();

// SPA fallback: event subdomain → index.html, root domain → home.html
app.MapFallback(async (HttpContext ctx, EventDataService eventDataService) =>
{
    var slug = ctx.Items[EventMiddleware.SlugKey];

    if (slug is null)
    {
        var homePath = Path.Combine(app.Environment.WebRootPath, "home.html");
        if (!File.Exists(homePath)) { ctx.Response.StatusCode = 404; return; }
        ctx.Response.ContentType = "text/html";
        await ctx.Response.SendFileAsync(homePath);
        return;
    }

    var eventData = await eventDataService.GetEventDataAsync((EventSlug)slug, ctx.RequestAborted);
    if (eventData is null) { ctx.Response.StatusCode = 404; return; }

    var indexPath = Path.Combine(app.Environment.WebRootPath, "index.html");
    if (!File.Exists(indexPath)) { ctx.Response.StatusCode = 404; return; }
    ctx.Response.ContentType = "text/html";
    await ctx.Response.SendFileAsync(indexPath);
});

app.Run();

public partial class Program { }
