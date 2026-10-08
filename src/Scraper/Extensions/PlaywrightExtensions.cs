using Microsoft.Playwright;
using SiteChecker.Scraper.Utilities;
using SiteChecker.Scripting;

namespace SiteChecker.Scraper.Extensions;

public class PlaywrightConsts
{
    public const int DefaultTimeoutMS = 30_000;
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
    }
}
