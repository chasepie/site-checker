using SiteChecker.Domain.Enums;

namespace SiteChecker.Domain.Entities;

public class SiteCheck : IEntityWithId
{
    public required int Id { get; set; }

    public required string? Value { get; set; }

    public string? VpnLocationId { get; set; }

    public CheckStatus Status { get; set; } = CheckStatus.Created;

    public required DateTime StartDate { get; set; }

    public required DateTime? DoneDate { get; set; }

    public required int SiteId { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = [];

    public bool IsSuccess => Status == CheckStatus.Done;

    public bool IsComplete => Status == CheckStatus.Failed || Status == CheckStatus.Done;

    public SiteCheck() { }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SiteCheck(int siteId)
    {
        SiteId = siteId;
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
