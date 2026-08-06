namespace RimDevTools;

internal static class PathSafety
{
    public static bool IsWithin(string child, string parent)
    {
        var normalizedChild = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return normalizedChild.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    public static void EnsureNoReparsePoints(string root, string path)
    {
        var fullPath = Path.GetFullPath(path);
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!IsWithin(fullPath, normalizedRoot)) throw new IOException($"路径越出受控目录: {fullPath}");
        if ((File.Exists(fullPath) || Directory.Exists(fullPath)) && File.GetAttributes(fullPath).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException($"拒绝使用 Reparse Point: {fullPath}");

        var current = Directory.Exists(fullPath) ? new DirectoryInfo(fullPath) : new FileInfo(fullPath).Directory;
        while (current is not null && current.FullName.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException($"拒绝使用 Reparse Point: {current.FullName}");
            if (current.FullName.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)) break;
            current = current.Parent;
        }
    }
}
