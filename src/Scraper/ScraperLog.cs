using Microsoft.Extensions.Logging;

namespace SiteChecker.Scraper;

/// <summary>
/// One entry a Scraper logged.
/// </summary>
/// <param name="Level">The entry's level.</param>
/// <param name="Message">The formatted message.</param>
/// <param name="Exception">The logged exception as text, if there was one.</param>
public sealed record ScraperLogEntry(LogLevel Level, string Message, string? Exception);

/// <summary>
/// Records what a Scraper logs during one run, so the result can carry it to wherever the run was
/// requested from, while still passing each entry on to the real logger. It keeps at most
/// <see cref="MaxEntries"/> entries and <see cref="MaxCharacters"/> characters, so a script that
/// logs in a loop can't bloat the result; past that, a final entry says how many were dropped.
/// </summary>
public sealed class ScraperLog
{
    public const int MaxEntries = 200;
    public const int MaxCharacters = 64 * 1024;

    /// <summary>
    /// Trace is left out: it's for the app's own diagnostics, not a Scraper's.
    /// </summary>
    private const LogLevel MinimumLevel = LogLevel.Debug;

    private readonly List<ScraperLogEntry> _entries = [];
    private readonly Lock _lock = new();
    private int _characters;
    private int _dropped;

    /// <summary>
    /// A snapshot of the entries so far. A run abandoned at its timeout may still be logging.
    /// </summary>
    public IReadOnlyList<ScraperLogEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _dropped == 0
                    ? [.. _entries]
                    : [.. _entries, new ScraperLogEntry(LogLevel.Warning, $"{_dropped} more log entries were dropped.", null)];
            }
        }
    }

    /// <summary>
    /// A logger that records into this log and passes everything on to <paramref name="inner"/>.
    /// </summary>
    public ILogger Wrap(ILogger inner) => new RecordingLogger(inner, this);

    private void Add<TState>(LogLevel level, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        // Once the log is full, count the entry without formatting it, so a script that logs in a
        // loop doesn't pay for messages that are dropped anyway.
        lock (_lock)
        {
            if (IsFull)
            {
                _dropped++;
                return;
            }
        }

        var message = formatter(state, exception);
        var exceptionText = exception?.ToString();
        var characters = message.Length + (exceptionText?.Length ?? 0);
        lock (_lock)
        {
            if (IsFull || _characters + characters > MaxCharacters)
            {
                _dropped++;
                return;
            }

            _entries.Add(new ScraperLogEntry(level, message, exceptionText));
            _characters += characters;
        }
    }

    /// <summary>
    /// Whether no further entry can fit. Call under <see cref="_lock"/>.
    /// </summary>
    private bool IsFull => _entries.Count >= MaxEntries || _characters >= MaxCharacters;

    private sealed class RecordingLogger(ILogger inner, ScraperLog log) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel)
            => logLevel >= MinimumLevel && logLevel != LogLevel.None || inner.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            inner.Log(logLevel, eventId, state, exception, formatter);
            if (logLevel >= MinimumLevel && logLevel != LogLevel.None)
            {
                log.Add(logLevel, state, exception, formatter);
            }
        }
    }
}
