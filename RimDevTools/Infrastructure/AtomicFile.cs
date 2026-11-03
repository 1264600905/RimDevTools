namespace RimDevTools;

public static class AtomicFile
{
    public static void WriteAllText(string path, string content)
        => WriteAllBytes(path, new System.Text.UTF8Encoding(false).GetBytes(content));

    public static void WriteAllBytes(string path, ReadOnlySpan<byte> content)
        => WriteAllBytesCore(path, content, useMoveForReplacement: false);

    // Manifest updates use a same-directory rename; File.Replace can fail with 1175 when a transient handle denies deletion.
    internal static void WriteAllTextUsingMoveReplacement(string path, string content)
        => WriteAllBytesCore(path, new System.Text.UTF8Encoding(false).GetBytes(content), useMoveForReplacement: true);

    private static void WriteAllBytesCore(string path, ReadOnlySpan<byte> content, bool useMoveForReplacement)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = full + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(content);
                stream.Flush(true);
            }
            if (File.Exists(full))
            {
                if (useMoveForReplacement) File.Move(temp, full, true);
                else File.Replace(temp, full, null, true);
            }
            else File.Move(temp, full);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
