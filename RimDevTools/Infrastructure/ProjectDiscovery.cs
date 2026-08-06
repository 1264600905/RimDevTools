namespace RimDevTools;

public sealed record ProjectContext(string Root, string ConfigPath, ProjectConfig Config);

public static class ProjectDiscovery
{
    public static ProjectContext? Find(string? startDirectory, out CliError? error)
    {
        error = null;
        var current = Path.GetFullPath(startDirectory ?? Environment.CurrentDirectory);
        var candidates = new[] { Path.Combine(current, "mod_config.jsonc"), Path.Combine(Directory.GetParent(current)?.FullName ?? current, "mod_config.jsonc") };
        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) continue;
            try { return new ProjectContext(Path.GetDirectoryName(path)!, path, ProjectConfig.Load(path)); }
            catch (Exception ex) { error = new CliError("CONFIG_INVALID", $"无法解析配置文件: {ex.Message}"); return null; }
        }
        error = new CliError("PROJECT_NOT_FOUND", "当前目录及其直接父目录中均未找到 mod_config.jsonc");
        return null;
    }
}
