using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace SiteChecker.Backend.Services.Security;

/// <summary>
/// Requires a valid antiforgery token on every controller request that can change something
/// (anything but GET, HEAD, OPTIONS and TRACE), unless the action is marked
/// <see cref="IgnoreAntiforgeryTokenAttribute"/>. MVC's own <c>AutoValidateAntiforgeryToken</c>
/// needs the Razor view services, which this API doesn't register.
/// </summary>
/// <remarks>
/// TODO(.NET 11): The automatic CSRF protection checks <c>Sec-Fetch-Site</c> (else
/// <c>Origin</c>) on every request without tokens, but only records a verdict, which nothing
/// enforces for JSON or bodiless actions like ours. Reject when that verdict
/// (<c>IAntiforgeryValidationFeature</c>) is invalid instead of checking the token, then remove the
/// token handling (see <c>AddLogin</c> and <c>AuthController.IssueAntiforgeryToken</c>). Keep this
/// filter's method and opt-out rules.
/// </remarks>
public sealed class ValidateAntiforgeryFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    private readonly IAntiforgery _antiforgery = antiforgery;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method))
        {
            return;
        }
        // The last antiforgery attribute is the most specific (the action's over its controller's),
        // as in MVC, so an action can require the token that its controller ignores.
        var policy = context.HttpContext.GetEndpoint()?.Metadata
            .LastOrDefault(item => item is IAntiforgeryPolicy or IAntiforgeryMetadata);
        if (policy is IgnoreAntiforgeryTokenAttribute or IAntiforgeryMetadata { RequiresValidation: false })
        {
            return;
        }

        if (!await _antiforgery.IsRequestValidAsync(context.HttpContext))
        {
            context.Result = new BadRequestObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "The antiforgery token is missing or invalid. Reload the page and try again.",
            });
        }
    }
}
