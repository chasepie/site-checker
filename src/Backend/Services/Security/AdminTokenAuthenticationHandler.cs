using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace SiteChecker.Backend.Services.Security;

/// <summary>
/// Authenticates a request carrying the admin token as <c>Authorization: Bearer {token}</c>. When
/// no token is required (Development without <c>ADMIN_TOKEN</c>), every request is authenticated.
/// </summary>
public sealed class AdminTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    AdminToken adminToken)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    private readonly AdminToken _adminToken = adminToken;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!_adminToken.IsRequired)
        {
            return Task.FromResult(AuthenticateResult.Success(CreateTicket()));
        }

        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization.ToString(), out var header)
            || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(header.Parameter))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        return Task.FromResult(_adminToken.Matches(header.Parameter)
            ? AuthenticateResult.Success(CreateTicket())
            : AuthenticateResult.Fail("The admin token is wrong."));
    }

    private static AuthenticationTicket CreateTicket()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], AdminToken.SchemeName);
        return new AuthenticationTicket(new ClaimsPrincipal(identity), AdminToken.SchemeName);
    }
}
