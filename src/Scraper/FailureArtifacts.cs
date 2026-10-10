using System.Text;
using Microsoft.Extensions.Logging;
using SiteChecker.Utilities;

namespace SiteChecker.Scraper;

/// <summary>
/// Writes the page's HTML and the exception behind an Unexpected Failure to the logs directory
/// (<c>site-checker/logs</c>, or <c>logs</c> next to the app in Docker), named
/// <c>{SiteCheckId}_{SiteId}</c>.
/// </summary>
public sealed class FailureArtifacts
{
    private readonly ILogger<FailureArtifacts> _logger;

    public FailureArtifacts(ILogger<FailureArtifacts> logger)
        : this(logger, DefaultLogsDirectory())
    {
    }

    public FailureArtifacts(ILogger<FailureArtifacts> logger, string logsDirectory)
    {
        _logger = logger;
        LogsDirectory = logsDirectory;
    }

    public string LogsDirectory { get; }

    /// <summary>
    /// Writes the dumps for a Site Check's Unexpected Failure: a description with the exception,
    /// and the page's HTML when the scrape captured it. Never throws: a dump that can't be written
    /// is logged and skipped.
    /// </summary>
    public async Task WriteAsync(ScrapeRequest request, ScrapeResult result, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(LogsDirectory);
            var filePathBase = Path.Join(LogsDirectory, $"{request.SiteCheckId}_{request.Site.Id}");

            await File.WriteAllTextAsync($"{filePathBase}.log", Describe(request, result), cancellationToken);
            if (result.PageHtml is { } html)
            {
                await File.WriteAllTextAsync($"{filePathBase}.html", html, cancellationToken);
            }
            _logger.LogInformation("Saved failure dumps to {FilePathBase}.", filePathBase);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Couldn't save the failure dumps for Site Check {SiteCheckId}.", request.SiteCheckId);
        }
    }

    private static string Describe(ScrapeRequest request, ScrapeResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Timestamp: {DateTime.Now:F}");
        sb.AppendLine($"Site: {request.Site.Name} (ID {request.Site.Id})");
        sb.AppendLine($"Site Check ID: {request.SiteCheckId}");
        sb.AppendLine($"URL: {request.Site.Url}");
        sb.AppendLine();
        sb.AppendLine($"Message: {result.Message}");
        foreach (var diagnostic in result.Diagnostics)
        {
            sb.AppendLine($"{diagnostic.FileName}({diagnostic.Line},{diagnostic.Column}): {diagnostic.Id} {diagnostic.Message}");
        }
        if (result.ExceptionDetail is not null)
        {
            sb.AppendLine();
            sb.AppendLine("Exception:");
            sb.AppendLine(result.ExceptionDetail);
        }
        if (result.Logs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Scraper log:");
            foreach (var entry in result.Logs)
            {
                sb.AppendLine($"[{entry.Level}] {entry.Message}");
                if (entry.Exception is not null)
                {
                    sb.AppendLine(entry.Exception);
                }
            }
        }
        return sb.ToString();
    }

    private static string DefaultLogsDirectory()
    {
        if (!EnvironmentUtils.IsDockerContainer()
            && RepoUtils.TryGetRepoDirectory(out var repoRoot))
        {
            return Path.Join(repoRoot, "site-checker/logs");
        }
        return Path.Join(AppContext.BaseDirectory, "logs");
    }
}
