namespace SiteChecker.Scraper.Test;

using SiteChecker.Scraper.Utilities;

[TestClass]
public sealed class TryResultTests
{
    [TestMethod]
    public void Success_SetsIsSuccessToTrue()
    {
        var result = TryResult.Success("value");
        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public void Success_SetsResultToProvidedValue()
    {
        var result = TryResult.Success("hello");
        Assert.AreEqual("hello", result.Result);
    }

    [TestMethod]
    public void Success_SetsExceptionToNull()
    {
        var result = TryResult.Success(42);
        Assert.IsNull(result.Exception);
    }

    [TestMethod]
    public void Success_WorksWithReferenceType()
    {
        var result = TryResult.Success("hello");
        Assert.AreEqual("hello", result.Result);
    }

    [TestMethod]
    public void Success_WorksWithValueType()
    {
        var result = TryResult.Success(42);
        Assert.AreEqual(42, result.Result);
    }

    [TestMethod]
    public void Failure_SetsIsSuccessToFalse()
    {
        var result = TryResult.Failure<string>(new InvalidOperationException("error"));
        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void Failure_SetsExceptionToProvidedException()
    {
        var ex = new InvalidOperationException("error");
        var result = TryResult.Failure<string>(ex);
        Assert.AreSame(ex, result.Exception);
    }

    [TestMethod]
    public void Failure_SetsResultToDefaultForReferenceType()
    {
        var result = TryResult.Failure<string>(new InvalidOperationException("error"));
        Assert.IsNull(result.Result);
    }

    [TestMethod]
    public void Failure_SetsResultToDefaultForValueType()
    {
        var result = TryResult.Failure<int>(new InvalidOperationException("error"));
        Assert.AreEqual(0, result.Result);
    }
}
