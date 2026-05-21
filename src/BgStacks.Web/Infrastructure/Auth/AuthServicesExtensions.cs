using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

namespace BgStacks.Web.Infrastructure.Auth;

public static class AuthServicesExtensions
{
    public static IServiceCollection AddAuthServices(
        this IServiceCollection services,
        IConfiguration configuration,
        TokenCredential credential)
    {
        var authBuilder = services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        })
        .AddCookie(options =>
        {
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.LoginPath = "/.auth/login/google";
            options.Events.OnRedirectToLogin = ctx =>
            {
                ctx.Response.StatusCode = 401;
                return Task.CompletedTask;
            };
        });

        var googleClientId = configuration["Auth:Google:ClientId"];
        if (!string.IsNullOrEmpty(googleClientId))
            authBuilder.AddGoogle(options =>
            {
                options.ClientId = googleClientId;
                options.ClientSecret = configuration["Auth:Google:ClientSecret"]!;
                options.Events.OnCreatingTicket = ctx =>
                {
                    if (ctx.User.TryGetProperty("picture", out var pic) && pic.ValueKind == System.Text.Json.JsonValueKind.String)
                        ctx.Identity!.AddClaim(new System.Security.Claims.Claim("picture", pic.GetString()!));
                    ctx.Identity!.AddClaim(new System.Security.Claims.Claim("idp", "google"));
                    return Task.CompletedTask;
                };
            });

        var facebookClientId = configuration["Auth:Facebook:ClientId"];
        if (!string.IsNullOrEmpty(facebookClientId))
            authBuilder.AddFacebook(options =>
            {
                options.ClientId = facebookClientId;
                options.ClientSecret = configuration["Auth:Facebook:ClientSecret"]!;
                options.Events.OnCreatingTicket = ctx =>
                {
                    ctx.Identity!.AddClaim(new System.Security.Claims.Claim("idp", "facebook"));
                    return Task.CompletedTask;
                };
            });

        var discordClientId = configuration["Auth:Discord:ClientId"];
        if (!string.IsNullOrEmpty(discordClientId))
            authBuilder.AddDiscord(options =>
            {
                options.ClientId = discordClientId;
                options.ClientSecret = configuration["Auth:Discord:ClientSecret"]!;
                options.Scope.Add("identify");
                options.Scope.Add("email");
                options.Events.OnCreatingTicket = ctx =>
                {
                    if (ctx.User.TryGetProperty("avatar", out var avatar) && avatar.ValueKind == System.Text.Json.JsonValueKind.String)
                        ctx.Identity!.AddClaim(new System.Security.Claims.Claim("avatar", avatar.GetString()!));
                    ctx.Identity!.AddClaim(new System.Security.Claims.Claim("idp", "discord"));
                    return Task.CompletedTask;
                };
            });

        var dpBlobUri = configuration["DataProtection:BlobUri"];
        var dpKeyUri = configuration["DataProtection:KeyVaultKeyUri"];
        if (dpBlobUri is not null && dpKeyUri is not null)
        {
            var dpBlob = new BlobClient(new Uri(dpBlobUri), credential);
            services.AddDataProtection()
                .PersistKeysToAzureBlobStorage(dpBlob)
                .ProtectKeysWithAzureKeyVault(new Uri(dpKeyUri), credential);
        }

        return services;
    }
}
