using System.Diagnostics;
using Microsoft.Playwright;

namespace SiteChecker.Scripting;

/// <summary>
/// Page helpers available to scripts.
/// </summary>
public static class PageExtensions
{
    extension(IPage page)
    {
        /// <summary>
        /// Screenshots the whole page, widening the viewport to at least 1920 pixels and growing it to
        /// the page's height first.
        /// </summary>
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

        /// <summary>
        /// Scrolls to the bottom of the page, for pages that load content as you scroll.
        /// </summary>
        public async Task ScrollToBottomAsync()
        {
            await page.EvaluateAsync("() => window.scrollTo(0, document.body.scrollHeight)");
        }
    }
}

/// <summary>
/// Locator helpers available to scripts.
/// </summary>
public static class LocatorExtensions
{
    private const int DefaultTimeoutMS = 30_000;
    private const int DefaultCheckIntervalMS = 500;

    extension(ILocator locator)
    {
        /// <summary>
        /// Waits for the locator like <see cref="ILocator.WaitForAsync"/>, but in short intervals so
        /// that <paramref name="cancellationToken"/> can stop the wait.
        /// </summary>
        public async Task WaitForAsync(CancellationToken cancellationToken)
        {
            await locator.WaitForAsync(null, cancellationToken);
        }

        /// <inheritdoc cref="WaitForAsync(ILocator, LocatorWaitForOptions?, int, CancellationToken)"/>
        public async Task WaitForAsync(
            LocatorWaitForOptions? options,
            CancellationToken cancellationToken)
        {
            await locator.WaitForAsync(options, DefaultCheckIntervalMS, cancellationToken);
        }

        /// <summary>
        /// Waits for the locator like <see cref="ILocator.WaitForAsync"/>, checking every
        /// <paramref name="checkIntervalMS"/> milliseconds so that <paramref name="cancellationToken"/>
        /// can stop the wait.
        /// </summary>
        /// <exception cref="TimeoutException">The locator wasn't found within the timeout. Playwright reports
        /// its own timeouts as <see cref="TimeoutException"/> too.</exception>
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
            var totalTimeoutMS = options.Timeout ?? DefaultTimeoutMS;
            var intervalOptions = new LocatorWaitForOptions(options)
            {
                Timeout = Math.Min(checkIntervalMS, totalTimeoutMS)
            };

            TimeoutException? lastEx = null;
            var sw = Stopwatch.StartNew();
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
