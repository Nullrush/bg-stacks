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
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;
using ZiggyCreatures.Caching.Fusion;

namespace BgStacks.Web.Infrastructure;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddAzureInfrastructure(
        this IServiceCollection services,
        TokenCredential credential)
    {
        services.AddOptions<CosmosOptions>()
            .BindConfiguration(CosmosOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<BlobOptions>()
            .BindConfiguration(BlobOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(sp => {
            var o = sp.GetRequiredService<IOptions<CosmosOptions>>().Value;
            var opts = new CosmosClientOptions
            {
                UseSystemTextJsonSerializerWithOptions = new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                },
            };
            return o.ConnectionString is not null
                ? new CosmosClient(o.ConnectionString, opts)
                : new CosmosClient(o.Endpoint!, credential, opts);
        });

        services.AddSingleton(sp => {
            var o = sp.GetRequiredService<IOptions<BlobOptions>>().Value;
            return o.ConnectionString is not null
                ? new BlobServiceClient(o.ConnectionString)
                : new BlobServiceClient(new Uri(o.ServiceUri!), credential);
        });

        services.AddSingleton<IDistributedCache, BlobDistributedCache>();
        services.AddFusionCache()
            .WithSystemTextJsonSerializer()
            .WithRegisteredDistributedCache();

        // CRITICAL: resolve DatabaseId from IOptions at scope time, not construction time.
        // PR revisions receive Cosmos__DatabaseId as an env var override — if captured as a
        // local, all revisions silently point at prod.
        services.AddScoped<ITagsRepository>(sp => {
            var dbId = sp.GetRequiredService<IOptions<CosmosOptions>>().Value.DatabaseId;
            return new CosmosTagsRepository(sp.GetRequiredService<CosmosClient>(), dbId);
        });
        services.AddScoped<IEventRepository>(sp => {
            var dbId = sp.GetRequiredService<IOptions<CosmosOptions>>().Value.DatabaseId;
            return new CosmosEventRepository(sp.GetRequiredService<CosmosClient>(), dbId);
        });
        services.AddScoped<IGameDetailsRepository>(sp => {
            var dbId = sp.GetRequiredService<IOptions<CosmosOptions>>().Value.DatabaseId;
            return new CosmosGameDetailsRepository(sp.GetRequiredService<CosmosClient>(), dbId);
        });
        services.AddScoped<IGameStatsRepository>(sp => {
            var dbId = sp.GetRequiredService<IOptions<CosmosOptions>>().Value.DatabaseId;
            return new CosmosGameStatsRepository(sp.GetRequiredService<CosmosClient>(), dbId);
        });

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
