using SiteChecker.Database.Model;
using SiteChecker.Scraper;

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
        /// <summary>
        /// Records a scrape's outcome. Requested Actions are recorded by the runner, which decides
        /// which of them to carry out.
        /// </summary>
        public void Update(ScrapeResult result, DateTime completedDate)
        {
            switch (result.Outcome)
            {
                case ScrapeOutcome.Succeeded:
                    siteCheck.Status = CheckStatus.Succeeded;
                    siteCheck.Value = result.Content;
                    siteCheck.FailureKind = null;
                    break;

                case ScrapeOutcome.KnownFailure:
                    siteCheck.Status = CheckStatus.Failed;
                    siteCheck.Value = result.Message;
                    siteCheck.FailureKind = FailureKind.Known;
                    break;

                case ScrapeOutcome.UnexpectedFailure:
                    siteCheck.Status = CheckStatus.Failed;
                    siteCheck.Value = result.Message;
                    siteCheck.FailureKind = FailureKind.Unexpected;

                    var exceptionType = result.ExceptionType;
                    if (!string.IsNullOrEmpty(exceptionType))
                    {
                        // Create a new dictionary to ensure EF Core detects the change
                        siteCheck.Metadata = new(siteCheck.Metadata)
                        {
                            [EXCEPTION_TYPE] = exceptionType
                        };
                    }
                    break;

                default:
                    throw new InvalidOperationException($"Unknown scrape outcome {result.Outcome}");
            }

            siteCheck.CompletedDate = completedDate;
        }
    }
}
