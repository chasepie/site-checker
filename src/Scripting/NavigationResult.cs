using Microsoft.Playwright;

namespace SiteChecker.Scripting;

/// <summary>
/// The outcome of navigating to the Site's URL: the response, or the error if there was none.
/// </summary>
public sealed class NavigationResult
{
    private NavigationResult(IResponse? response, Exception? error)
    {
        Response = response;
        Error = error;
    }

    /// <summary>
    /// The main resource's response, with its status code and headers. <c>null</c> when navigation
    /// failed, or for a navigation that produced no response (such as a same-document one).
    /// </summary>
    public IResponse? Response { get; }

    /// <summary>
    /// Why navigation failed (a connection reset, a load timeout, ...), or <c>null</c> if it didn't.
    /// </summary>
    public Exception? Error { get; }

    /// <summary>
    /// Whether navigation completed with a successful (2xx) status, or with no response at all.
    /// </summary>
    public bool Succeeded => Error is null && (Response is null || Response.Ok);

    /// <summary>
    /// A navigation that completed, successfully or not, with the given response.
    /// </summary>
    public static NavigationResult FromResponse(IResponse? response) => new(response, null);

    /// <summary>
    /// A navigation that failed without a response.
    /// </summary>
    public static NavigationResult FromError(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(null, error);
    }

    /// <summary>
    /// Throws unless navigation <see cref="Succeeded"/>, turning a failed navigation into an
    /// Unexpected Failure.
    /// </summary>
    /// <exception cref="NavigationFailedException">Navigation failed or returned a non-2xx status.</exception>
    public void EnsureSucceeded()
    {
        if (Error is not null)
        {
            throw new NavigationFailedException($"Navigation failed: {Error.Message}", Error);
        }

        if (Response is { Ok: false })
        {
            throw new NavigationFailedException(
                $"Navigation to {Response.Url} returned HTTP {Response.Status} {Response.StatusText}".TrimEnd());
        }
    }
}

/// <summary>
/// Thrown by <see cref="NavigationResult.EnsureSucceeded"/> when navigation failed.
/// </summary>
public sealed class NavigationFailedException(string message, Exception? innerException = null)
    : Exception(message, innerException);
