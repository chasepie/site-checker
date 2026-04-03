using System.ComponentModel.DataAnnotations;
using SiteChecker.Domain.ValueObjects;

namespace SiteChecker.Domain.DTOs;

public class SiteUpdate
{
    public const int KnownFailuresThresholdDefault = 5;

    public int Id { get; set; }
    public required string Name { get; set; }
    public required Uri Url { get; set; }

    public bool UseVpn { get; set; } = false;
    public bool AlwaysTakeScreenshot { get; set; } = false;

    [Range(1, 999)]
    public int KnownFailuresThreshold { get; set; } = KnownFailuresThresholdDefault;

    public SiteSchedule Schedule { get; set; } = new();
    public PushoverConfig PushoverConfig { get; set; } = new();
    public DiscordConfig DiscordConfig { get; set; } = new();
}
