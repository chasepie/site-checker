using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using SiteChecker.Domain;
using SiteChecker.Domain.Exceptions;
using SiteChecker.Scraper.Extensions;
using SiteChecker.Utilities;

namespace SiteChecker.Scraper.Scrapers;

public interface IScraper
{
    public string Id { get; }
    public Task<IScrapeResult> ScrapeAsync(IPage page, ScrapeRequest request);
}

public abstract class ScraperBase(ILogger logger) : IScraper
{
    protected ILogger Logger => logger;

    public abstract string Id { get; }
    public abstract string Url { get; }

    protected abstract Task<IScrapeResult> DoScrapeAsync(IPage page, ScrapeRequest request);

    private async Task SaveHtmlToLogsAsync(
        string filePathBase,
        ScrapeRequest request,
        IPage page,
        CancellationToken cancellationToken = default)
    {
        var htmlResult = await page.TrySaveHTMLContentAsync($"{filePathBase}.html", cancellationToken);
        if (htmlResult.IsSuccess)
        {
            request.LogInfo(Logger, $"Saved HTML content to {htmlResult.Result}");
        }
        else if (Logger.IsEnabled(LogLevel.Error))
        {
            request.LogError(Logger, "Saving HTML content also failed", htmlResult.Exception);
        }
    }

    protected virtual Task SaveExceptionsToLogsAsync(
        string filePathBase, ScrapeRequest request, Exception ex,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Timestamp: {DateTime.Now.ToString("F")}");
        sb.AppendLine($"Scraper ID: {request.ScraperId}");
        sb.AppendLine($"Request ID: {request.Id}");
        sb.AppendLine();
        sb.AppendLine("Exception:");
        sb.AppendLine(ex.Message);
        sb.AppendLine(ex.StackTrace);

        return File.WriteAllTextAsync($"{filePathBase}.log", sb.ToString(), cancellationToken);
    }

    private async Task TakeScreenshot(IPage page, ScrapeRequest request, IScrapeResult result)
    {
        var screenshot = await page.TryTakeFullPageScreenshotAsync();
        if (screenshot.IsSuccess)
        {
            result.Screenshot = screenshot.Result;
        }
        else if (Logger.IsEnabled(LogLevel.Error))
        {
            request.LogError(Logger, "Taking screenshot after scrape exception also failed", screenshot.Exception);
        }
    }

    private async Task<FailureScrapeResult> HandleScrapeExceptionAsync(Exception ex, ScrapeRequest request, IPage page)
    {
        request.LogError(Logger, "Scraping failed", ex);

        string logsDir;

        if (!EnvironmentUtils.IsDockerContainer()
            && RepoUtils.TryGetRepoDirectory(out var repoRoot))
        {
            logsDir = Path.Join(repoRoot, "site-checker/logs");
        }
        else
        {
            logsDir = Path.Join(AppContext.BaseDirectory, "logs");
        }

        if (!Directory.Exists(logsDir))
        {
            Directory.CreateDirectory(logsDir);
        }

        var fileNameBase = $"{request.Id}_{request.ScraperId}";
        var filePathBase = Path.Join(logsDir, fileNameBase);
        await SaveHtmlToLogsAsync(filePathBase, request, page);
        await SaveExceptionsToLogsAsync(filePathBase, request, ex);

        if (ex is not ScraperException scraperEx)
        {
            scraperEx = new UnexpectedScraperException($"An unexpected error occurred during scraping ({nameof(ScraperBase)})", ex);
        }

        return FailureScrapeResult.FromException(scraperEx);
    }

    public virtual async Task<ILocator> WaitForFirstLocatorAsync(
        IPage page,
        ILocator locator,
        LocatorWaitForOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return await WaitForFirstLocatorInternalAsync(page, [locator], options, cancellationToken);
    }

    public virtual async Task<ILocator> WaitForFirstLocatorAsync(
        IPage page,
        IEnumerable<ILocator> locator,
        LocatorWaitForOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return await WaitForFirstLocatorInternalAsync(page, locator, options, cancellationToken);
    }

    private static async Task<ILocator> WaitForFirstLocatorInternalAsync(
        IPage page,
        IEnumerable<ILocator> locators,
        LocatorWaitForOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new LocatorWaitForOptions();
        options.Timeout ??= PlaywrightConsts.DefaultTimeoutMS;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var blankPageTask = page.WaitForBlankPageAsync(cts.Token);
        var accessDeniedTask = page.WaitForAccessDeniedAsync(cts.Token);

        List<Task<ILocator>> allTasks = [
            blankPageTask,
            accessDeniedTask
        ];
        allTasks.AddRange(locators.Select(async locator =>
        {
            await locator.WaitForAsync(cts.Token);
            return locator;
        }));

        Task<ILocator> firstLocator;
        try
        {
            firstLocator = await Task.WhenAny(allTasks);
        }
        finally
        {
            cts.Cancel();
        }

        if (firstLocator == blankPageTask)
        {
            throw new BlankPageScraperException();
        }
        else if (firstLocator == accessDeniedTask)
        {
            throw new AccessDeniedScraperException();
        }

        return await firstLocator;
    }

    public async Task<IScrapeResult> ScrapeAsync(IPage page, ScrapeRequest request)
    {
        IScrapeResult result;
        try
        {
            result = await DoScrapeAsync(page, request);
        }
        catch (KnownScraperException ex)
        {
            request.LogError(Logger, ex.Message, ex);
            result = FailureScrapeResult.FromException(ex);
        }
        catch (Exception ex)
        {
            result = await HandleScrapeExceptionAsync(ex, request, page);
        }

        var screenshotNotTaken = result.Screenshot == null || result.Screenshot.Length == 0;
        if (screenshotNotTaken && (request.AlwaysTakeScreenshot || !result.WasSuccessful))
        {
            await TakeScreenshot(page, request, result);
        }

        return result;
    }
}

internal static class ScraperExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddScraper<T>() where T : ScraperBase
        {
            return services.AddSingleton<IScraper, T>();
        }
    }
}
