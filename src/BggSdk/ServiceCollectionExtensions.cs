using Microsoft.Extensions.DependencyInjection;

namespace BggSdk;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="BggClient"/> as a typed HTTP client with a base address
    /// of https://boardgamegeek.com/xmlapi2/ (or <paramref name="baseAddress"/> if provided)
    /// and a <see cref="BggAuthHandler"/> that injects the supplied <paramref name="bearerToken"/>.
    /// Returns the <see cref="IHttpClientBuilder"/> so callers can chain
    /// additional configuration (e.g. Polly retry policies).
    /// </summary>
    /// <remarks>
    /// Calling this method more than once appends additional <see cref="BggAuthHandler"/>
    /// instances to the pipeline. Each handler sets the <c>Authorization</c> header, so the
    /// last-registered token wins and earlier tokens are silently discarded. Register once
    /// unless you intentionally want to replace the token.
    /// </remarks>
    public static IHttpClientBuilder AddBggClient(
        this IServiceCollection services,
        string bearerToken,
        string? baseAddress = null)
    {
        ArgumentNullException.ThrowIfNull(bearerToken);
        var rawBase = baseAddress ?? "https://boardgamegeek.com/xmlapi2/";
        if (!Uri.TryCreate(rawBase, UriKind.Absolute, out var uri))
            throw new ArgumentException(
                $"baseAddress must be an absolute URI; got: \"{rawBase}\"",
                nameof(baseAddress));
        if (!uri.AbsoluteUri.EndsWith('/'))
            uri = new Uri(uri.AbsoluteUri + '/');
        return services
            .AddHttpClient<BggClient>(client => client.BaseAddress = uri)
            .AddHttpMessageHandler(() => new BggAuthHandler(bearerToken));
    }
}
