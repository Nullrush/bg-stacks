using Microsoft.Extensions.DependencyInjection;

namespace BggSdk;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="BggClient"/> as a typed HTTP client with a base address
    /// of https://boardgamegeek.com/xmlapi2/ and a <see cref="BggAuthHandler"/>
    /// that injects the supplied <paramref name="bearerToken"/>.
    /// Returns the <see cref="IHttpClientBuilder"/> so callers can chain
    /// additional configuration (e.g. Polly retry policies).
    /// </summary>
    public static IHttpClientBuilder AddBggClient(
        this IServiceCollection services,
        string bearerToken)
    {
        ArgumentNullException.ThrowIfNull(bearerToken);
        return services
            .AddHttpClient<BggClient>(client =>
                client.BaseAddress = new Uri("https://boardgamegeek.com/xmlapi2/"))
            .AddHttpMessageHandler(() => new BggAuthHandler(bearerToken));
    }
}
