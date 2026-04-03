using System.Diagnostics.CodeAnalysis;
using SiteChecker.Domain.Exceptions;

namespace SiteChecker.Domain;

public static class ScrapeResultExtensions
{
    extension(IScrapeResult result)
    {
        public bool IsSuccess([NotNullWhen(true)] out SuccessScrapeResult? success)
        {
            success = null;
            if (result.WasSuccessful && result is SuccessScrapeResult succ)
            {
                success = succ;
            }

            return success != null;
        }

        public bool IsFailure([NotNullWhen(true)] out FailureScrapeResult? failure)
        {
            failure = null;
            if (!result.WasSuccessful && result is FailureScrapeResult fail)
            {
                failure = fail;
            }

            return failure != null;
        }
    }
}

public interface IScrapeResult
{
    bool WasSuccessful { get; }
    byte[]? Screenshot { get; set; }
}

public class SuccessScrapeResult : IScrapeResult
{
    public bool WasSuccessful => true;
    public byte[]? Screenshot { get; set; }
    public required string Content { get; set; }
}

public class FailureScrapeResult : IScrapeResult
{
    public bool WasSuccessful => false;
    public byte[]? Screenshot { get; set; }
    public required string ErrorMessage { get; set; }
    public ScraperException? Exception { get; set; }

    public static FailureScrapeResult FromException(ScraperException ex)
    {
        var message = ex.Message;
        if (ex.InnerException != null)
        {
            message += $" | Inner Exception: {ex.InnerException.Message}";
        }

        return new FailureScrapeResult
        {
            ErrorMessage = message,
            Exception = ex
        };
    }
}
