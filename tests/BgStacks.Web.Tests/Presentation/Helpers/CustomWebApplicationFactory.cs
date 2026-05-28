using BgStacks.Web.Application.Events;
using BgStacks.Web.Domain.Events;
using BgStacks.Web.Domain.Tags;
using BgStacks.Web.Infrastructure.Cache;
using BgStacks.Web.Infrastructure.Events;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BgStacks.Web.Tests.Presentation.Helpers;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public InMemoryTagsRepository TagsRepository { get; } = new();
    public InMemoryEventRepository EventRepository { get; } = new();
    public InMemoryBggGeeklistService BggGeeklistService { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Provide stub connection strings so ValidateOnStart() passes at test host startup.
        // Cosmos repositories are replaced by in-memory fakes below; the BlobServiceClient
        // and BlobDistributedCache are not replaced but are never exercised in tests
        // (the Testing environment skips container creation and no test triggers a cache call).
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{CosmosOptions.SectionName}:ConnectionString"] = "AccountEndpoint=https://fake.documents.azure.com:443/;AccountKey=fake==",
                [$"{BlobOptions.SectionName}:ConnectionString"] = "DefaultEndpointsProtocol=https;AccountName=fake;AccountKey=fake==;EndpointSuffix=core.windows.net",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITagsRepository>();
            services.RemoveAll<IEventRepository>();
            services.RemoveAll<IBggGeeklistService>();

            services.AddScoped<ITagsRepository>(_ => TagsRepository);
            services.AddScoped<IEventRepository>(_ => EventRepository);
            services.AddScoped<IBggGeeklistService>(_ => BggGeeklistService);

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultSignInScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName, _ => { });
        });
    }
}
