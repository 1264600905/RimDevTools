using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RimDevTools;

public sealed record DeploymentArtifactAction(string Path, string Kind, string Action, string? Reason = null, string? PackageId = null);

public static partial class DeploymentArtifactRecovery
{
    [GeneratedRegex(@"^\.rimdev-(?<kind>backup|staging)-[0-9a-f]{32}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ArtifactNameRegex();

    public static IReadOnlyList<DeploymentArtifactAction> Plan(string modsRoot, string target, string? packageId)
    {
        var fullRoot = Path.GetFullPath(modsRoot);
        var fullTarget = Path.GetFullPath(target);
        if (!Directory.Exists(fullRoot)) return [];

        var candidates = new List<(string Path, string Kind, string? PackageId, string? Error)>();
        foreach (var directory in Directory.EnumerateDirectories(fullRoot))
        {
            var match = ArtifactNameRegex().Match(Path.GetFileName(directory));
            if (!match.Success) continue;
            var full = Path.GetFullPath(directory);
            if (!PathSafety.IsWithin(full, fullRoot)) continue;
            try
            {
                PathSafety.EnsureNoReparsePoints(fullRoot, full);
                candidates.Add((full, match.Groups["kind"].Value.ToLowerInvariant(), ReadPackageId(full), null));
            }
            catch (Exception ex)
            {
                candidates.Add((full, match.Groups["kind"].Value.ToLowerInvariant(), null, ex.Message));
            }
        }

        var actions = new List<DeploymentArtifactAction>();
        foreach (var staging in candidates.Where(x => x.Kind == "staging"))
            actions.Add(staging.Error is null
                ? new(staging.Path, staging.Kind, "remove", "遗留部署暂存目录")
                : new(staging.Path, staging.Kind, "retain", staging.Error));

        var backups = candidates.Where(x => x.Kind == "backup").ToArray();
        var owned = string.IsNullOrWhiteSpace(packageId)
            ? []
            : backups.Where(x => x.Error is null && string.Equals(x.PackageId, packageId, StringComparison.OrdinalIgnoreCase)).ToArray();

        foreach (var backup in backups)
        {
            if (backup.Error is not null)
            {
                actions.Add(new(backup.Path, backup.Kind, "retain", backup.Error));
                continue;
            }
            if (!string.Equals(backup.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
            {
                actions.Add(new(backup.Path, backup.Kind, "ignore", "无法确认属于当前模组", backup.PackageId));
                continue;
            }

            if (Directory.Exists(fullTarget))
                actions.Add(new(backup.Path, backup.Kind, "remove", "当前部署目标存在，旧备份可安全删除", backup.PackageId));
            else if (owned.Length == 1)
                actions.Add(new(backup.Path, backup.Kind, "restore", "当前部署目标缺失，从唯一备份恢复", backup.PackageId));
            else
                actions.Add(new(backup.Path, backup.Kind, "retain", "当前部署目标缺失且存在多个候选备份，拒绝自动选择", backup.PackageId));
        }
        return actions;
    }

    public static IReadOnlyList<DeploymentArtifactAction> Execute(string modsRoot, string target, IReadOnlyList<DeploymentArtifactAction> plan)
    {
        var fullRoot = Path.GetFullPath(modsRoot);
        var fullTarget = Path.GetFullPath(target);
        var results = new List<DeploymentArtifactAction>();
        foreach (var item in plan)
        {
            if (item.Action is "ignore" or "retain")
            {
                results.Add(item);
                continue;
            }

            try
            {
                PathSafety.EnsureNoReparsePoints(fullRoot, item.Path);
                if (item.Action == "remove")
                {
                    if (Directory.Exists(item.Path)) Directory.Delete(item.Path, true);
                    results.Add(item with { Action = "removed" });
                }
                else if (item.Action == "restore")
                {
                    if (Directory.Exists(fullTarget)) throw new IOException($"部署目标已存在: {fullTarget}");
                    Directory.Move(item.Path, fullTarget);
                    results.Add(item with { Action = "restored" });
                }
            }
            catch (Exception ex)
            {
                results.Add(item with { Action = "failed", Reason = ex.Message });
            }
        }
        return results;
    }

    private static string? ReadPackageId(string directory)
    {
        var about = Path.Combine(directory, "About", "About.xml");
        if (!File.Exists(about)) return null;
        try { return XDocument.Load(about).Root?.Elements().FirstOrDefault(x => x.Name.LocalName.Equals("packageId", StringComparison.OrdinalIgnoreCase))?.Value.Trim(); }
        catch { return null; }
    }
}
