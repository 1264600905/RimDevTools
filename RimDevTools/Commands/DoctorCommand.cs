using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RimDevTools;

public sealed record DoctorCheck(string Id, string Status, bool Required, string? Path, string? Message = null, object? Details = null);

public static class DoctorCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        var context = ProjectDiscovery.Find(Environment.CurrentDirectory, out var discoveryError);
        var checks = new List<DoctorCheck>();
        if (context is null)
        {
            checks.Add(new DoctorCheck("project.discovery", discoveryError?.Code == "CONFIG_INVALID" ? "invalid" : "missing", true, null, discoveryError?.Message));
            return Task.FromResult(CliResult.WriteSuccess("doctor", null, new { checks }, [discoveryError?.Code ?? "PROJECT_NOT_FOUND"]));
        }

        var config = context.Config;
        checks.Add(new DoctorCheck("project.discovery", "available", true, context.ConfigPath));
        checks.Add(new DoctorCheck(
            "project.config_version",
            config.ConfigVersion == 2 ? "available" : "invalid",
            true,
            context.ConfigPath,
            config.ConfigVersion == 2 ? null : $"需要 config_version=2，当前为 {config.ConfigVersion}"));

        AddPathCheck(checks, "game.directory", config.RimWorldDir, true, PathKind.Directory);
        AddPathCheck(checks, "game.executable", string.IsNullOrWhiteSpace(config.RimWorldDir) ? null : Path.Combine(config.RimWorldDir, "RimWorldWin64.exe"), true, PathKind.File);
        AddPathCheck(checks, "game.config_directory", GameEnvironment.ConfigDirectory(), false, PathKind.Directory);
        AddPathCheck(checks, "game.mods_config", GameEnvironment.ModsConfigPath(), false, PathKind.File);
        AddPathCheck(checks, "game.player_log", GameEnvironment.PlayerLogPath(), false, PathKind.File);

        var steamRunning = Process.GetProcessesByName("steam").Length > 0;
        var steamPath = FindSteamExecutable();
        checks.Add(new DoctorCheck("game.steam", steamRunning || steamPath is not null ? "available" : "missing", false, steamPath, steamRunning ? "Steam 正在运行" : steamPath is null ? "未发现 Steam；直接启动游戏仍可用" : "Steam 已安装但未运行"));

        var dotnet = FindOnPath("dotnet.exe");
        checks.Add(new DoctorCheck("deploy.dotnet", dotnet is null ? "missing" : "available", config.CsprojPaths.Count > 0, dotnet, dotnet is null ? "PATH 中未找到 dotnet.exe" : null));
        foreach (var projectFile in config.CsprojPaths)
        {
            var path = ResolveProjectPath(context, projectFile);
            var safe = path is not null && PathSafety.IsWithin(path, context.Root);
            if (!safe) checks.Add(new DoctorCheck($"deploy.csproj.{projectFile}", "invalid", true, path, "工程路径越出项目目录"));
            else AddPathCheck(checks, $"deploy.csproj.{projectFile}", path, true, PathKind.File);
        }

        if (config.ShaderBundle.Enabled)
        {
            AddPathCheck(checks, "deploy.unity", config.ShaderBundle.UnityExePath, true, PathKind.File);
            AddPathCheck(checks, "deploy.unity_project", ResolveProjectPath(context, config.ShaderBundle.UnityProjectPath), true, PathKind.Directory);
            checks.Add(new DoctorCheck("deploy.unity_method", string.IsNullOrWhiteSpace(config.ShaderBundle.BuildMethod) ? "missing" : "available", true, null, string.IsNullOrWhiteSpace(config.ShaderBundle.BuildMethod) ? "shader_bundle.build_method 未配置" : null));
        }
        else
        {
            checks.Add(new DoctorCheck("deploy.unity", "disabled", false, config.ShaderBundle.UnityExePath));
        }

        var regexStatus = ValidateRegex(config.ProtectedFilesRegex, out var regexMessage);
        checks.Add(new DoctorCheck("deploy.protected_files_regex", regexStatus, true, null, regexMessage));

        for (var index = 0; index < config.WorkshopPaths.Count; index++)
            AddPathCheck(checks, $"ref.workshop.{index}", config.WorkshopPaths[index], false, PathKind.Directory);
        AddPathCheck(checks, "ref.ilspy", ResolveIlspyPath(context), false, PathKind.File);

        var modRoots = ModRoots(context).ToArray();
        AddModCheck(checks, "test.logsync_mod", modRoots, "trigger.logsyncmod", true);
        var logSyncDirectory = FindModDirectory(modRoots, "trigger.logsyncmod");
        var logSyncSettings = GameEnvironment.LogSyncSettingsPath();
        checks.Add(new DoctorCheck(
            "test.logsync_settings",
            File.Exists(logSyncSettings) ? "available" : "not_applicable",
            false,
            logSyncSettings,
            File.Exists(logSyncSettings) ? null : "设置文件尚不存在；test run 会在事务内临时创建"));
        var autoTestAssembly = logSyncDirectory is null ? null : Directory.EnumerateFiles(logSyncDirectory, "*AutoTest*.dll", SearchOption.AllDirectories).FirstOrDefault();
        checks.Add(new DoctorCheck("test.autotest_extension", autoTestAssembly is null ? "missing" : "available", true, autoTestAssembly, autoTestAssembly is null ? "LogSync 模组中未发现 AutoTest 程序集" : null));

        AddModCheck(checks, "data.rimsearcher_datamod", modRoots, "kearril.RimSearcherDataMod", false);
        var installedDataMod = FindModDirectory(modRoots, "kearril.RimSearcherDataMod");
        var rimSearcher = ComponentLocator.RimSearcherPath();
        AddPathCheck(checks, "data.rimsearcher_cli", rimSearcher, false, PathKind.File);
        var rimSearcherVersion = File.Exists(rimSearcher) ? FileVersionInfo.GetVersionInfo(rimSearcher).FileVersion : null;
        var dataModAssembly = installedDataMod is null ? null : Path.Combine(installedDataMod, "Assemblies", "RimSearcher.DataMod.dll");
        var dataModVersion = dataModAssembly is null ? null : ComponentLocator.AssemblyVersion(dataModAssembly);
        checks.Add(new DoctorCheck(
            "data.component_version",
            dataModVersion is null ? "missing" : CliApplication.VersionsEqual(rimSearcherVersion, dataModVersion) ? "available" : "stale",
            false,
            dataModAssembly,
            dataModVersion is null ? "未发现 DataMod 程序集版本" : CliApplication.VersionsEqual(rimSearcherVersion, dataModVersion) ? null : "RimSearcher CLI 与 DataMod 版本不一致",
            new { rimSearcherVersion, dataModVersion }));

        var databasePath = ResolveDbPath(context);
        if (databasePath is null)
        {
            checks.Add(new DoctorCheck("data.database", "not_applicable", false, null, "rimsearcher_db_path 未配置"));
        }
        else if (!File.Exists(databasePath))
        {
            checks.Add(new DoctorCheck("data.database", "missing", false, databasePath, "数据库不存在；建议运行 RimDevTools data refresh"));
        }
        else
        {
            var status = DatabaseInspector.Inspect(databasePath, "project_config", config.GameVersion, config.CurrentPackageId(context.Root));
            checks.Add(new DoctorCheck(
                "data.database",
                !status.CanOpen || status.Integrity != "ok" ? "invalid" : status.RefreshRecommended ? "stale" : "available",
                false,
                databasePath,
                status.RefreshRecommended ? "数据库需要重新导出" : null,
                status));
        }

        var lockPath = Path.Combine(GameEnvironment.ConfigDirectory(), "RimDevTools.lock.json");
        var lockBusy = EnvironmentLock.IsBusy(GameEnvironment.ConfigDirectory());
        var lockInfoExists = File.Exists(lockPath);
        checks.Add(new DoctorCheck(
            "environment.lock",
            lockBusy ? "busy" : lockInfoExists ? "stale" : "available",
            false,
            lockPath,
            lockBusy ? "RimWorld 环境互斥锁正被占用" : lockInfoExists ? "互斥锁未占用，但存在遗留诊断文件" : null));
        var pendingTransactions = TransactionRecovery.PendingDirectories(GameEnvironment.ConfigDirectory());
        checks.Add(new DoctorCheck("environment.transactions", pendingTransactions.Count > 0 ? "stale" : "available", false, null, pendingTransactions.Count > 0 ? "存在未完成事务" : null, new { directories = pendingTransactions }));

        var result = new
        {
            projectRoot = context.Root,
            configVersion = config.ConfigVersion,
            rimDevToolsVersion = typeof(DoctorCommand).Assembly.GetName().Version?.ToString(),
            rimSearcherVersion,
            dataModVersion,
            checks
        };
        return Task.FromResult(CliResult.WriteSuccess("doctor", context.Root, result));
    }

    internal static string? ResolveDbPath(ProjectContext context)
        => string.IsNullOrWhiteSpace(context.Config.RimSearcherDbPath) ? null : Path.GetFullPath(context.Config.RimSearcherDbPath, context.Root);

    internal static string? ResolveIlspyPath(ProjectContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Config.IlspycmdPath)) return null;
        return Path.GetFullPath(context.Config.IlspycmdPath, Path.Combine(context.Root, ".tools"));
    }

    private static string? ResolveProjectPath(ProjectContext context, string? path)
        => string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path, context.Root);

    private static IEnumerable<string> ModRoots(ProjectContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.Config.RimWorldDir)) yield return Path.Combine(context.Config.RimWorldDir, "Mods");
        foreach (var workshop in context.Config.WorkshopPaths) yield return workshop;
    }

    private static void AddModCheck(List<DoctorCheck> checks, string id, IEnumerable<string> roots, string packageId, bool required)
    {
        var path = FindModDirectory(roots, packageId);
        checks.Add(new DoctorCheck(id, path is null ? required ? "missing" : "not_applicable" : "available", required, path, path is null ? $"未找到 {packageId}" : null));
    }

    private static string? FindModDirectory(IEnumerable<string> roots, string packageId)
    {
        foreach (var root in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                var about = Path.Combine(directory, "About", "About.xml");
                if (!File.Exists(about)) continue;
                try
                {
                    var candidate = XDocument.Load(about).Root?.Elements().FirstOrDefault(x => x.Name.LocalName.Equals("packageId", StringComparison.OrdinalIgnoreCase))?.Value.Trim();
                    if (candidate?.Equals(packageId, StringComparison.OrdinalIgnoreCase) == true) return directory;
                }
                catch { }
            }
        }
        return null;
    }

    private static string ValidateRegex(string? pattern, out string? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(pattern)) return "available";
        try { _ = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); return "available"; }
        catch (Exception ex) { message = ex.Message; return "invalid"; }
    }

    private static void AddPathCheck(List<DoctorCheck> checks, string id, string? path, bool required, PathKind kind)
    {
        var full = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        var available = full is not null && (kind == PathKind.File ? File.Exists(full) : Directory.Exists(full));
        checks.Add(new DoctorCheck(id, available ? "available" : required ? "missing" : "not_applicable", required, full, available ? null : required ? "路径不存在" : "未配置或未发现"));
    }

    private static string? FindSteamExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steam.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steam.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steam", "steam.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? FindOnPath(string fileName)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var path = Path.Combine(directory.Trim('"'), fileName);
                if (File.Exists(path)) return path;
            }
            catch { }
        }
        return null;
    }

    private enum PathKind { File, Directory }
}

internal static class GameEnvironment
{
    public static string ConfigDirectory()
    {
        var overridePath = Environment.GetEnvironmentVariable("RIMDEVTOOLS_CONFIG_DIR");
        return !string.IsNullOrWhiteSpace(overridePath)
            ? Path.GetFullPath(overridePath)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Ludeon Studios", "RimWorld by Ludeon Studios", "Config");
    }

    public static string PlayerLogPath() => Path.Combine(Directory.GetParent(ConfigDirectory())?.FullName ?? ConfigDirectory(), "Player.log");
    public static string ModsConfigPath() => Path.Combine(ConfigDirectory(), "ModsConfig.xml");
    public static string LogSyncSettingsPath() => Path.Combine(ConfigDirectory(), "Mod_LogSyncMod_LogSyncMod.xml");
}
