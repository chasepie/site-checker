using SiteChecker.Domain.DTOs;
using SiteChecker.Domain.ValueObjects;

namespace SiteChecker.Domain.Entities;

public class Site : IEntityWithId
{
    public const int KnownFailuresThresholdDefault = 5;

    public int Id { get; set; }
    public required string Name { get; set; }
    public required Uri Url { get; set; }

    public bool UseVpn { get; set; } = false;
    public bool AlwaysTakeScreenshot { get; set; } = false;

    public int KnownFailuresThreshold { get; set; } = KnownFailuresThresholdDefault;

    public SiteSchedule Schedule { get; set; } = new();
    public PushoverConfig PushoverConfig { get; set; } = new();
    public DiscordConfig DiscordConfig { get; set; } = new();

    public required string ScraperId { get; set; }

    public void Update(SiteUpdate update)
    {
        Name = update.Name;
        Url = update.Url;
        UseVpn = update.UseVpn;
        AlwaysTakeScreenshot = update.AlwaysTakeScreenshot;
        Schedule.Update(update.Schedule);
        PushoverConfig.Update(update.PushoverConfig);
        DiscordConfig.Update(update.DiscordConfig);
    }
}
