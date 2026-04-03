using SiteChecker.Domain.ValueObjects;

namespace SiteChecker.Domain.Test.ValueObjects;

[TestClass]
public class SiteScheduleTests
{
    private static SiteSchedule ActiveSchedule(uint intervalMinutes = 60) => new()
    {
        Enabled = true,
        Start = new TimeOnly(0, 0),
        End = new TimeOnly(23, 59),
        Interval = intervalMinutes,
    };

    // Fixed local time used across tests — noon on a specific date avoids midnight edge cases.
    private static readonly DateTime Noon = new(2024, 6, 15, 12, 0, 0, DateTimeKind.Local);

    [TestMethod]
    public void IsDueForCheck_IsFalse_WhenDisabled()
    {
        var schedule = ActiveSchedule();
        schedule.Enabled = false;

        Assert.IsFalse(schedule.IsDueForCheck(null, Noon));
    }

    [TestMethod]
    public void IsDueForCheck_IsFalse_WhenIntervalIsNull()
    {
        var schedule = ActiveSchedule();
        schedule.Interval = null;

        Assert.IsFalse(schedule.IsDueForCheck(null, Noon));
    }

    [TestMethod]
    public void IsDueForCheck_IsFalse_WhenStartIsNull()
    {
        var schedule = ActiveSchedule();
        schedule.Start = null;

        Assert.IsFalse(schedule.IsDueForCheck(null, Noon));
    }

    [TestMethod]
    public void IsDueForCheck_IsFalse_WhenEndIsNull()
    {
        var schedule = ActiveSchedule();
        schedule.End = null;

        Assert.IsFalse(schedule.IsDueForCheck(null, Noon));
    }

    [TestMethod]
    public void IsDueForCheck_IsFalse_WhenNowIsBeforeWindow()
    {
        var schedule = new SiteSchedule
        {
            Enabled = true,
            Start = new TimeOnly(10, 0),
            End = new TimeOnly(11, 0),
            Interval = 60,
        };
        var nineAm = new DateTime(2024, 6, 15, 9, 0, 0, DateTimeKind.Local);

        Assert.IsFalse(schedule.IsDueForCheck(null, nineAm));
    }

    [TestMethod]
    public void IsDueForCheck_IsFalse_WhenNowIsAfterWindow()
    {
        var schedule = new SiteSchedule
        {
            Enabled = true,
            Start = new TimeOnly(10, 0),
            End = new TimeOnly(11, 0),
            Interval = 60,
        };
        var twelvePm = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Local);

        Assert.IsFalse(schedule.IsDueForCheck(null, twelvePm));
    }

    [TestMethod]
    public void IsDueForCheck_IsTrue_WhenNoPreviousCheck_AndInWindow()
    {
        var schedule = ActiveSchedule();
        Assert.IsTrue(schedule.IsDueForCheck(null, Noon));
    }

    [TestMethod]
    public void IsDueForCheck_IsTrue_WhenLastCheckExceededInterval()
    {
        var schedule = ActiveSchedule(intervalMinutes: 60);
        // Last check 90 min before Noon (UTC), converts back to local as 10:30
        var lastCheck = Noon.ToUniversalTime().AddMinutes(-90);

        Assert.IsTrue(schedule.IsDueForCheck(lastCheck, Noon));
    }

    [TestMethod]
    public void IsDueForCheck_IsFalse_WhenLastCheckWithinInterval()
    {
        var schedule = ActiveSchedule(intervalMinutes: 60);
        // Last check 30 min before Noon (UTC), converts back to local as 11:30
        var lastCheck = Noon.ToUniversalTime().AddMinutes(-30);

        Assert.IsFalse(schedule.IsDueForCheck(lastCheck, Noon));
    }

    [TestMethod]
    public void IsDueForCheck_IsTrue_WhenLastCheckExactlyAtIntervalBoundary()
    {
        var schedule = ActiveSchedule(intervalMinutes: 60);
        // Last check exactly 60 min before Noon — boundary is inclusive (<=)
        var lastCheck = Noon.ToUniversalTime().AddMinutes(-60);

        Assert.IsTrue(schedule.IsDueForCheck(lastCheck, Noon));
    }

    [TestMethod]
    public void Update_AppliesAllFields()
    {
        var schedule = new SiteSchedule();
        var updated = new SiteSchedule
        {
            Enabled = true,
            Start = new TimeOnly(8, 0),
            End = new TimeOnly(18, 0),
            Interval = 30,
        };

        schedule.Update(updated);

        Assert.IsTrue(schedule.Enabled);
        Assert.AreEqual(new TimeOnly(8, 0), schedule.Start);
        Assert.AreEqual(new TimeOnly(18, 0), schedule.End);
        Assert.AreEqual(30u, schedule.Interval);
    }
}
