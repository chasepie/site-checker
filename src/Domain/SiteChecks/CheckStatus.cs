namespace SiteChecker.Domain.SiteChecks;

public enum CheckStatus
{
    Created,
    Queued,
    Checking,
    Done,
    Failed
}
