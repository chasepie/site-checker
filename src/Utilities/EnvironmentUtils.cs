namespace SiteChecker.Utilities;

public static class EnvironmentUtils
{
    public static bool IsDockerContainer()
    {
        var inDockerEnv = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
        return bool.TryParse(inDockerEnv, out var result) && result;
    }
}
