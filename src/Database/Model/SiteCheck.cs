using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SiteChecker.Domain.Common;
using SiteChecker.Domain.SiteChecks;

namespace SiteChecker.Database.Model;

public class SiteCheck : IEntityWithId
{
    public required int Id { get; set; }

    public required string? Value { get; set; }

    public string? VpnLocationId { get; set; }

    public CheckStatus Status { get; set; } = CheckStatus.Created;

    public required DateTime StartDate { get; set; }

    public required DateTime? DoneDate { get; set; }

    public required int SiteId { get; set; }

    [JsonIgnore]
    public Dictionary<string, string> Metadata { get; set; } = [];

    [JsonIgnore, NotMapped]
    public Dictionary<string, object> MetadataLocal { get; set; } = [];

    [JsonIgnore]
    public Site Site { get; set; } = null!;

    [JsonIgnore]
    public bool IsSuccess => Status == CheckStatus.Done;

    [JsonIgnore]
    public bool IsComplete => Status == CheckStatus.Failed || Status == CheckStatus.Done;

    [JsonIgnore]
    public SiteCheckScreenshot? Screenshot { get; set; }

    public SiteCheck() { }

    [SetsRequiredMembers]
    public SiteCheck(Site site)
    {
        SiteId = site.Id;
        Status = CheckStatus.Created;
        StartDate = DateTime.UtcNow;
    }

    public void Update(Exception ex)
    {
        Status = CheckStatus.Failed;
        Value = ex.Message;
        DoneDate = DateTime.UtcNow;
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
