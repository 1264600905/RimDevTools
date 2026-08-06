namespace RimDevTools;

public static class AtomicFile
{
    public static void WriteAllText(string path, string content)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = full + ".tmp." + Guid.NewGuid().ToString("N");
        File.WriteAllText(temp, content);
        File.Move(temp, full, true);
    }
}
