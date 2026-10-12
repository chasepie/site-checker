namespace SiteChecker.Utilities.UnitTests;

using SiteChecker.Utilities;

[TestClass]
public sealed class AppDirectoriesTests
{
    [TestMethod]
    public void RunFromTheRepo_EveryDirectoryIsInItsSiteCheckerFolder()
    {
        var siteChecker = Path.Join(RepoUtils.GetRepoDirectory(), "site-checker");

        Assert.AreEqual(Path.Join(siteChecker, "data"), AppDirectories.Data);
        Assert.AreEqual(Path.Join(siteChecker, "logs"), AppDirectories.Logs);
        Assert.AreEqual(Path.Join(siteChecker, "pia"), AppDirectories.Pia);
    }
}
