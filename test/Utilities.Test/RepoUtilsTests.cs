namespace SiteChecker.Utilities.Test;

using SiteChecker.Utilities;

[TestClass]
public sealed class RepoUtilsTests
{
    [TestMethod]
    public void GetRepoDirectory_ReturnsNonEmptyString()
    {
        var path = RepoUtils.GetRepoDirectory();

        Assert.IsFalse(string.IsNullOrEmpty(path));
    }

    [TestMethod]
    public void GetRepoDirectory_ReturnedPathExists()
    {
        var path = RepoUtils.GetRepoDirectory();

        Assert.IsTrue(Directory.Exists(path));
    }

    [TestMethod]
    public void GetRepoDirectory_ReturnedPathContainsSolutionFile()
    {
        var path = RepoUtils.GetRepoDirectory();

        var slnxFiles = Directory.GetFiles(path, "SiteChecker.slnx");
        Assert.HasCount(1, slnxFiles);
    }

    [TestMethod]
    public void TryGetRepoDirectory_ReturnsTrue()
    {
        var result = RepoUtils.TryGetRepoDirectory(out _);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void TryGetRepoDirectory_OutParamMatchesGetRepoDirectory()
    {
        RepoUtils.TryGetRepoDirectory(out var tryPath);
        var getPath = RepoUtils.GetRepoDirectory();

        Assert.AreEqual(getPath, tryPath);
    }

    [TestMethod]
    public void TryGetRepoDirectory_OutParamIsNonNull_WhenReturnsTrue()
    {
        var result = RepoUtils.TryGetRepoDirectory(out var path);

        Assert.IsTrue(result);
        Assert.IsNotNull(path);
    }
}
