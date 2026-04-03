using SiteChecker.Domain.DTOs;
using SiteChecker.Domain.Entities;
using SiteChecker.Domain.ValueObjects;

namespace SiteChecker.Domain.Test.Entities;

[TestClass]
public class SiteTests
{
    private static Site MakeSite() => new()
    {
        Id = 1,
        Name = "Original",
        Url = new Uri("https://original.example.com"),
        ScraperId = "scraper-1",
        UseVpn = false,
        AlwaysTakeScreenshot = false,
    };

    private static SiteUpdate MakeUpdate() => new()
    {
        Name = "Updated",
        Url = new Uri("https://updated.example.com"),
        UseVpn = true,
        AlwaysTakeScreenshot = true,
        Schedule = new SiteSchedule { Enabled = true, Start = new TimeOnly(9, 0), End = new TimeOnly(17, 0), Interval = 60 },
        PushoverConfig = new PushoverConfig(),
        DiscordConfig = new DiscordConfig(),
    };

    [TestMethod]
    public void Update_AppliesName_And_Url()
    {
        var site = MakeSite();
        var update = MakeUpdate();

        site.Update(update);

        Assert.AreEqual("Updated", site.Name);
        Assert.AreEqual(new Uri("https://updated.example.com"), site.Url);
    }

    [TestMethod]
    public void Update_AppliesVpnAndScreenshotFlags()
    {
        var site = MakeSite();
        site.Update(MakeUpdate());

        Assert.IsTrue(site.UseVpn);
        Assert.IsTrue(site.AlwaysTakeScreenshot);
    }

    [TestMethod]
    public void Update_AppliesSchedule()
    {
        var site = MakeSite();
        var update = MakeUpdate();

        site.Update(update);

        Assert.IsTrue(site.Schedule.Enabled);
        Assert.AreEqual(new TimeOnly(9, 0), site.Schedule.Start);
        Assert.AreEqual(new TimeOnly(17, 0), site.Schedule.End);
        Assert.AreEqual(60u, site.Schedule.Interval);
    }
}
