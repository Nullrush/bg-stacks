using BggSdk;
using BgStacks.Web.Application.Events;
using ZiggyCreatures.Caching.Fusion;

namespace BgStacks.Web.Infrastructure.Events;

public static class BggServicesExtensions
{
    public static IServiceCollection AddBggServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var bggApiToken = configuration["Bgg:ApiToken"];
        if (string.IsNullOrEmpty(bggApiToken)
            && (environment.IsProduction() || environment.IsEnvironment("Staging")))
            throw new InvalidOperationException("Bgg:ApiToken configuration is required.");

        services.AddBggClient(bggApiToken ?? "");
        services.AddScoped<IBggThingService, BggThingService>();
        services.AddScoped<IBggGeeklistService>(sp =>
            new BggGeeklistService(
                sp.GetRequiredService<BggClient>(),
                sp.GetRequiredService<IBggThingService>(),
                sp.GetRequiredService<IFusionCache>(),
                configuration.GetValue("Bgg:CacheCheckIntervalMinutes", 30)));

        return services;
    }
}
