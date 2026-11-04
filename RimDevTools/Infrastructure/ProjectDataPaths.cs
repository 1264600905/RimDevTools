namespace RimDevTools;

internal static class ProjectDataPaths
{
    internal static string DirectoryPath(string root)
    {
        var path = Path.Combine(Path.GetFullPath(root), ".tools", "ProjectData");
        PathSafety.EnsureNoReparsePoints(root, path);
        return path;
    }
    internal static string File(string root, string name)
    {
        if (Path.GetFileName(name) != name) throw new ArgumentException("工具数据文件名不能包含目录");
        var path = Path.Combine(DirectoryPath(root), name);
        PathSafety.EnsureNoReparsePoints(root, path);
        return path;
    }
    internal static bool IsDataPath(string path)
        => Path.GetFullPath(path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Zip(Path.GetFullPath(path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Skip(1))
            .Any(pair => pair.First.Equals(".tools", StringComparison.OrdinalIgnoreCase) && pair.Second.Equals("ProjectData", StringComparison.OrdinalIgnoreCase));
}
