namespace SiteChecker.Scraper.Test;

using SiteChecker.Scraper.Exceptions;

[TestClass]
public sealed class ScraperExceptionTests
{
    [TestMethod]
    public void ScraperException_IsAbstract()
    {
        Assert.IsTrue(typeof(ScraperException).IsAbstract);
    }

    [TestMethod]
    public void UnexpectedScraperException_InheritsFromScraperException()
    {
        var ex = new UnexpectedScraperException("msg");
        Assert.IsInstanceOfType<ScraperException>(ex);
    }

    [TestMethod]
    public void KnownScraperException_InheritsFromScraperException()
    {
        var ex = new KnownScraperException("msg");
        Assert.IsInstanceOfType<ScraperException>(ex);
    }

    [TestMethod]
    public void AccessDeniedScraperException_InheritsFromKnownScraperException()
    {
        var ex = new AccessDeniedScraperException();
        Assert.IsInstanceOfType<KnownScraperException>(ex);
    }

    [TestMethod]
    public void BlankPageScraperException_InheritsFromKnownScraperException()
    {
        var ex = new BlankPageScraperException();
        Assert.IsInstanceOfType<KnownScraperException>(ex);
    }

    [TestMethod]
    public void AccessDeniedScraperException_HasAccessDeniedMessage()
    {
        var ex = new AccessDeniedScraperException();
        Assert.AreEqual("Access Denied", ex.Message);
    }

    [TestMethod]
    public void BlankPageScraperException_HasBlankPageMessage()
    {
        var ex = new BlankPageScraperException();
        Assert.AreEqual("Blank Page", ex.Message);
    }

    [TestMethod]
    public void KnownScraperException_UsesProvidedMessage()
    {
        var ex = new KnownScraperException("custom message");
        Assert.AreEqual("custom message", ex.Message);
    }

    [TestMethod]
    public void UnexpectedScraperException_UsesProvidedMessage()
    {
        var ex = new UnexpectedScraperException("custom message");
        Assert.AreEqual("custom message", ex.Message);
    }

    [TestMethod]
    public void AccessDeniedScraperException_PropagatesInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new AccessDeniedScraperException(inner);
        Assert.AreSame(inner, ex.InnerException);
    }

    [TestMethod]
    public void BlankPageScraperException_PropagatesInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new BlankPageScraperException(inner);
        Assert.AreSame(inner, ex.InnerException);
    }

    [TestMethod]
    public void KnownScraperException_PropagatesInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new KnownScraperException("msg", inner);
        Assert.AreSame(inner, ex.InnerException);
    }

    [TestMethod]
    public void UnexpectedScraperException_PropagatesInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new UnexpectedScraperException("msg", inner);
        Assert.AreSame(inner, ex.InnerException);
    }

    [TestMethod]
    public void AccessDeniedScraperException_IsCatchableAsKnownScraperException()
    {
        Assert.Throws<KnownScraperException>(() => throw new AccessDeniedScraperException());
    }
}
