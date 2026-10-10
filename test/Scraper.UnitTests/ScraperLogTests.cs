namespace SiteChecker.Scraper.UnitTests;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SiteChecker.Scraper;

[TestClass]
public sealed class ScraperLogTests
{
    [TestMethod]
    public void Entries_AreRecorded_AndPassedOn()
    {
        var inner = Substitute.For<ILogger>();
        var log = new ScraperLog();
        var exception = new InvalidOperationException("boom");

        log.Wrap(inner).LogError(exception, "Failed on {Page}", 2);

        var entry = Assert.ContainsSingle(log.Entries);
        Assert.AreEqual(LogLevel.Error, entry.Level);
        Assert.AreEqual("Failed on 2", entry.Message);
        Assert.Contains("boom", entry.Exception!);
        inner.Received(1).Log(LogLevel.Error, Arg.Any<EventId>(), Arg.Any<Arg.AnyType>(), exception, Arg.Any<Func<Arg.AnyType, Exception?, string>>());
    }

    [TestMethod]
    public void Trace_IsPassedOn_ButNotRecorded()
    {
        var log = new ScraperLog();
        var logger = log.Wrap(NullLogger.Instance);

        logger.LogTrace("noise");
        logger.LogDebug("detail");

        Assert.AreEqual("detail", Assert.ContainsSingle(log.Entries).Message);
    }

    [TestMethod]
    public void Entries_PastTheLimit_AreCountedAsDropped()
    {
        var log = new ScraperLog();
        var logger = log.Wrap(NullLogger.Instance);

        for (var i = 0; i < ScraperLog.MaxEntries + 5; i++)
        {
            logger.LogInformation("entry {Index}", i);
        }

        Assert.HasCount(ScraperLog.MaxEntries + 1, log.Entries);
        Assert.AreEqual("entry 199", log.Entries[ScraperLog.MaxEntries - 1].Message);
        Assert.AreEqual("5 more log entries were dropped.", log.Entries[^1].Message);
    }

    [TestMethod]
    public void Entries_PastTheCharacterLimit_AreCountedAsDropped()
    {
        var log = new ScraperLog();
        var logger = log.Wrap(NullLogger.Instance);
        var big = new string('x', ScraperLog.MaxCharacters / 2);

        logger.LogInformation("{Text}", big);
        logger.LogInformation("{Text}", big);
        logger.LogInformation("{Text}", big);

        Assert.HasCount(3, log.Entries);
        Assert.AreEqual("1 more log entries were dropped.", log.Entries[^1].Message);
    }
}
