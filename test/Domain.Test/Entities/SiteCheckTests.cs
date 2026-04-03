using SiteChecker.Domain.Entities;
using SiteChecker.Domain.Enums;
using SiteChecker.Domain.Exceptions;

namespace SiteChecker.Domain.Test.Entities;

[TestClass]
public class SiteCheckTests
{
    [TestMethod]
    public void Constructor_Sets_SiteId_Status_StartDate()
    {
        var before = DateTime.UtcNow;
        var siteCheck = new SiteCheck(42);
        var after = DateTime.UtcNow;

        Assert.AreEqual(42, siteCheck.SiteId);
        Assert.AreEqual(CheckStatus.Created, siteCheck.Status);
        Assert.IsTrue(siteCheck.StartDate >= before && siteCheck.StartDate <= after);
    }

    [TestMethod]
    public void IsComplete_IsFalse_WhenCreated()
    {
        var siteCheck = new SiteCheck(1);
        Assert.IsFalse(siteCheck.IsComplete);
    }

    [TestMethod]
    public void IsComplete_IsFalse_WhenQueued()
    {
        var siteCheck = new SiteCheck(1);
        siteCheck.Status = CheckStatus.Queued;
        Assert.IsFalse(siteCheck.IsComplete);
    }

    [TestMethod]
    public void IsComplete_IsFalse_WhenChecking()
    {
        var siteCheck = new SiteCheck(1);
        siteCheck.Status = CheckStatus.Checking;
        Assert.IsFalse(siteCheck.IsComplete);
    }

    [TestMethod]
    public void IsComplete_IsTrue_WhenDone()
    {
        var siteCheck = new SiteCheck(1);
        siteCheck.CompleteWithResult(new SuccessScrapeResult { Content = "ok" });
        Assert.IsTrue(siteCheck.IsComplete);
    }

    [TestMethod]
    public void IsComplete_IsTrue_WhenFailed()
    {
        var siteCheck = new SiteCheck(1);
        siteCheck.FailWithException(new InvalidOperationException("err"));
        Assert.IsTrue(siteCheck.IsComplete);
    }

    [TestMethod]
    public void IsSuccess_IsTrue_WhenDone()
    {
        var siteCheck = new SiteCheck(1);
        siteCheck.CompleteWithResult(new SuccessScrapeResult { Content = "content" });
        Assert.IsTrue(siteCheck.IsSuccess);
    }

    [TestMethod]
    public void IsSuccess_IsFalse_WhenFailed()
    {
        var siteCheck = new SiteCheck(1);
        siteCheck.FailWithException(new InvalidOperationException("err"));
        Assert.IsFalse(siteCheck.IsSuccess);
    }

    [TestMethod]
    public void IsSuccess_IsFalse_WhenCreated()
    {
        var siteCheck = new SiteCheck(1);
        Assert.IsFalse(siteCheck.IsSuccess);
    }

    [TestMethod]
    public void CompleteWithResult_Success_SetsDoneStatus_And_Value()
    {
        var siteCheck = new SiteCheck(1);
        siteCheck.CompleteWithResult(new SuccessScrapeResult { Content = "page content" });

        Assert.AreEqual(CheckStatus.Done, siteCheck.Status);
        Assert.AreEqual("page content", siteCheck.Value);
        Assert.IsNotNull(siteCheck.DoneDate);
    }

    [TestMethod]
    public void CompleteWithResult_Failure_SetsFailedStatus_And_Value()
    {
        var siteCheck = new SiteCheck(1);
        siteCheck.CompleteWithResult(new FailureScrapeResult { ErrorMessage = "timeout" });

        Assert.AreEqual(CheckStatus.Failed, siteCheck.Status);
        Assert.AreEqual("timeout", siteCheck.Value);
        Assert.IsNotNull(siteCheck.DoneDate);
    }

    [TestMethod]
    public void CompleteWithResult_FailureWithException_StoresExceptionTypeInMetadata()
    {
        var siteCheck = new SiteCheck(1);
        siteCheck.CompleteWithResult(FailureScrapeResult.FromException(new AccessDeniedScraperException()));

        Assert.IsTrue(siteCheck.Metadata.ContainsKey("EXCEPTION_TYPE"));
        Assert.IsTrue(siteCheck.Metadata["EXCEPTION_TYPE"].EndsWith(nameof(AccessDeniedScraperException), StringComparison.Ordinal));
    }

    [TestMethod]
    public void CompleteWithResult_FailureWithNoException_DoesNotStoreExceptionType()
    {
        var siteCheck = new SiteCheck(1);
        siteCheck.CompleteWithResult(new FailureScrapeResult { ErrorMessage = "plain failure" });

        Assert.IsFalse(siteCheck.Metadata.ContainsKey("EXCEPTION_TYPE"));
    }

    [TestMethod]
    public void IsKnownFailure_IsTrue_ForAccessDeniedException()
    {
        var siteCheck = new SiteCheck(1);
        siteCheck.CompleteWithResult(FailureScrapeResult.FromException(new AccessDeniedScraperException()));
        Assert.IsTrue(siteCheck.IsKnownFailure);
    }

    [TestMethod]
    public void IsKnownFailure_IsTrue_ForBlankPageException()
    {
        var siteCheck = new SiteCheck(1);
        siteCheck.CompleteWithResult(FailureScrapeResult.FromException(new BlankPageScraperException()));
        Assert.IsTrue(siteCheck.IsKnownFailure);
    }

    [TestMethod]
    public void IsKnownFailure_IsTrue_ForKnownScraperException()
    {
        var siteCheck = new SiteCheck(1);
        siteCheck.CompleteWithResult(FailureScrapeResult.FromException(new KnownScraperException("custom known")));
        Assert.IsTrue(siteCheck.IsKnownFailure);
    }

    [TestMethod]
    public void IsKnownFailure_IsFalse_ForUnexpectedScraperException()
    {
        var siteCheck = new SiteCheck(1);
        siteCheck.CompleteWithResult(FailureScrapeResult.FromException(new UnexpectedScraperException("boom")));
        Assert.IsFalse(siteCheck.IsKnownFailure);
    }

    [TestMethod]
    public void IsKnownFailure_IsFalse_WhenStatusIsDone()
    {
        var siteCheck = new SiteCheck(1);
        siteCheck.CompleteWithResult(new SuccessScrapeResult { Content = "ok" });
        Assert.IsFalse(siteCheck.IsKnownFailure);
    }

    [TestMethod]
    public void IsKnownFailure_IsFalse_WhenFailedWithoutException()
    {
        var siteCheck = new SiteCheck(1);
        siteCheck.CompleteWithResult(new FailureScrapeResult { ErrorMessage = "plain failure" });
        Assert.IsFalse(siteCheck.IsKnownFailure);
    }

    [TestMethod]
    public void FailWithException_SetsFailedStatus_Value_DoneDate()
    {
        var siteCheck = new SiteCheck(1);
        var ex = new InvalidOperationException("something went wrong");
        var before = DateTime.UtcNow;
        siteCheck.FailWithException(ex);
        var after = DateTime.UtcNow;

        Assert.AreEqual(CheckStatus.Failed, siteCheck.Status);
        Assert.AreEqual("something went wrong", siteCheck.Value);
        Assert.IsNotNull(siteCheck.DoneDate);
        Assert.IsTrue(siteCheck.DoneDate >= before && siteCheck.DoneDate <= after);
    }
}
