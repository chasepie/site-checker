namespace SiteChecker.Scraper.Test;

using SiteChecker.Scraper;
using SiteChecker.Scraper.Exceptions;

[TestClass]
public sealed class ScrapeResultTests
{
    [TestMethod]
    public void SuccessScrapeResult_WasSuccessfulIsTrue()
    {
        var result = new SuccessScrapeResult { Content = "content" };
        Assert.IsTrue(result.WasSuccessful);
    }

    [TestMethod]
    public void SuccessScrapeResult_ContentIsPreserved()
    {
        var result = new SuccessScrapeResult { Content = "hello" };
        Assert.AreEqual("hello", result.Content);
    }

    [TestMethod]
    public void SuccessScrapeResult_ScreenshotIsNullByDefault()
    {
        var result = new SuccessScrapeResult { Content = "content" };
        Assert.IsNull(result.Screenshot);
    }

    [TestMethod]
    public void FailureScrapeResult_WasSuccessfulIsFalse()
    {
        var result = FailureScrapeResult.FromException(new KnownScraperException("error"));
        Assert.IsFalse(result.WasSuccessful);
    }

    [TestMethod]
    public void FromException_SetsErrorMessageToExceptionMessage()
    {
        var result = FailureScrapeResult.FromException(new KnownScraperException("oops"));
        Assert.AreEqual("oops", result.ErrorMessage);
    }

    [TestMethod]
    public void FromException_AppendsInnerExceptionMessage_WhenPresent()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new KnownScraperException("outer", inner);
        var result = FailureScrapeResult.FromException(ex);
        Assert.AreEqual("outer | Inner Exception: inner", result.ErrorMessage);
    }

    [TestMethod]
    public void FromException_ErrorMessageExcludesInnerSuffix_WhenNoInner()
    {
        var result = FailureScrapeResult.FromException(new KnownScraperException("oops"));
        Assert.DoesNotContain("| Inner Exception:", result.ErrorMessage);
    }

    [TestMethod]
    public void FromException_SetsExceptionProperty()
    {
        var ex = new KnownScraperException("oops");
        var result = FailureScrapeResult.FromException(ex);
        Assert.AreSame(ex, result.Exception);
    }

    [TestMethod]
    public void FromException_ScreenshotIsNullByDefault()
    {
        var result = FailureScrapeResult.FromException(new KnownScraperException("oops"));
        Assert.IsNull(result.Screenshot);
    }

    [TestMethod]
    public void IsSuccess_ReturnsTrueAndPopulatesOut_WhenSuccess()
    {
        IScrapeResult result = new SuccessScrapeResult { Content = "ok" };
        var isSuccess = result.IsSuccess(out var success);
        Assert.IsTrue(isSuccess);
        Assert.IsNotNull(success);
    }

    [TestMethod]
    public void IsSuccess_ReturnsFalseAndOutIsNull_WhenFailure()
    {
        IScrapeResult result = FailureScrapeResult.FromException(new KnownScraperException("fail"));
        var isSuccess = result.IsSuccess(out var success);
        Assert.IsFalse(isSuccess);
        Assert.IsNull(success);
    }

    [TestMethod]
    public void IsFailure_ReturnsTrueAndPopulatesOut_WhenFailure()
    {
        IScrapeResult result = FailureScrapeResult.FromException(new KnownScraperException("fail"));
        var isFailure = result.IsFailure(out var failure);
        Assert.IsTrue(isFailure);
        Assert.IsNotNull(failure);
    }

    [TestMethod]
    public void IsFailure_ReturnsFalseAndOutIsNull_WhenSuccess()
    {
        IScrapeResult result = new SuccessScrapeResult { Content = "ok" };
        var isFailure = result.IsFailure(out var failure);
        Assert.IsFalse(isFailure);
        Assert.IsNull(failure);
    }
}
