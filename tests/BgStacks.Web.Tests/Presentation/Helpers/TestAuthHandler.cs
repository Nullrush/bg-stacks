using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BgStacks.Web.Tests.Presentation.Helpers;

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestScheme";

    public static ClaimsPrincipal? CurrentUser { get; set; }

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (CurrentUser is null)
            return Task.FromResult(AuthenticateResult.NoResult());
        var ticket = new AuthenticationTicket(CurrentUser, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
