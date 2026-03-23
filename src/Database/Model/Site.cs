using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace SiteChecker.Database.Model;

public class SiteUpdate : IEntityWithId
{
    internal const int KNOWN_FAILURES_THRESHOLD_DEFAULT = 5;

    public int Id { get; set; }
    public required string Name { get; set; }
    public required Uri Url { get; set; }

    public bool UseVpn { get; set; } = false;
    public bool AlwaysTakeScreenshot { get; set; } = false;

    [Range(1, 999)]
    public int KnownFailuresThreshold { get; set; } = KNOWN_FAILURES_THRESHOLD_DEFAULT;

    public SiteSchedule Schedule { get; set; } = new();
    public PushoverConfig PushoverConfig { get; set; } = new();
    public DiscordConfig DiscordConfig { get; set; } = new();
}

[Index(nameof(ScraperId), IsUnique = true)]
public class Site : SiteUpdate
{
    public required string ScraperId { get; set; }

    public ICollection<SiteCheck> SiteChecks { get; set; } = [];

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

public class SiteSchedule
{
    public bool Enabled { get; set; } = false;
    public TimeOnly? Start { get; set; } = null;
    public TimeOnly? End { get; set; } = null;
    public uint? Interval { get; set; } = null;

    public void Update(SiteSchedule schedule)
    {
        Enabled = schedule.Enabled;
        Start = schedule.Start;
        End = schedule.End;
        Interval = schedule.Interval;
    }
}
