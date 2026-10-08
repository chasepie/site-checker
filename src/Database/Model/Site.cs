using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

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

    /// <summary>
    /// How long a scrape may run, in seconds. <c>null</c> uses <c>SCRAPE_TIMEOUT</c>.
    /// </summary>
    public int? TimeoutSeconds { get; set; }

    public SiteSchedule Schedule { get; set; } = new();
    public PushoverConfig PushoverConfig { get; set; } = new();
    public DiscordConfig DiscordConfig { get; set; } = new();
}

public class Site : SiteUpdate
{
    public ScraperDefinition Scraper { get; set; } = new();

    /// <summary>
    /// The Script Scraper's source. Loaded only where it's needed, and never serialized with the Site.
    /// </summary>
    [JsonIgnore]
    public SiteScript? SiteScript { get; set; }

    public ICollection<SiteCheck> SiteChecks { get; set; } = [];

    public void Update(SiteUpdate update)
    {
        Name = update.Name;
        Url = update.Url;
        UseVpn = update.UseVpn;
        AlwaysTakeScreenshot = update.AlwaysTakeScreenshot;
        KnownFailuresThreshold = update.KnownFailuresThreshold;
        TimeoutSeconds = update.TimeoutSeconds;
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
