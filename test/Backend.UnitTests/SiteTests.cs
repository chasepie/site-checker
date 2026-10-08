namespace SiteChecker.Backend.UnitTests;

using SiteChecker.Database.Model;

[TestClass]
public sealed class SiteTests
{
    [TestMethod]
    public void Update_CopiesEveryEditableSetting()
    {
        var site = new Site { Name = "Old", Url = new Uri("https://old.example"), ScraperId = "SCRAPER" };
        var update = new SiteUpdate
        {
            Id = site.Id,
            Name = "New",
            Url = new Uri("https://new.example"),
            UseVpn = true,
            AlwaysTakeScreenshot = true,
            KnownFailuresThreshold = 2,
        };

        site.Update(update);

        Assert.AreEqual("New", site.Name);
        Assert.AreEqual(new Uri("https://new.example"), site.Url);
        Assert.IsTrue(site.UseVpn);
        Assert.IsTrue(site.AlwaysTakeScreenshot);
        Assert.AreEqual(2, site.KnownFailuresThreshold);
    }
}
