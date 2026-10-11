namespace SiteChecker.Utilities;

/// <summary>
/// Where the app keeps its files: in the repo's <c>site-checker</c> folder when run locally from
/// it, and otherwise where Docker Compose mounts them. None of them may exist yet. Found once, since
/// finding the repo walks the directory tree and every <c>SiteCheckerDbContext</c> without a
/// configured provider asks for <see cref="Data"/>.
/// </summary>
public static class AppDirectories
{
    /// <summary>
    /// The repo's <c>site-checker</c> folder, or <c>null</c> in Docker or outside a repo.
    /// </summary>
    private static readonly string? LocalRoot
        = !EnvironmentUtils.IsDockerContainer() && RepoUtils.TryGetRepoDirectory(out var repoRoot)
            ? Path.Join(repoRoot, "site-checker")
            : null;

    /// <summary>
    /// The app's state (the database and the login keys): <c>site-checker/data</c> locally, and
    /// <c>data</c> next to the app otherwise (<c>/app/data</c> in Docker).
    /// </summary>
    public static string Data { get; } = Path.Join(LocalRoot ?? AppContext.BaseDirectory, "data");

    /// <summary>
    /// The failure dumps: <c>site-checker/logs</c> locally, and <c>logs</c> next to the app
    /// otherwise (<c>/app/logs</c> in Docker).
    /// </summary>
    public static string Logs { get; } = Path.Join(LocalRoot ?? AppContext.BaseDirectory, "logs");

    /// <summary>
    /// The folder shared with the VPN container, which holds its location: <c>site-checker/pia</c>
    /// locally, and <c>/pia</c> otherwise, where Compose mounts it in both containers.
    /// </summary>
    public static string Pia { get; } = LocalRoot is null ? "/pia" : Path.Join(LocalRoot, "pia");
}
