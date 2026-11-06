using System.Text.Json;

namespace RimDevTools;

// Independent from the temporary ModSettings transaction. Manual/quick/group runs do not update it.
internal static class LastAutoTestProject
{
    internal static void Record(ProjectContext context, string configDirectory, string runId)
    {
        var root = Path.GetFullPath(context.Root);
        if (!Directory.Exists(root) || string.IsNullOrWhiteSpace(context.Config.CurrentPackageId(root)))
            throw new InvalidDataException("Cannot persist an invalid automatic-test project context");
        AtomicFile.WriteAllText(Path.Combine(configDirectory, "RimDevTools.LastAutoTestProject.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, projectRoot = root, projectIdentity = context.Config.CurrentPackageId(root),
            gameVersion = context.Config.GameVersion, runId, startedAtUtc = DateTimeOffset.UtcNow
        }));
    }
}
