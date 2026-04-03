namespace SiteChecker.Domain.ValueObjects;

public class SiteSchedule
{
    public bool Enabled { get; set; } = false;
    public TimeOnly? Start { get; set; } = null;
    public TimeOnly? End { get; set; } = null;
    public uint? Interval { get; set; } = null;

    public void Update(SiteSchedule schedule)
    {
        Enabled = schedule.Enabled;
        Start = schedule.Start;
        End = schedule.End;
        Interval = schedule.Interval;
    }
}
