using System.Diagnostics;
using Microsoft.Playwright;
using SiteChecker.Scraper.Utilities;

namespace SiteChecker.Scraper.Extensions;

public class PlaywrightConsts
{
    public const int DefaultTimeoutMS = 30_000;
    public const int TimeoutIntervalMS = 500;
    public const int ErrorScenarioWaitMS = 10_000;
}

public static class IPageExtensions
{
    extension(IPage page)
    {
        public Task GotoUriAsync(Uri uri, PageGotoOptions? options = null)
        {
            return page.GotoAsync(uri.ToString(), options);
        }

        public async Task<byte[]> TakeFullPageScreenshotAsync(PageScreenshotOptions? options = null)
        {
            var actualWidth = await page.EvaluateAsync<int>("() => document.body.offsetWidth");
            var actualHeight = await page.EvaluateAsync<int>("() => document.body.offsetHeight");
            var maxWidth = Math.Max(actualWidth, 1920);
            await page.SetViewportSizeAsync(maxWidth, actualHeight);

            options ??= new PageScreenshotOptions();
            options.FullPage = true;
            return await page.ScreenshotAsync(options);
        }

        public async Task<TryResult<byte[]>> TryTakeFullPageScreenshotAsync(PageScreenshotOptions? options = null)
        {
            try
            {
                return TryResult.Success(await page.TakeFullPageScreenshotAsync(options));
            }
            catch (Exception ex)
            {
                return TryResult.Failure<byte[]>(ex);
            }
        }

        public async Task SaveHTMLContentAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            var content = await page.ContentAsync();
            await File.WriteAllTextAsync(filePath, content, cancellationToken);
        }

        public async Task<TryResult<bool>> TrySaveHTMLContentAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await page.SaveHTMLContentAsync(filePath, cancellationToken);
                return TryResult.Success(true);
            }
            catch (Exception ex)
            {
                return TryResult.Failure<bool>(ex);
            }
        }

        public async Task<ILocator> WaitForAccessDeniedAsync(
            CancellationToken cancellationToken = default)
        {
            return await page.WaitForAccessDeniedAsync(null, cancellationToken);
        }

        public async Task<ILocator> WaitForAccessDeniedAsync(
            LocatorWaitForOptions? options,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(PlaywrightConsts.ErrorScenarioWaitMS), cancellationToken); // Give page time to load
            var locator = page.Locator("h1", new() { HasTextString = "Access Denied" });
            await locator.WaitForAsync(options, cancellationToken);
            return locator;
        }

        public async Task<ILocator> WaitForBlankPageAsync(
            CancellationToken cancellationToken = default)
        {
            return await page.WaitForBlankPageAsync(null, cancellationToken);
        }

        public async Task<ILocator> WaitForBlankPageAsync(
            LocatorWaitForOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(PlaywrightConsts.ErrorScenarioWaitMS), cancellationToken); // Give page time to load
            var locator = page.Locator("body:not(:has(*:not(script):not(iframe)))");
            await locator.WaitForAsync(options, cancellationToken);
            return locator;
        }

        public async Task ScrollToBottomAsync()
        {
            await page.EvaluateAsync("() => window.scrollTo(0, document.body.scrollHeight)");
        }
    }
}


public static class ILocatorExtensions
{
    extension(ILocator locator)
    {
        public async Task WaitForAsync(
            CancellationToken cancellationToken)
        {
            await locator.WaitForAsync(null, cancellationToken);
        }

        public async Task WaitForAsync(
            LocatorWaitForOptions? options,
            CancellationToken cancellationToken)
        {
            await locator.WaitForAsync(options, PlaywrightConsts.TimeoutIntervalMS, cancellationToken);
        }

        public async Task WaitForAsync(
            LocatorWaitForOptions? options,
            int checkIntervalMS,
            CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
            {
                await locator.WaitForAsync(options);
                return;
            }

            options ??= new LocatorWaitForOptions();
            var totalTimeoutMS = options.Timeout ?? PlaywrightConsts.DefaultTimeoutMS;
            var intervalOptions = new LocatorWaitForOptions(options)
            {
                Timeout = Math.Min(checkIntervalMS, totalTimeoutMS)
            };

            TimeoutException? lastEx = null;
            var sw = new Stopwatch();
            sw.Start();
            while (sw.ElapsedMilliseconds < totalTimeoutMS)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await locator.WaitForAsync(intervalOptions);
                    return;
                }
                catch (TimeoutException ex)
                {
                    // Swallow and retry
                    lastEx = ex;
                }
            }

            throw new TimeoutException($"Timeout of {totalTimeoutMS}ms exceeded (Interval {checkIntervalMS}ms) waiting for {locator}", lastEx);
        }
    }
}
