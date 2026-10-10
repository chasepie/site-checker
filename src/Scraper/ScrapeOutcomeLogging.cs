using Microsoft.Extensions.Logging;

namespace SiteChecker.Scraper;

public static class ScrapeOutcomeLogging
{
    extension(ILogger logger)
    {
        /// <summary>
        /// Starts the scope a Scraper's own log entries are written under: the Site and the Site
        /// Check, the same in the worker and when the app logs them again.
        /// </summary>
        public IDisposable? BeginScrapeScope(ScrapeRequest request)
            => logger.BeginScope(new Dictionary<string, object?>
            {
                ["SiteId"] = request.Site.Id,
                ["SiteName"] = request.Site.Name,
                ["SiteCheckId"] = request.SiteCheckId,
            });

        /// <summary>
        /// Logs how a scrape ended: an error for an Unexpected Failure, a warning for a Known
        /// Failure, and information for success.
        /// </summary>
        public void LogScrapeOutcome(ScrapeRequest request, ScrapeResult result)
        {
            if (result.Outcome == ScrapeOutcome.UnexpectedFailure)
            {
                logger.LogError("Scraping {SiteName} (Site Check {SiteCheckId}) failed: {Message}{ExceptionDetail}",
                    request.Site.Name, request.SiteCheckId, result.Message,
                    result.ExceptionDetail is { } detail ? Environment.NewLine + detail : string.Empty);
            }
            else if (result.Outcome == ScrapeOutcome.KnownFailure)
            {
                logger.LogWarning("Scraping {SiteName} (Site Check {SiteCheckId}) found a Known Failure: {Message}",
                    request.Site.Name, request.SiteCheckId, result.Message);
            }
            else
            {
                logger.LogInformation("Scraped {SiteName} (Site Check {SiteCheckId}) in {Duration}.",
                    request.Site.Name, request.SiteCheckId, result.Duration);
            }
        }
    }
}
