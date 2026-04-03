namespace SiteChecker.Domain;

public interface IScrapeResult
{
    bool WasSuccessful { get; }
    byte[]? Screenshot { get; }
}
