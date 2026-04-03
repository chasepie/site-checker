namespace SiteChecker.Domain.ValueObjects;

public class SiteSchedule
{
    public bool Enabled { get; set; } = false;
    public TimeOnly? Start { get; set; } = null;
    public TimeOnly? End { get; set; } = null;
    public uint? Interval { get; set; } = null;

    /// <summary>
    /// Returns true when this schedule is configured, active at <paramref name="now"/>, and the
    /// interval has elapsed since the last completed check.
    /// </summary>
    /// <param name="lastCompleteCheckStartDate">
    /// UTC start date of the most recent completed (Done or Failed) check, or null if none exists.
    /// </param>
    /// <param name="now">The current local date/time.</param>
    public bool IsDueForCheck(DateTime? lastCompleteCheckStartDate, DateTime now)
    {
        if (!Enabled || !Interval.HasValue || !Start.HasValue || !End.HasValue)
        {
            return false;
        }

        var nowTime = TimeOnly.FromDateTime(now);
        if (!nowTime.IsBetween(Start.Value, End.Value))
        {
            return false;
        }

        if (lastCompleteCheckStartDate == null)
        {
            return true;
        }

        var intervalTimeAgo = now.Subtract(TimeSpan.FromMinutes(Interval.Value));
        return lastCompleteCheckStartDate.Value.ToLocalTime() <= intervalTimeAgo;
    }

    public void Update(SiteSchedule schedule)
    {
        Enabled = schedule.Enabled;
        Start = schedule.Start;
        End = schedule.End;
        Interval = schedule.Interval;
    }
}
