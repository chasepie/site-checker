using Microsoft.Extensions.Logging;

namespace SiteChecker.Scraper;

/// <summary>
/// Decides what happens to a run the pipeline stopped waiting for, at its timeout or because its
/// caller gave up. The pipeline has already closed the browser, which ends any run waiting on
/// Playwright, but .NET can't stop a thread, so a run stuck in a synchronous loop keeps going.
/// </summary>
public interface IAbandonedRunMonitor
{
    /// <summary>
    /// Whether an abandoned run is still going.
    /// </summary>
    bool HasAbandonedRuns { get; }

    /// <summary>
    /// Watches a run the pipeline abandoned.
    /// </summary>
    void Track(Task run, ScrapeRequest request);
}

/// <summary>
/// Logs when an abandoned run ends, and otherwise leaves it running. For the app's in-process
/// pipeline, where a stuck run can't be stopped without stopping the app.
/// </summary>
public sealed class LoggingAbandonedRunMonitor(ILogger<LoggingAbandonedRunMonitor> logger) : IAbandonedRunMonitor
{
    private readonly PendingRuns _runs = new(logger);

    public bool HasAbandonedRuns => _runs.Any;

    public void Track(Task run, ScrapeRequest request) => _runs.Track(run, request);
}

/// <summary>
/// Counts the abandoned runs still going, and logs each one as it ends.
/// </summary>
internal sealed class PendingRuns(ILogger logger)
{
    private int _pending;

    public bool Any => Volatile.Read(ref _pending) > 0;

    public void Track(Task run, ScrapeRequest request)
    {
        Interlocked.Increment(ref _pending);
        _ = run.ContinueWith(
            t =>
            {
                Interlocked.Decrement(ref _pending);
                logger.LogInformation(t.Exception?.GetBaseException(),
                    "The abandoned run for {SiteName} (Site Check {SiteCheckId}) has ended.", request.Site.Name, request.SiteCheckId);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}

/// <summary>
/// Stops the process when an abandoned run hasn't ended <see cref="Grace"/> after it was abandoned, so
/// whatever restarts the process (Docker, for the Scrape Worker) frees its thread. If stopping
/// gracefully takes longer than <see cref="FailFastDelay"/>, it fails fast. A run that ends within
/// the grace period changes nothing.
/// </summary>
/// <param name="timeProvider">Times the grace period and the fail-fast delay.</param>
/// <param name="logger">Logs the runs that end, and the stop.</param>
/// <param name="stopApplication">Starts a graceful shutdown, such as
/// <c>IHostApplicationLifetime.StopApplication</c>.</param>
/// <param name="failFast">Ends the process at once; <see cref="Environment.FailFast(string)"/>
/// outside tests.</param>
public sealed class ExitingAbandonedRunMonitor(
    TimeProvider timeProvider,
    ILogger<ExitingAbandonedRunMonitor> logger,
    Action stopApplication,
    Action<string> failFast) : IAbandonedRunMonitor, IDisposable
{
    /// <summary>
    /// How long after it was abandoned a run has to end. Closing the browser ends a run
    /// waiting on Playwright within a second or two; only a run that never yields lasts this long.
    /// </summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long a graceful stop may take before the process fails fast.
    /// </summary>
    public static readonly TimeSpan FailFastDelay = TimeSpan.FromSeconds(10);

    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<ExitingAbandonedRunMonitor> _logger = logger;
    private readonly Action _stopApplication = stopApplication;
    private readonly Action<string> _failFast = failFast;
    private readonly PendingRuns _runs = new(logger);

    /// <summary>
    /// The timers that haven't fired yet. Each removes itself when it fires, so a worker that
    /// times out many runs over its life doesn't keep every run and request alive.
    /// </summary>
    private readonly HashSet<ITimer> _timers = [];
    private readonly Lock _lock = new();
    private bool _stopping;

    public bool HasAbandonedRuns => _runs.Any;

    public void Track(Task run, ScrapeRequest request)
    {
        _runs.Track(run, request);
        AddTimer(() => CheckRun(run, request), Grace);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var timer in _timers)
            {
                timer.Dispose();
            }
            _timers.Clear();
        }
    }

    private void CheckRun(Task run, ScrapeRequest request)
    {
        if (run.IsCompleted)
        {
            return;
        }

        lock (_lock)
        {
            if (_stopping)
            {
                return;
            }
            _stopping = true;
        }

        _logger.LogCritical(
            "The abandoned run for {SiteName} (Site Check {SiteCheckId}) is still going {Grace} s later, so it can't be stopped; stopping the process to free it.",
            request.Site.Name, request.SiteCheckId, Grace.TotalSeconds);
        AddTimer(() => _failFast("An abandoned scrape didn't end, and stopping gracefully took too long."), FailFastDelay);
        _stopApplication();
    }

    private void AddTimer(Action callback, TimeSpan dueTime)
    {
        lock (_lock)
        {
            // Created under the lock, which the callback takes first, so it can't see the timer
            // before it's assigned.
            ITimer? timer = null;
            timer = _timeProvider.CreateTimer(
                _ =>
                {
                    lock (_lock)
                    {
                        _timers.Remove(timer!);
                    }
                    timer!.Dispose();
                    callback();
                },
                null,
                dueTime,
                Timeout.InfiniteTimeSpan);
            _timers.Add(timer);
        }
    }
}
