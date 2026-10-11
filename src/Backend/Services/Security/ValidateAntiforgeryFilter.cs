using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SiteChecker.Backend.Services.Security;

/// <summary>
/// Requires a valid antiforgery token on every controller request that can change something
/// (anything but GET, HEAD, OPTIONS and TRACE), unless the action is marked
/// <see cref="IgnoreAntiforgeryTokenAttribute"/>. MVC's own <c>AutoValidateAntiforgeryToken</c>
/// needs the Razor view services, which this API doesn't register.
/// </summary>
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
        var metadata = context.HttpContext.GetEndpoint()?.Metadata;
        if (metadata?.GetMetadata<IgnoreAntiforgeryTokenAttribute>() is not null
            || metadata?.GetMetadata<IAntiforgeryMetadata>() is { RequiresValidation: false })
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
