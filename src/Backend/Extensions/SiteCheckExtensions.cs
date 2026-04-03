using NetCord.Rest;
using SiteChecker.Backend.Notifiers.Pushover;
using SiteChecker.Domain.Entities;
using SiteChecker.Domain.Enums;

namespace SiteChecker.Backend.Extensions;

public static class SiteCheckExtensions
{
    extension(SiteCheck siteCheck)
    {
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
