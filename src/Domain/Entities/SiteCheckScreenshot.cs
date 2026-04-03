namespace SiteChecker.Domain.Entities;

public class SiteCheckScreenshot : IEntityWithId
{
    public int Id { get; set; }

    public required byte[] Data { get; set; }

    public int SiteCheckId { get; set; }

    public SiteCheckScreenshot() { }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SiteCheckScreenshot(int siteCheckId, byte[] data)
    {
        SiteCheckId = siteCheckId;
        Data = data;
    }
}
