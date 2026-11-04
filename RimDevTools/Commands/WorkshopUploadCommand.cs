using System.Globalization;
using System.Security.Cryptography;

namespace RimDevTools;

internal sealed record WorkshopContentFile(string Path, long Bytes, string Sha256);

public static class WorkshopUploadCommand
{
    internal static ulong? ReadId(string path)
    {
        if (!File.Exists(path)) return null;
        if (!ulong.TryParse(File.ReadAllText(path).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id is 0 or ulong.MaxValue) throw new InvalidDataException("无效的 PublishedFileId: " + path);
        return id;
    }

    internal static WorkshopContentFile[] InspectContent(string root)
    {
        var files = new List<WorkshopContentFile>();
        var pending = new Stack<string>();
        pending.Push(root);
        var prohibited = new HashSet<string>([".git", ".tools", ".agents", ".claude", ".rimdev", "source", "bin", "obj", "TestResults"], StringComparer.OrdinalIgnoreCase);
        while (pending.TryPop(out var directory))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("拒绝上传重解析点: " + path);
                if (prohibited.Contains(Path.GetFileName(path))) throw new InvalidDataException("发布目录含开发文件: " + path);
                if (Directory.Exists(path)) { pending.Push(path); continue; }
                if (path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("发布目录含禁止的构建产物: " + path);
                using var stream = File.OpenRead(path);
                files.Add(new(Path.GetRelativePath(root, path).Replace('\\', '/'), stream.Length, Convert.ToHexString(SHA256.HashData(stream))));
            }
        }
        return files.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
    }

}
