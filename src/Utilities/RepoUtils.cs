using System.Diagnostics.CodeAnalysis;

namespace SiteChecker.Utilities;

public static class RepoUtils
{
    private static string? GetRepoDirectoryInternal()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.GetFiles(dir.FullName, "SiteChecker.slnx").Length != 0)
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return null;
    }

    public static bool TryGetRepoDirectory([NotNullWhen(true)] out string? path)
    {
        path = GetRepoDirectoryInternal();
        return path != null;
    }

    public static string GetRepoDirectory()
    {
        return GetRepoDirectoryInternal()
            ?? throw new DirectoryNotFoundException("Could not find repository directory.");
    }
}
