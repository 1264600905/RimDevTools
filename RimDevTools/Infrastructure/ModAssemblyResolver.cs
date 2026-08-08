using System.Xml.Linq;

namespace RimDevTools;

public sealed record ModAssemblyResolution(string? AssemblyPath, IReadOnlyList<string> SearchedPaths, string? Error);

public static class ModAssemblyResolver
{
    public static ModAssemblyResolution Resolve(string modDirectory, string? gameVersion, string assemblyFileName)
    {
        var relativeRoots = new List<string>();
        var loadFolders = Path.Combine(modDirectory, "LoadFolders.xml");
        if (File.Exists(loadFolders) && !string.IsNullOrWhiteSpace(gameVersion))
        {
            try
            {
                var versionNode = XDocument.Load(loadFolders).Root?.Elements().FirstOrDefault(element => element.Name.LocalName.Equals($"v{gameVersion}", StringComparison.OrdinalIgnoreCase));
                if (versionNode is not null) relativeRoots.AddRange(versionNode.Elements().Where(element => element.Name.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase)).Select(element => element.Value.Trim()));
            }
            catch (Exception ex)
            {
                return new ModAssemblyResolution(null, [], $"LoadFolders.xml 无法解析: {ex.Message}");
            }
        }
        if (relativeRoots.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(gameVersion)) relativeRoots.Add(gameVersion);
            relativeRoots.Add("");
        }

        var searched = new List<string>();
        foreach (var relative in relativeRoots.Concat(string.IsNullOrWhiteSpace(gameVersion) ? [] : [gameVersion]).Append("").Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var root = relative is "/" or "" || relative == @"\"
                ? modDirectory
                : Path.Combine(modDirectory, relative.TrimStart('/', Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            foreach (var candidate in new[] { Path.Combine(root, "Assemblies", assemblyFileName), Path.Combine(root, assemblyFileName) })
            {
                var full = Path.GetFullPath(candidate);
                if (!searched.Contains(full, StringComparer.OrdinalIgnoreCase)) searched.Add(full);
                if (File.Exists(full)) return new ModAssemblyResolution(full, searched, null);
            }
        }
        return new ModAssemblyResolution(null, searched, $"未找到 {assemblyFileName}");
    }
}
