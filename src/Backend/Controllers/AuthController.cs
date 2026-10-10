using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SiteChecker.Backend.Models;
using SiteChecker.Backend.Services.Security;

namespace SiteChecker.Backend.Controllers;

/// <summary>
/// The login. There's one password (<c>ADMIN_PASSWORD</c>) and no users; a login is a session
/// cookie that every other endpoint requires.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[AllowAnonymous]
public sealed class AuthController(
    AdminPassword adminPassword,
    IAntiforgery antiforgery)
    : ControllerBase
{
    private readonly AdminPassword _adminPassword = adminPassword;
    private readonly IAntiforgery _antiforgery = antiforgery;

    /// <summary>
    /// Whether this browser is logged in. Also issues the antiforgery token for its next writes.
    /// </summary>
    [HttpGet("session")]
    public ActionResult<SessionInfo> GetSession()
    {
        IssueAntiforgeryToken();
        return new SessionInfo
        {
            LoginRequired = _adminPassword.IsRequired,
            LoggedIn = !_adminPassword.IsRequired || User.Identity?.IsAuthenticated == true,
        };
    }

    /// <summary>
    /// Logs in with the password. A wrong one gets 401; too many attempts in a minute get 429.
    /// </summary>
    [HttpPost("login")]
    // Forging a login to the only account gains an attacker nothing, and requiring a token here
    // would mean fetching one before every login.
    [IgnoreAntiforgeryToken]
    [EnableRateLimiting(SecurityExtensions.LoginRateLimitPolicy)]
    public async Task<ActionResult> Login([FromBody] LoginRequest request)
    {
        if (!_adminPassword.IsRequired)
        {
            return NoContent();
        }
        if (!_adminPassword.Matches(request.Password.Trim()))
        {
            return Unauthorized();
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "admin")],
            CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = true });

        // Antiforgery tokens are bound to the user, so the anonymous one from before is now stale.
        HttpContext.User = principal;
        IssueAntiforgeryToken();
        return NoContent();
    }

    /// <summary>
    /// Ends this browser's session.
    /// </summary>
    [HttpPost("logout")]
    public async Task<ActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    /// <summary>
    /// Sets the antiforgery cookie pair: the HttpOnly cookie token, and the request token in a
    /// cookie the page reads and sends back as <see cref="SecurityExtensions.AntiforgeryHeaderName"/>.
    /// </summary>
    private void IssueAntiforgeryToken()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        Response.Cookies.Append(SecurityExtensions.AntiforgeryCookieName, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            SameSite = SameSiteMode.Strict,
            Secure = Request.IsHttps,
            Path = "/",
        });
    }
}
