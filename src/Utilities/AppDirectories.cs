namespace SiteChecker.Utilities;

public static class AppDirectories
{
    /// <summary>
    /// Where the app keeps its state (the database and the login keys): <c>site-checker/data</c> in
    /// the repo when run locally, and <c>data</c> next to the app otherwise (<c>/app/data</c> in
    /// Docker). It may not exist yet. Found once, since finding the repo walks the directory tree and
    /// every <c>SiteCheckerDbContext</c> without a configured provider asks for it.
    /// </summary>
    public static string Data { get; }
        = !EnvironmentUtils.IsDockerContainer() && RepoUtils.TryGetRepoDirectory(out var repoRoot)
            ? Path.Join(repoRoot, "site-checker/data")
            : Path.Join(AppContext.BaseDirectory, "data");
}
