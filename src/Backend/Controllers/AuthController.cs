using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiteChecker.Backend.Models;
using SiteChecker.Backend.Services.Security;
using SiteChecker.Backend.Services.SignalR;

namespace SiteChecker.Backend.Controllers;

/// <summary>
/// The login. There's one password (<c>ADMIN_PASSWORD</c>) and no users; a login is a session
/// cookie that every other endpoint requires. Only the session check and the login itself are open;
/// logging out needs a login, like everything else.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public sealed class AuthController(
    AdminPassword adminPassword,
    IAntiforgery antiforgery,
    HubConnections hubConnections,
    LoginThrottle loginThrottle)
    : ControllerBase
{
    private readonly AdminPassword _adminPassword = adminPassword;
    private readonly IAntiforgery _antiforgery = antiforgery;
    private readonly HubConnections _hubConnections = hubConnections;
    private readonly LoginThrottle _loginThrottle = loginThrottle;

    /// <summary>
    /// Whether this browser is logged in. Also issues the antiforgery token for its next writes.
    /// </summary>
    [HttpGet("session")]
    [AllowAnonymous]
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
    /// Logs in with the password. A wrong one gets 401; too many attempts in a minute, from this
    /// client or from all of them, get 429.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    // Forging a login to the only account gains an attacker nothing, and requiring a token here
    // would mean fetching one before every login.
    [IgnoreAntiforgeryToken]
    public async Task<ActionResult> Login([FromBody] LoginRequest request)
    {
        if (!_adminPassword.IsRequired)
        {
            return NoContent();
        }
        // Counted here rather than by the rate limiting middleware, which would also count requests
        // that can't log in, such as a cross-site form post that MVC then refuses with 415.
        if (!_loginThrottle.TryAttempt(HttpContext.Connection.RemoteIpAddress))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests);
        }
        if (!_adminPassword.Matches(request.Password.Trim()))
        {
            return Unauthorized();
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "admin"),
                new Claim(AdminPassword.SessionStampClaim, _adminPassword.SessionStamp!),
            ],
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
    /// Ends this browser's session, and closes every live-update connection, which would otherwise
    /// carry on with the login they started with. Browsers still logged in reconnect. Needs a login,
    /// so a stranger can't close everyone's connections.
    /// </summary>
    [HttpPost("logout")]
    public async Task<ActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        _hubConnections.CloseAll();
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
