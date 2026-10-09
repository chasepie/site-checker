namespace SiteChecker.Scraper.UnitTests;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SiteChecker.Scraper;

[TestClass]
public sealed class AbandonedRunMonitorTests : IDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly List<string> _failFasts = [];
    private readonly ExitingAbandonedRunMonitor _monitor;
    private int _stops;

    public AbandonedRunMonitorTests()
    {
        _monitor = new ExitingAbandonedRunMonitor(
            _time,
            NullLogger<ExitingAbandonedRunMonitor>.Instance,
            () => _stops++,
            _failFasts.Add);
    }

    public void Dispose() => _monitor.Dispose();

    private static ScrapeRequest Request() => new()
    {
        SiteCheckId = 7,
        Site = new ScrapeSite(3, "Site", new Uri("https://example.com/"), UseVpn: false),
        Scraper = new ScriptSpec("Script.cs", "source", "hash"),
    };

    [TestMethod]
    public void RunThatEndsWithinTheGracePeriod_DoesNotStopTheProcess()
    {
        var run = new TaskCompletionSource();
        _monitor.Track(run.Task, Request());
        Assert.IsTrue(_monitor.HasAbandonedRuns);

        _time.Advance(ExitingAbandonedRunMonitor.Grace / 2);
        run.SetResult();
        _time.Advance(ExitingAbandonedRunMonitor.Grace + ExitingAbandonedRunMonitor.FailFastDelay);

        Assert.IsFalse(_monitor.HasAbandonedRuns);
        Assert.AreEqual(0, _stops);
        Assert.IsEmpty(_failFasts);
    }

    [TestMethod]
    public void RunStillGoingAfterTheGracePeriod_StopsTheProcess()
    {
        var run = new TaskCompletionSource();
        _monitor.Track(run.Task, Request());

        _time.Advance(ExitingAbandonedRunMonitor.Grace - TimeSpan.FromSeconds(1));
        Assert.AreEqual(0, _stops);
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.AreEqual(1, _stops);
        Assert.IsTrue(_monitor.HasAbandonedRuns);
        Assert.IsEmpty(_failFasts);
    }

    [TestMethod]
    public void StopThatTakesTooLong_FailsFast()
    {
        _monitor.Track(new TaskCompletionSource().Task, Request());

        _time.Advance(ExitingAbandonedRunMonitor.Grace);
        _time.Advance(ExitingAbandonedRunMonitor.FailFastDelay);

        Assert.AreEqual(1, _stops);
        Assert.ContainsSingle(_failFasts);
    }

    [TestMethod]
    public void SeveralStuckRuns_StopTheProcessOnce()
    {
        _monitor.Track(new TaskCompletionSource().Task, Request());
        _time.Advance(TimeSpan.FromSeconds(1));
        _monitor.Track(new TaskCompletionSource().Task, Request());

        _time.Advance(ExitingAbandonedRunMonitor.Grace);

        Assert.AreEqual(1, _stops);
    }

    [TestMethod]
    public void LoggingMonitor_TracksARunUntilItEnds()
    {
        var monitor = new LoggingAbandonedRunMonitor(NullLogger<LoggingAbandonedRunMonitor>.Instance);
        var run = new TaskCompletionSource();

        monitor.Track(run.Task, Request());
        Assert.IsTrue(monitor.HasAbandonedRuns);
        run.SetResult();

        Assert.IsFalse(monitor.HasAbandonedRuns);
    }
}
