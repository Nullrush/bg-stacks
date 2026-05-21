using BgStacks.Web.Application.Events;
using BgStacks.Web.Domain.Events;
using BgStacks.Web.Domain.Tags;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
