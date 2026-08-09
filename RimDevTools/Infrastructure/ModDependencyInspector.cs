using System.Xml.Linq;

namespace RimDevTools;

public sealed record ModDependencyWarning(
    string ModPackageId,
    string ModName,
    string MissingDependencyPackageId,
    string? MissingDependencyName,
    bool DependencyInstalled,
    string ModPath);

public static class ModDependencyInspector
{
    public static IReadOnlyList<ModDependencyWarning> Inspect(ProjectContext context, IReadOnlyCollection<string> activeMods)
    {
        var normalizedActive = new HashSet<string>(activeMods.Where(value => !string.IsNullOrWhiteSpace(value)), StringComparer.OrdinalIgnoreCase);
        var index = ModInstallationIndex.Build(context.Config.RimWorldDir, context.Config.WorkshopPaths, context.Root);
        var warnings = new List<ModDependencyWarning>();

        foreach (var packageId in normalizedActive)
        {
            var installation = index.Find(packageId);
            if (installation is null) continue;

            var aboutPath = Path.Combine(installation.Directory, "About", "About.xml");
            if (!TryReadMetadata(aboutPath, context.Config.GameVersion, out var modName, out var dependencies)) continue;
            foreach (var dependency in dependencies)
            {
                if (normalizedActive.Contains(dependency.PackageId)) continue;
                warnings.Add(new ModDependencyWarning(
                    packageId,
                    modName,
                    dependency.PackageId,
                    dependency.Name,
                    index.Find(dependency.PackageId) is not null,
                    installation.Directory));
            }
        }

        return warnings
            .GroupBy(warning => string.Join("|", warning.ModPackageId, warning.MissingDependencyPackageId), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(warning => warning.ModPackageId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(warning => warning.MissingDependencyPackageId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string FormatWarning(ModDependencyWarning warning)
    {
        var dependency = string.IsNullOrWhiteSpace(warning.MissingDependencyName)
            ? warning.MissingDependencyPackageId
            : $"{warning.MissingDependencyName} ({warning.MissingDependencyPackageId})";
        var availability = warning.DependencyInstalled ? "已安装但未启用" : "未找到安装目录";
        return $"启用模组 {warning.ModName} ({warning.ModPackageId}) 缺少必要上级依赖 {dependency}：{availability}；本轮仍会继续执行。";
    }

    private static bool TryReadMetadata(string aboutPath, string? gameVersion, out string modName, out IReadOnlyList<ModDependency> dependencies)
    {
        modName = Path.GetFileName(Directory.GetParent(aboutPath)?.Parent?.FullName ?? aboutPath);
        dependencies = [];
        try
        {
            var root = XDocument.Load(aboutPath).Root;
            if (root is null) return false;
            modName = ElementValue(root, "name") ?? modName;
            var values = new List<ModDependency>();
            AddDependencies(root.Elements().FirstOrDefault(element => element.Name.LocalName.Equals("modDependencies", StringComparison.OrdinalIgnoreCase)), values);

            var versioned = root.Elements().FirstOrDefault(element => element.Name.LocalName.Equals("modDependenciesByVersion", StringComparison.OrdinalIgnoreCase));
            if (versioned is not null)
            {
                var versionNode = !string.IsNullOrWhiteSpace(gameVersion)
                    ? versioned.Elements().FirstOrDefault(element => element.Name.LocalName.Equals($"v{gameVersion}", StringComparison.OrdinalIgnoreCase))
                    : null;
                versionNode ??= versioned.Elements().LastOrDefault();
                AddDependencies(versionNode, values);
            }

            dependencies = values
                .Where(dependency => !string.IsNullOrWhiteSpace(dependency.PackageId))
                .GroupBy(dependency => dependency.PackageId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void AddDependencies(XElement? container, List<ModDependency> dependencies)
    {
        if (container is null) return;
        foreach (var item in container.Elements().Where(element => element.Name.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase)))
        {
            var packageId = ElementValue(item, "packageId");
            if (!string.IsNullOrWhiteSpace(packageId)) dependencies.Add(new ModDependency(packageId, ElementValue(item, "displayName")));
        }
    }

    private static string? ElementValue(XElement element, string name)
        => element.Elements().FirstOrDefault(child => child.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Trim();

    private sealed record ModDependency(string PackageId, string? Name);
}
