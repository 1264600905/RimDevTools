using System.Text.RegularExpressions;

namespace RimDevTools;

public static class DoctorCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        var context = ProjectDiscovery.Find(Environment.CurrentDirectory, out var discoveryError);
        var checks = new List<object>();
        if (context is null)
        {
            checks.Add(new { id = "project.discovery", status = "missing", required = true, path = (string?)null, message = discoveryError?.Message });
            return Task.FromResult(CliResult.WriteSuccess("doctor", null, new { checks }, [discoveryError?.Code ?? "PROJECT_NOT_FOUND"]));
        }

        checks.Add(new { id = "project.discovery", status = "available", required = true, path = context.ConfigPath, message = (string?)null });
        AddFileCheck(checks, "game.executable", Path.Combine(context.Config.RimWorldDir ?? "", "RimWorldWin64.exe"), true);
        AddFileCheck(checks, "game.config", GameEnvironment.ConfigDirectory(), false);
        AddFileCheck(checks, "game.player_log", GameEnvironment.PlayerLogPath(), false);
        foreach (var projectFile in context.Config.CsprojPaths)
            AddFileCheck(checks, $"deploy.csproj.{projectFile}", Path.Combine(context.Root, projectFile), true);

        if (context.Config.ShaderBundle.Enabled)
        {
            AddFileCheck(checks, "deploy.unity", context.Config.ShaderBundle.UnityExePath, true);
            AddFileCheck(checks, "deploy.unity_project", Path.Combine(context.Root, context.Config.ShaderBundle.UnityProjectPath ?? ""), true);
        }
        else checks.Add(new { id = "deploy.unity", status = "disabled", required = false, path = context.Config.ShaderBundle.UnityExePath, message = (string?)null });

        var dbPath = ResolveDbPath(context);
        AddFileCheck(checks, "data.database", dbPath, false);
        var ilspy = ResolveIlspyPath(context);
        AddFileCheck(checks, "ref.ilspy", ilspy, false);
        var invalidRegex = false;
        if (!string.IsNullOrWhiteSpace(context.Config.ProtectedFilesRegex))
        {
            try { _ = new Regex(context.Config.ProtectedFilesRegex); } catch { invalidRegex = true; }
        }
        checks.Add(new { id = "deploy.protected_files_regex", status = invalidRegex ? "invalid" : "available", required = true, path = (string?)null, message = invalidRegex ? "正则表达式无法编译" : null });
        var result = new { projectRoot = context.Root, configVersion = context.Config.ConfigVersion, checks };
        return Task.FromResult(CliResult.WriteSuccess("doctor", context.Root, result));
    }

    internal static string? ResolveDbPath(ProjectContext context)
        => string.IsNullOrWhiteSpace(context.Config.RimSearcherDbPath) ? null : Path.GetFullPath(context.Config.RimSearcherDbPath, context.Root);

    internal static string? ResolveIlspyPath(ProjectContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Config.IlspycmdPath)) return null;
        return Path.GetFullPath(context.Config.IlspycmdPath, Path.Combine(context.Root, ".tools"));
    }

    private static void AddFileCheck(List<object> checks, string id, string? path, bool required)
    {
        var full = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        var available = full is not null && (File.Exists(full) || Directory.Exists(full));
        checks.Add(new { id, status = available ? "available" : required ? "missing" : "not_applicable", required, path = full, message = available ? null : required ? "路径不存在" : "未配置或未发现" });
    }
}

internal static class GameEnvironment
{
    public static string ConfigDirectory()
    {
        var overridePath = Environment.GetEnvironmentVariable("RIMDEVTOOLS_CONFIG_DIR");
        return !string.IsNullOrWhiteSpace(overridePath) ? Path.GetFullPath(overridePath) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Ludeon Studios", "RimWorld by Ludeon Studios", "Config");
    }

    public static string PlayerLogPath() => Path.Combine(Directory.GetParent(ConfigDirectory())?.FullName ?? ConfigDirectory(), "Player.log");
    public static string ModsConfigPath() => Path.Combine(ConfigDirectory(), "ModsConfig.xml");
    public static string LogSyncSettingsPath() => Path.Combine(ConfigDirectory(), "Mod_LogSyncMod_LogSyncMod.xml");
}
