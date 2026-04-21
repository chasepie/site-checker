using System.Diagnostics.CodeAnalysis;
using NetCord.Rest;
using SiteChecker.Database.Model;
using SiteChecker.Backend.Notifiers.Pushover;
using SiteChecker.Domain.SiteChecks;
using SiteChecker.Scraper;
using SiteChecker.Scraper.Exceptions;

namespace SiteChecker.Backend.Extensions;

public static class SiteCheckExtensions
{
    private const string SCRAPE_RESULT = nameof(SCRAPE_RESULT);
    private const string EXCEPTION_TYPE = nameof(EXCEPTION_TYPE);

    extension(SiteCheck siteCheck)
    {
        public void Update(IScrapeResult result)
        {
            if (result.IsFailure(out var failure))
            {
                siteCheck.Status = CheckStatus.Failed;
                siteCheck.Value = failure.ErrorMessage;
            }
            else if (result.IsSuccess(out var success))
            {
                siteCheck.Status = CheckStatus.Done;
                siteCheck.Value = success.Content;
            }
            else
            {
                throw new InvalidOperationException("Unknown scrape result type");
            }

            siteCheck.DoneDate = DateTime.UtcNow;
            siteCheck.MetadataLocal[SCRAPE_RESULT] = result;
            if (result.IsFailure(out var failureResult) && failureResult.Exception != null)
            {
                var exceptionType = failureResult.Exception.GetType().FullName;
                if (!string.IsNullOrEmpty(exceptionType))
                {
                    // Create a new dictionary to ensure EF Core detects the change
                    siteCheck.Metadata = new(siteCheck.Metadata)
                    {
                        [EXCEPTION_TYPE] = exceptionType
                    };
                }
            }
        }

        public IScrapeResult? GetScrapeResult()
        {
            if (siteCheck.MetadataLocal.TryGetValue(SCRAPE_RESULT, out var result)
                && result is IScrapeResult scrapeResult)
            {
                return scrapeResult;
            }

            return null;
        }

        public bool TryGetFailureResult([NotNullWhen(true)] out FailureScrapeResult? failureResult)
        {
            var scrapeResult = siteCheck.GetScrapeResult();
            if (scrapeResult is FailureScrapeResult failure)
            {
                failureResult = failure;
                return true;
            }

            failureResult = null;
            return false;
        }

        public bool IsKnownExceptionFailure()
        {
            if (siteCheck.Status != CheckStatus.Failed)
            {
                return false;
            }

            if (siteCheck.TryGetFailureResult(out var failureResult))
            {
                return failureResult.Exception is KnownScraperException;
            }

            if (siteCheck.Metadata.TryGetValue(EXCEPTION_TYPE, out var exceptionTypeObj)
                && exceptionTypeObj is string exceptionType
                && !string.IsNullOrEmpty(exceptionType))
            {
                return exceptionType.EndsWith(nameof(KnownScraperException))
                    || exceptionType.EndsWith(nameof(AccessDeniedScraperException))
                    || exceptionType.EndsWith(nameof(BlankPageScraperException));
            }

            return false;
        }

        private string GetTitle() => siteCheck.Site.Name + (siteCheck.IsSuccess ? " Updated" : " Check Failed");

        private string GetMessage() => siteCheck.Value ?? "[No content]";

        public PushoverContents? ToPushoverContents()
        {
            var priority = siteCheck.Status switch
            {
                CheckStatus.Done => siteCheck.Site.PushoverConfig.SuccessPriority,
                CheckStatus.Failed => siteCheck.Site.PushoverConfig.FailurePriority,
                _ => null,
            };
            if (priority == null)
            {
                return null;
            }

            return new()
            {
                Title = siteCheck.GetTitle(),
                Message = siteCheck.GetMessage(),
                Priority = (int)priority,
                Url = siteCheck.Site.Url.ToString(),
                Attachment = siteCheck.Screenshot?.Data,
            };
        }

        public EmbedProperties ToDiscordEmbed()
        {
            return new EmbedProperties()
            {
                Title = siteCheck.GetTitle(),
                Description = siteCheck.GetMessage(),
                Url = siteCheck.Site.Url.ToString(),
            };
        }
    }
}
