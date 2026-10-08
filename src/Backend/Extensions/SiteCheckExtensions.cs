using SiteChecker.Database.Model;
using SiteChecker.Scraper;
using SiteChecker.Scraper.Exceptions;

namespace SiteChecker.Backend.Extensions;

public static class SiteCheckExtensions
{
    /// <summary>
    /// Diagnostic only: the full type name of the exception behind a failure. Nothing reads it;
    /// use <see cref="SiteCheck.FailureKind"/> instead.
    /// </summary>
    private const string EXCEPTION_TYPE = nameof(EXCEPTION_TYPE);

    extension(SiteCheck siteCheck)
    {
        public void Update(IScrapeResult result, DateTime completedDate)
        {
            if (result.IsFailure(out var failure))
            {
                siteCheck.Status = CheckStatus.Failed;
                siteCheck.Value = failure.ErrorMessage;
                siteCheck.FailureKind = failure.Exception is KnownScraperException
                    ? FailureKind.Known
                    : FailureKind.Unexpected;

                var exceptionType = failure.Exception?.GetType().FullName;
                if (!string.IsNullOrEmpty(exceptionType))
                {
                    // Create a new dictionary to ensure EF Core detects the change
                    siteCheck.Metadata = new(siteCheck.Metadata)
                    {
                        [EXCEPTION_TYPE] = exceptionType
                    };
                }
            }
            else if (result.IsSuccess(out var success))
            {
                siteCheck.Status = CheckStatus.Succeeded;
                siteCheck.Value = success.Content;
                siteCheck.FailureKind = null;
            }
            else
            {
                throw new InvalidOperationException("Unknown scrape result type");
            }

            siteCheck.CompletedDate = completedDate;
        }
    }
}
