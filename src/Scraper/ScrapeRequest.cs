using Microsoft.Extensions.Logging;

namespace SiteChecker.Scraper;

public enum BrowserType
{
    Browserless,
    BrowserlessVpn,
    Local,
}

public class ScrapeRequest
{
    public required int Id { get; set; }
    public required string ScraperId { get; set; }
    public bool AlwaysTakeScreenshot { get; set; } = false;
    public BrowserType BrowserType { get; set; } = BrowserType.Browserless;
}

internal static class ScrapeRequestExtensions
{
    private const string logTemplate = "[Scraper ID: {ScraperId}] [Request ID: {RequestId}]: {Message}";

    extension(ScrapeRequest request)
    {
        public void LogInfo(ILogger logger, string message)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(logTemplate, request.ScraperId, request.Id, message);
            }
        }

        public void LogError(ILogger logger, string message, Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, logTemplate, request.ScraperId, request.Id, message);
            }
        }
    }
}
