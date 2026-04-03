using NetCord.Rest;
using SiteChecker.Backend.Notifiers.Pushover;
using SiteChecker.Domain.Entities;
using SiteChecker.Domain.Enums;
using SiteChecker.Domain;
using SiteChecker.Domain.Exceptions;

namespace SiteChecker.Backend.Extensions;

public static class SiteCheckExtensions
{
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

        public bool IsKnownExceptionFailure()
        {
            if (siteCheck.Status != CheckStatus.Failed)
            {
                return false;
            }

            if (siteCheck.Metadata.TryGetValue(EXCEPTION_TYPE, out var exceptionType)
                && !string.IsNullOrEmpty(exceptionType))
            {
                return exceptionType.EndsWith(nameof(KnownScraperException))
                    || exceptionType.EndsWith(nameof(AccessDeniedScraperException))
                    || exceptionType.EndsWith(nameof(BlankPageScraperException));
            }

            return false;
        }

        public PushoverContents? ToPushoverContents(Site site, SiteCheckScreenshot? screenshot)
        {
            var priority = siteCheck.Status switch
            {
                CheckStatus.Done => site.PushoverConfig.SuccessPriority,
                CheckStatus.Failed => site.PushoverConfig.FailurePriority,
                _ => null,
            };
            if (priority == null)
            {
                return null;
            }

            return new()
            {
                Title = siteCheck.GetTitle(site),
                Message = siteCheck.GetMessage(),
                Priority = (int)priority,
                Url = site.Url.ToString(),
                Attachment = screenshot?.Data,
            };
        }

        public EmbedProperties ToDiscordEmbed(Site site)
        {
            return new EmbedProperties()
            {
                Title = siteCheck.GetTitle(site),
                Description = siteCheck.GetMessage(),
                Url = site.Url.ToString(),
            };
        }

        private string GetTitle(Site site) => site.Name + (siteCheck.IsSuccess ? " Updated" : " Check Failed");

        private string GetMessage() => siteCheck.Value ?? "[No content]";
    }
}
