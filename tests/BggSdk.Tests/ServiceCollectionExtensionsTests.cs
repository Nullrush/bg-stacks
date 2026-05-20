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
    public void AddBggClient_CalledTwiceWithDifferentTokens_DoesNotThrow()
    {
        // Callers should be able to register once without error; this guards against
        // internal state that would blow up on double-registration.
        var services = new ServiceCollection();

        var act = () =>
        {
            services.AddBggClient("token-a");
            services.AddBggClient("token-b");
        };

        act.Should().NotThrow();
    }
}
