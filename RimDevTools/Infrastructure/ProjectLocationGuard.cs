namespace RimDevTools;

public sealed record ProhibitedProjectLocation(string Kind, string Root);

internal static class ProjectLocationGuard
{
    public static IReadOnlyList<ProhibitedProjectLocation> FindDeploymentLocations(
        string projectRoot,
        string? rimWorldDirectory,
        IEnumerable<string> workshopPaths)
    {
        var locations = new List<ProhibitedProjectLocation>();
        if (!string.IsNullOrWhiteSpace(rimWorldDirectory))
            AddIfContainsProject(locations, "rimworld_mods", Path.Combine(rimWorldDirectory, "Mods"), projectRoot);
        foreach (var workshopPath in workshopPaths)
            AddIfContainsProject(locations, "steam_workshop", workshopPath, projectRoot);
        return locations
            .DistinctBy(location => location.Root, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static CliError? CreateDeploymentError(
        string projectRoot,
        string? rimWorldDirectory,
        IEnumerable<string> workshopPaths)
    {
        var locations = FindDeploymentLocations(projectRoot, rimWorldDirectory, workshopPaths);
        if (locations.Count == 0) return null;
        return new CliError(
            "PROJECT_DEPLOYMENT_LOCATION_INVALID",
            "项目位于 RimWorld Mods 或 Steam Workshop 内容目录中。请将项目移动到独立工作目录；deploy 会自动同步到 RimWorld/Mods。",
            new { projectRoot = Path.GetFullPath(projectRoot), locations });
    }

    private static void AddIfContainsProject(List<ProhibitedProjectLocation> locations, string kind, string? root, string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(root)) return;
        var fullRoot = Path.GetFullPath(root);
        if (PathSafety.IsWithinOrEqual(projectRoot, fullRoot)) locations.Add(new ProhibitedProjectLocation(kind, fullRoot));
    }
}
