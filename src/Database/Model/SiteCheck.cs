using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace SiteChecker.Database.Model;

public enum CheckStatus
{
    // Values are pinned because they are persisted; 0 was the retired "Created" status.
    Queued = 1,
    Checking = 2,
    Succeeded = 3,
    Failed = 4,
}

/// <summary>
/// Why a Failed Site Check failed. Values are pinned because they are persisted.
/// </summary>
public enum FailureKind
{
    /// <summary>An unexpected error.</summary>
    Unexpected = 1,

    /// <summary>A Known Failure: a recognised condition such as access denied or a blank page.</summary>
    Known = 2,
}

// Serves the Site Check queue: the oldest Queued check (by StartDate, then Id) is claimed next.
[Index(nameof(Status), nameof(StartDate))]
public class SiteCheck : IEntityWithId
{
    public required int Id { get; set; }

    public required string? Value { get; set; }

    public string? VpnLocationId { get; set; }

    public CheckStatus Status { get; set; } = CheckStatus.Queued;

    /// <summary>
    /// Set only when <see cref="Status"/> is <see cref="CheckStatus.Failed"/>.
    /// </summary>
    public FailureKind? FailureKind { get; set; }

    /// <summary>
    /// When this check's Failing notification reached at least one Notification Channel. A
    /// Failing Run is reported once any of its checks has this set.
    /// </summary>
    public DateTime? ReportedAt { get; set; }

    public required DateTime StartDate { get; set; }

    public required DateTime? CompletedDate { get; set; }

    public required int SiteId { get; set; }

    [JsonIgnore]
    public Dictionary<string, string> Metadata { get; set; } = [];

    [JsonIgnore]
    public Site Site { get; set; } = null!;

    [JsonIgnore]
    public bool IsSuccess => Status == CheckStatus.Succeeded;

    [JsonIgnore]
    public bool IsComplete => Status == CheckStatus.Failed || Status == CheckStatus.Succeeded;

    [JsonIgnore]
    public SiteCheckScreenshot? Screenshot { get; set; }

    public SiteCheck() { }

    [SetsRequiredMembers]
    public SiteCheck(Site site, DateTime startDate)
    {
        SiteId = site.Id;
        Status = CheckStatus.Queued;
        StartDate = startDate;
    }

    public void Update(Exception ex, DateTime completedDate)
    {
        Status = CheckStatus.Failed;
        FailureKind = Model.FailureKind.Unexpected;
        Value = ex.Message;
        CompletedDate = completedDate;
    }
}

[Index(nameof(SiteCheckId), IsUnique = true)]
public class SiteCheckScreenshot : IEntityWithId
{
    public int Id { get; set; }

    public required byte[] Data { get; set; }

    public int SiteCheckId { get; set; }

    [JsonIgnore]
    public SiteCheck? SiteCheck { get; set; }

    public SiteCheckScreenshot() { }

    [SetsRequiredMembers]
    public SiteCheckScreenshot(SiteCheck siteCheck, byte[] data)
    {
        SiteCheckId = siteCheck.Id;
        Data = data;
    }
}
