using Azure.Core;
using Azure.Storage.Blobs;
using BgStacks.Web.Domain.Events;
using BgStacks.Web.Domain.Tags;
using BgStacks.Web.Infrastructure.Cache;
using BgStacks.Web.Infrastructure.Events;
using BgStacks.Web.Infrastructure.Tags;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Caching.Distributed;
using System.Threading.RateLimiting;
using ZiggyCreatures.Caching.Fusion;

namespace BgStacks.Web.Infrastructure;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddAzureInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        TokenCredential credential)
    {
        var cosmosDatabaseId = configuration["Cosmos:DatabaseId"] ?? "bgstacks";
        var cosmosConnStr = configuration["Cosmos:ConnectionString"];
        var cosmosOptions = new CosmosClientOptions
        {
            UseSystemTextJsonSerializerWithOptions = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            },
        };
        var cosmosClient = cosmosConnStr is not null
            ? new CosmosClient(cosmosConnStr, cosmosOptions)
            : new CosmosClient(configuration["Cosmos:Endpoint"]!, credential, cosmosOptions);
        services.AddSingleton(cosmosClient);

        var blobConnStr = configuration["Blob:ConnectionString"];
        var blobClient = blobConnStr is not null
            ? new BlobServiceClient(blobConnStr)
            : new BlobServiceClient(new Uri(configuration["Blob:ServiceUri"]!), credential);
        services.AddSingleton(blobClient);

        services.Configure<BlobOptions>(configuration.GetSection(BlobOptions.SectionName));

        services.AddScoped<ITagsRepository>(sp =>
            new CosmosTagsRepository(sp.GetRequiredService<CosmosClient>(), cosmosDatabaseId));
        services.AddScoped<IEventRepository>(sp =>
            new CosmosEventRepository(sp.GetRequiredService<CosmosClient>(), cosmosDatabaseId));
        services.AddScoped<IGameDetailsRepository>(sp =>
            new CosmosGameDetailsRepository(sp.GetRequiredService<CosmosClient>(), cosmosDatabaseId));
        services.AddScoped<IGameStatsRepository>(sp =>
            new CosmosGameStatsRepository(sp.GetRequiredService<CosmosClient>(), cosmosDatabaseId));

        services.AddSingleton<IDistributedCache, BlobDistributedCache>();
        services.AddFusionCache()
            .WithSystemTextJsonSerializer()
            .WithRegisteredDistributedCache();

        return services;
    }

    // In production/staging all traffic arrives through the ACA LB. ForwardLimit=1
    // ensures only the LB-appended (rightmost) X-Forwarded-For entry is trusted,
    // so a client-crafted header earlier in the chain is ignored.
    // When ForwardedHeaders:KnownNetworks is configured (CIDR list), only those
    // ranges are trusted. When unset, all sources are trusted (ACA: proxy IPs are dynamic).
    public static IServiceCollection AddProxyForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var knownNetworks = configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>();
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            options.ForwardLimit = 1;

            if (knownNetworks is { Length: > 0 })
            {
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
                foreach (var cidr in knownNetworks)
                    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
            }
            else
            {
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            }
        });

        return services;
    }

    public static IServiceCollection AddEventDataRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.AddPolicy("event-data", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });

        return services;
    }
}
