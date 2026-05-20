using BggSdk;
using Microsoft.Extensions.DependencyInjection;

namespace BggSdk.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBggClient_RegistersBggClient()
    {
        var services = new ServiceCollection();

        services.AddBggClient("test-token");

        var provider = services.BuildServiceProvider();
        var client = provider.GetService<BggClient>();
        client.Should().NotBeNull();
    }

    [Fact]
    public void AddBggClient_ReturnsIHttpClientBuilder()
    {
        var services = new ServiceCollection();

        var builder = services.AddBggClient("test-token");

        builder.Should().NotBeNull();
        builder.Should().BeAssignableTo<IHttpClientBuilder>();
    }

    [Fact]
    public void AddBggClient_CustomBaseAddress_UsesProvidedAddress()
    {
        var services = new ServiceCollection();

        services.AddBggClient("token", "https://staging.example.com/api/");

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var httpClient = factory.CreateClient(nameof(BggClient));
        httpClient.BaseAddress.Should().Be(new Uri("https://staging.example.com/api/"));
    }

    [Fact]
    public void AddBggClient_CustomBaseAddress_NormalizesTrailingSlash()
    {
        var services = new ServiceCollection();

        services.AddBggClient("token", "https://staging.example.com/api"); // no trailing slash

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var httpClient = factory.CreateClient(nameof(BggClient));
        httpClient.BaseAddress.Should().Be(new Uri("https://staging.example.com/api/"));
    }

    [Fact]
    public void AddBggClient_CalledTwiceWithDifferentTokens_DoesNotThrow()
    {
        // Guards against double-registration blowing up on internal state;
        // calling AddBggClient twice with different tokens must not throw.
        var services = new ServiceCollection();

        var act = () =>
        {
            services.AddBggClient("token-a");
            services.AddBggClient("token-b");
        };

        act.Should().NotThrow();
    }
}
