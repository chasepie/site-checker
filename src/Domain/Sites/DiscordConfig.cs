namespace SiteChecker.Domain.Sites;

public class DiscordConfig
{
    public bool SuccessEnabled { get; set; } = false;
    public bool FailureEnabled { get; set; } = false;
    public ulong? ChannelId { get; set; } = null;

    public void Update(DiscordConfig config)
    {
        SuccessEnabled = config.SuccessEnabled;
        FailureEnabled = config.FailureEnabled;
        ChannelId = config.ChannelId;
    }
}
