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
        // Real connections are not used — repositories and cache are replaced by in-memory fakes.
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{CosmosOptions.SectionName}:ConnectionString"] = "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b5n/rCSlFy37cMOjggEAAAAAAAAAAAA==",
                [$"{BlobOptions.SectionName}:ConnectionString"] = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;",
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
