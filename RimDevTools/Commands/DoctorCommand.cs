using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RimDevTools;

public sealed record DoctorCheck(string Id, string Status, bool Required, string? Path, string? Message = null, object? Details = null);
public sealed record DoctorSummary(bool Ready, int IssueCount, int WarningCount, IReadOnlyList<DoctorCheck> Checks);

public static class DoctorCommand
{
    private static readonly HashSet<string> ProblemStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "missing", "invalid", "stale", "busy", "unsupported"
    };
    private static readonly HashSet<string> AlwaysVisibleChecks = new(StringComparer.OrdinalIgnoreCase)
    {
        "build.netfx_targeting_pack",
        "build.game_assembly.Assembly-CSharp",
        "build.harmony_assembly"
    };

    public static Task<int> RunAsync(string[] args)
    {
        var verbose = CliArgs.Has(args, "--verbose");
        var context = ProjectDiscovery.Find(Environment.CurrentDirectory, out var discoveryError);
        if (context is null)
        {
            var discoveryChecks = new[] { new DoctorCheck("project.discovery", discoveryError?.Code == "CONFIG_INVALID" ? "invalid" : "missing", true, null, discoveryError?.Message) };
            var discoverySummary = Summarize(discoveryChecks, verbose);
            return Task.FromResult(CliResult.WriteSuccess("doctor", null, new { ready = discoverySummary.Ready, issueCount = discoverySummary.IssueCount, warningCount = discoverySummary.WarningCount, checks = discoverySummary.Checks }, [discoveryError?.Code ?? "PROJECT_NOT_FOUND"]));
        }

        var config = context.Config;
        var checks = new List<DoctorCheck>
        {
            new("project.discovery", "available", true, context.ConfigPath),
            new("project.config_version", config.ConfigVersion == 2 ? "available" : "invalid", true, context.ConfigPath, config.ConfigVersion == 2 ? null : $"需要 config_version=2，当前为 {config.ConfigVersion}")
        };
        var templateIssues = ProjectTemplatePreflight.Inspect(context);
        checks.Add(new DoctorCheck(
            "project.template_configuration",
            templateIssues.Count == 0 ? "available" : "invalid",
            true,
            context.Root,
            templateIssues.Count == 0 ? null : "检测到模板默认值；在替换名称、packageId、日志关键词和程序集名称前，项目不能部署或测试。",
            templateIssues.Count == 0 ? null : new { issues = templateIssues }));

        var build = DotNetBuildEnvironmentInspector.InspectCurrent();
        var targetingPackMessage = build.TargetFrameworkVersion == "v4.7.2"
            ? @"已找到 .NET Framework 4.7.2 Developer/Targeting Pack，可满足编译条件；仍建议安装 .NET Framework 4.8。AI 可能需要将当前 C# 工程的 TargetFrameworkVersion 修改为 v4.7.2。"
            : build.HasSupportedNetFrameworkTargetingPack ? null : "未找到 .NET Framework 4.8 或 4.7.2 Developer/Targeting Pack";
        checks.Add(new DoctorCheck(
            "build.netfx_targeting_pack",
            build.HasSupportedNetFrameworkTargetingPack ? "available" : "missing",
            config.CsprojPaths.Count > 0,
            build.TargetingPackPath,
            targetingPackMessage,
            new { frameworkVersion = build.TargetFrameworkVersion, preferredFrameworkVersion = "v4.8", supportedFrameworkVersions = DotNetBuildEnvironmentInspector.SupportedTargetFrameworkVersions }));
        checks.Add(new DoctorCheck("build.dotnet", build.HasDotNet ? "available" : "missing", config.CsprojPaths.Count > 0, build.DotNetPath, build.HasDotNet ? null : "PATH 中未找到 dotnet.exe"));
        checks.Add(new DoctorCheck("build.dotnet_sdk", build.HasSdk ? "available" : "missing", config.CsprojPaths.Count > 0, build.DotNetPath, build.HasSdk ? null : "dotnet 可用，但没有已安装的 SDK", new { sdks = build.InstalledSdks }));
        AddProjectChecks(checks, context);

        var environment = RimWorldEnvironmentResolver.Resolve(context);
        var steam = environment.Steam;
        var prohibitedLocations = ProjectLocationGuard.FindDeploymentLocations(context.Root, environment.References.RimWorldDirectory, steam.WorkshopPaths);
        checks.Add(new DoctorCheck(
            "project.deployment_location",
            prohibitedLocations.Count == 0 ? "available" : "invalid",
            true,
            context.Root,
            prohibitedLocations.Count == 0 ? null : "项目位于 RimWorld Mods 或 Steam Workshop 内容目录中；请移动到独立工作目录，deploy 会自动同步到 RimWorld/Mods。",
            prohibitedLocations.Count == 0 ? null : new { locations = prohibitedLocations }));
        checks.Add(new DoctorCheck("steam.installation", steam.SteamRoot is null ? "missing" : "available", true, steam.SteamRoot, steam.SteamRoot is null ? "未发现 Steam 安装" : null));
        checks.Add(new DoctorCheck("steam.libraries", steam.LibraryRoots.Count == 0 ? "missing" : "available", true, steam.SteamRoot, steam.LibraryRoots.Count == 0 ? "未发现 Steam Library" : null, new { libraries = steam.LibraryRoots, diagnostics = steam.Diagnostics }));
        checks.Add(new DoctorCheck("steam.rimworld_app", steam.AppManifestPath is null ? "missing" : "available", true, steam.AppManifestPath, steam.AppManifestPath is null ? "未发现结构完整的 Steam RimWorld app 294100 安装" : null));
        AddPathCheck(checks, "game.directory", steam.RimWorldDirectory, true, PathKind.Directory, new { source = steam.RimWorldSource, configured = config.RimWorldDir });
        AddPathCheck(checks, "game.executable", steam.RimWorldDirectory is null ? null : Path.Combine(steam.RimWorldDirectory, "RimWorldWin64.exe"), true, PathKind.File);
        AddPathCheck(checks, "game.managed_directory", environment.References.ManagedDirectory, config.CsprojPaths.Count > 0, PathKind.Directory);
        checks.Add(new DoctorCheck("steam.workshop", steam.WorkshopPaths.Count > 0 ? "available" : "missing", false, steam.WorkshopPaths.FirstOrDefault(), steam.WorkshopPaths.Count == 0 ? "未发现有效的 RimWorld Workshop content/294100 目录" : null, new { paths = steam.WorkshopPaths }));
        foreach (var assembly in environment.References.RequiredGameAssemblies)
            AddPathCheck(checks, $"build.game_assembly.{Path.GetFileNameWithoutExtension(assembly)}", assembly, config.CsprojPaths.Count > 0, PathKind.File);
        var harmonyMod = environment.Mods.Find("brrainz.harmony");
        checks.Add(new DoctorCheck("mods.harmony", harmonyMod is null ? "missing" : "available", config.CsprojPaths.Count > 0, harmonyMod?.Directory, harmonyMod is null ? "未找到 brrainz.harmony" : null));
        checks.Add(new DoctorCheck("build.harmony_assembly", environment.References.HarmonyDllPath is null ? "missing" : "available", config.CsprojPaths.Count > 0, environment.References.HarmonyDllPath, environment.References.HarmonyDllPath is null ? environment.HarmonyResolution?.Error ?? "未找到 0Harmony.dll" : null, environment.HarmonyResolution));
        checks.Add(new DoctorCheck("mods.metadata", environment.Mods.Diagnostics.Count == 0 ? "available" : "invalid", false, null, environment.Mods.Diagnostics.Count == 0 ? null : "部分模组 About.xml 无法解析", new { diagnostics = environment.Mods.Diagnostics }));

        AddConfigPathChecks(checks, context, environment);
        AddUnityChecks(checks, context);
        AddGitChecks(checks, context);
        AddRequiredModsCheck(checks, config, environment.Mods);
        var configDirectory = GameEnvironment.ConfigDirectory(context);
        AddPathCheck(checks, "game.config_directory", configDirectory, true, PathKind.Directory);
        AddModsConfigPathCheck(checks, context);
        AddPathCheck(checks, "game.player_log", GameEnvironment.PlayerLogPath(context), false, PathKind.File);
        AddActiveModsCheck(checks, context);
        var regexStatus = ValidateRegex(config.ProtectedFilesRegex, out var regexMessage);
        checks.Add(new DoctorCheck("deploy.protected_files_regex", regexStatus, true, null, regexMessage));
        AddPathCheck(checks, "ref.ilspy", ResolveIlspyPath(context), false, PathKind.File);

        var logSync = environment.Mods.Find("trigger.logsyncmod");
        checks.Add(new DoctorCheck("test.logsync_mod", logSync is null ? "missing" : "available", true, logSync?.Directory, logSync is null ? "未找到 trigger.logsyncmod" : null));
        var logSyncSettings = GameEnvironment.LogSyncSettingsPath(context);
        checks.Add(new DoctorCheck("test.logsync_settings", File.Exists(logSyncSettings) ? "available" : "not_applicable", false, logSyncSettings, File.Exists(logSyncSettings) ? null : "设置文件尚不存在；test run 会在事务内临时创建"));
        var autoTestAssembly = logSync is null ? null : SafeFind(logSync.Directory, "*AutoTest*.dll");
        checks.Add(new DoctorCheck("test.autotest_extension", autoTestAssembly is null ? "missing" : "available", true, autoTestAssembly, autoTestAssembly is null ? "LogSync 模组中未发现 AutoTest 程序集" : null));

        AddToolModChecks(checks, environment.Mods);
        var lockPath = configDirectory is null ? null : Path.Combine(configDirectory, "RimDevTools.lock.json");
        var lockBusy = configDirectory is not null && EnvironmentLock.IsBusy(configDirectory);
        checks.Add(new DoctorCheck("environment.lock", lockBusy ? "busy" : File.Exists(lockPath) ? "stale" : "available", false, lockPath, lockBusy ? "RimWorld 环境互斥锁正被占用" : File.Exists(lockPath) ? "互斥锁未占用，但存在遗留诊断文件" : null));
        var pendingTransactions = configDirectory is null ? [] : TransactionRecovery.PendingDirectories(configDirectory);
        checks.Add(new DoctorCheck("environment.transactions", pendingTransactions.Count > 0 ? "stale" : "available", false, null, pendingTransactions.Count > 0 ? "存在未完成事务" : null, new { directories = pendingTransactions }));

        var summary = Summarize(checks, verbose);
        var result = new
        {
            projectRoot = context.Root,
            configVersion = config.ConfigVersion,
            rimDevToolsVersion = typeof(DoctorCommand).Assembly.GetName().Version?.ToString(),
            ready = summary.Ready,
            issueCount = summary.IssueCount,
            warningCount = summary.WarningCount,
            checks = summary.Checks
        };
        return Task.FromResult(CliResult.WriteSuccess("doctor", context.Root, result));
    }

    public static DoctorSummary Summarize(IEnumerable<DoctorCheck> checks, bool verbose)
    {
        var all = checks.ToArray();
        var requiredIssues = all.Count(check => check.Required && ProblemStatuses.Contains(check.Status));
        var warnings = all.Count(check => !check.Required && ProblemStatuses.Contains(check.Status));
        return new DoctorSummary(requiredIssues == 0, requiredIssues, warnings, verbose ? all : all.Where(check => ProblemStatuses.Contains(check.Status) || AlwaysVisibleChecks.Contains(check.Id)).ToArray());
    }

    internal static string? ResolveIlspyPath(ProjectContext context)
        => !string.IsNullOrWhiteSpace(context.Config.IlspycmdPath)
            ? Path.GetFullPath(context.Config.IlspycmdPath, Path.Combine(context.Root, ".tools"))
            : Path.Combine(ToolComponentLocator.ExecutableDirectory(), "Ilspy", "ilspycmd.exe");

    private static void AddProjectChecks(List<DoctorCheck> checks, ProjectContext context)
    {
        foreach (var projectFile in context.Config.CsprojPaths)
            checks.Add(EvaluateProjectCheck(context.Root, projectFile));
    }

    private static void AddConfigPathChecks(List<DoctorCheck> checks, ProjectContext context, RimWorldEnvironmentResolution environment)
    {
        var configuredGame = context.Config.RimWorldDir;
        var gamePath = ResolveConfiguredPath(configuredGame, context.Root);
        var gameValid = gamePath is not null && File.Exists(Path.Combine(gamePath, "RimWorldWin64.exe"));
        checks.Add(new DoctorCheck(
            "config.rimworld_dir",
            gameValid ? "available" : string.IsNullOrWhiteSpace(configuredGame) || gamePath is not null ? "missing" : "invalid",
            true,
            gamePath,
            gameValid ? null : RecommendPath("rimworld_dir 未指向有效的 RimWorld 安装目录", environment.References.RimWorldDirectory),
            SuggestedPathDetails(environment.References.RimWorldDirectory)));

        foreach (var pair in context.Config.WorkshopPaths.Select((path, index) => new { path, index }))
        {
            var path = ResolveConfiguredPath(pair.path, context.Root);
            checks.Add(new DoctorCheck(
                $"config.workshop_paths.{pair.index}",
                path is not null && Directory.Exists(path) ? "available" : string.IsNullOrWhiteSpace(pair.path) || path is not null ? "missing" : "invalid",
                false,
                path,
                path is not null && Directory.Exists(path) ? null : RecommendPath("workshop_paths 配置的目录不存在", environment.Steam.WorkshopPaths.FirstOrDefault()),
                SuggestedPathDetails(environment.Steam.WorkshopPaths.FirstOrDefault())));
        }

        foreach (var pair in context.Config.SyncItems.Select((item, index) => new { item, index }))
        {
            var path = ResolveConfiguredPath(pair.item, context.Root);
            var withinProject = path is not null && PathSafety.IsWithin(path, context.Root);
            var exists = withinProject && (File.Exists(path) || Directory.Exists(path));
            checks.Add(new DoctorCheck(
                $"config.sync_items.{pair.index}",
                path is null || !withinProject ? "invalid" : exists ? "available" : "missing",
                !(pair.item ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Equals("AssetBundles", StringComparison.OrdinalIgnoreCase),
                path,
                !withinProject ? "sync_items 路径越出项目目录" : exists ? null : "sync_items 指向的文件或目录不存在"));
        }
    }

    private static string? ResolveConfiguredPath(string? path, string basePath)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.GetFullPath(path, basePath); }
        catch { return null; }
    }

    private static void AddModsConfigPathCheck(List<DoctorCheck> checks, ProjectContext context)
    {
        var validation = GameEnvironment.ValidateModsConfig(context);
        checks.Add(new DoctorCheck(
            "game.mods_config",
            validation.Ok ? "available" : validation.Code == "MODS_CONFIG_PATH_INVALID" ? "invalid" : "missing",
            true,
            validation.Path,
            validation.Message,
            validation.Details));
    }

    private static void AddActiveModsCheck(List<DoctorCheck> checks, ProjectContext context)
    {
        var validation = GameEnvironment.ValidateModsConfig(context);
        if (!validation.Ok || validation.Path is null)
        {
            checks.Add(new DoctorCheck("mods.min_active_enabled", "not_applicable", true, validation.Path, "ModsConfig.xml 不可用，无法检查实际启用模组"));
            return;
        }

        var activeMods = ModListParser.ReadActiveMods(validation.Path, out var readError);
        if (activeMods is null)
        {
            checks.Add(new DoctorCheck("mods.min_active_enabled", "invalid", true, validation.Path, readError));
            return;
        }

        var missing = context.Config.MinActiveMods
            .Where(packageId => !activeMods.Contains(packageId, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        checks.Add(new DoctorCheck(
            "mods.min_active_enabled",
            missing.Length == 0 ? "available" : "missing",
            true,
            validation.Path,
            missing.Length == 0 ? null : "ModsConfig.xml 未启用部分 min_active_mods",
            new { missingPackageIds = missing, activeMods }));
    }

    private static object? SuggestedPathDetails(string? path)
        => !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path)) ? new { suggestedPath = path } : null;

    private static string RecommendPath(string message, string? suggestedPath)
        => !string.IsNullOrWhiteSpace(suggestedPath) && (File.Exists(suggestedPath) || Directory.Exists(suggestedPath))
            ? $"{message}；建议改为: {suggestedPath}"
            : message;

    internal static DoctorCheck EvaluateProjectCheck(string projectRoot, string projectFile)
    {
        var path = Path.GetFullPath(projectFile, projectRoot);
        if (!PathSafety.IsWithin(path, projectRoot))
            return new DoctorCheck($"build.csproj.{projectFile}", "invalid", true, path, "工程路径越出项目目录");
        if (!File.Exists(path))
            return new DoctorCheck($"build.csproj.{projectFile}", "missing", true, path, "工程文件不存在");

        try
        {
            var target = XDocument.Load(path).Descendants().FirstOrDefault(element => element.Name.LocalName == "TargetFrameworkVersion")?.Value.Trim();
            var supported = DotNetBuildEnvironmentInspector.SupportedTargetFrameworkVersions.Contains(target, StringComparer.OrdinalIgnoreCase);
            var message = target == "v4.7.2" ? "工程目标为 .NET Framework 4.7.2，满足编译条件；仍建议优先使用 .NET Framework 4.8" : null;
            return new DoctorCheck(
                $"build.csproj.{projectFile}",
                supported ? "available" : "unsupported",
                true,
                path,
                supported ? message : $"工程必须目标为 .NET Framework 4.8 或 4.7.2，当前为 {target ?? "未声明"}");
        }
        catch (Exception ex)
        {
            return new DoctorCheck($"build.csproj.{projectFile}", "invalid", true, path, ex.Message);
        }
    }

    private static void AddUnityChecks(List<DoctorCheck> checks, ProjectContext context)
    {
        var configured = context.Config.ShaderBundle.UnityExePath;
        var candidates = new[] { configured, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor") }.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        var unity = candidates.FirstOrDefault(File.Exists);
        if (unity is null)
        {
            var hub = candidates.FirstOrDefault(Directory.Exists);
            unity = hub is null ? null : SafeFind(hub, "Unity.exe");
        }
        checks.Add(new DoctorCheck("deploy.unity", unity is null ? context.Config.ShaderBundle.Enabled ? "missing" : "not_applicable" : "available", context.Config.ShaderBundle.Enabled, unity ?? configured, unity is null ? context.Config.ShaderBundle.Enabled ? "Shader Bundle 已启用，但未发现 Unity Editor" : "Shader Bundle 未启用" : null));
        if (context.Config.ShaderBundle.Enabled)
        {
            AddPathCheck(checks, "deploy.unity_project", string.IsNullOrWhiteSpace(context.Config.ShaderBundle.UnityProjectPath) ? null : Path.GetFullPath(context.Config.ShaderBundle.UnityProjectPath, context.Root), true, PathKind.Directory);
            checks.Add(new DoctorCheck("deploy.unity_method", string.IsNullOrWhiteSpace(context.Config.ShaderBundle.BuildMethod) ? "missing" : "available", true, null, string.IsNullOrWhiteSpace(context.Config.ShaderBundle.BuildMethod) ? "shader_bundle.build_method 未配置" : null));
        }
    }

    private static void AddGitChecks(List<DoctorCheck> checks, ProjectContext context)
    {
        var git = DotNetBuildEnvironmentInspector.FindOnPath(OperatingSystem.IsWindows() ? "git.exe" : "git");
        var root = git is null ? null : RunText(git, context.Root, ["rev-parse", "--show-toplevel"]);
        checks.AddRange(EvaluateGitChecks(context.Root, git, root));
    }

    internal static IReadOnlyList<DoctorCheck> EvaluateGitChecks(string projectRoot, string? gitPath, string? repositoryRoot)
    {
        var result = new List<DoctorCheck>
        {
            new("git.command", gitPath is null ? "missing" : "available", false, gitPath, gitPath is null ? "PATH 中未找到 Git" : null)
        };
        if (gitPath is null)
        {
            result.Add(new DoctorCheck("git.worktree", "not_applicable", false, projectRoot));
            return result;
        }
        var sameRoot = repositoryRoot is not null && Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        result.Add(new DoctorCheck("git.worktree", repositoryRoot is null ? "missing" : sameRoot ? "available" : "invalid", false, repositoryRoot, repositoryRoot is null ? "项目不在 Git worktree 中" : sameRoot ? null : "Git 仓库根目录与项目根目录不一致", new { projectRoot, repositoryRoot }));
        return result;
    }

    private static void AddRequiredModsCheck(List<DoctorCheck> checks, ProjectConfig config, ModInstallationIndex mods)
    {
        var missingBootstrap = ModListParser.RequiredStartupMods
            .Where(packageId => !config.MinActiveMods.Contains(packageId, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        checks.Add(new DoctorCheck(
            "mods.min_active_required",
            missingBootstrap.Length == 0 ? "available" : "missing",
            true,
            null,
            missingBootstrap.Length == 0 ? null : "min_active_mods 未包含测试启动必须模组",
            new { requiredPackageIds = ModListParser.RequiredStartupMods, missingPackageIds = missingBootstrap }));

        var missing = config.MinActiveMods.Where(packageId => mods.Find(packageId) is null).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        checks.Add(new DoctorCheck("mods.required", missing.Length == 0 ? "available" : "missing", true, null, missing.Length == 0 ? null : "部分 min_active_mods 无法解析", new { missingPackageIds = missing }));
    }

    private static void AddToolModChecks(List<DoctorCheck> checks, ModInstallationIndex mods)
    {
        var dataRoot = ToolComponentLocator.ToolDataPath();
        var manifestPath = Path.Combine(dataRoot, "tool-mods.manifest.json");
        if (!File.Exists(manifestPath))
        {
            checks.Add(new DoctorCheck("tools.mod_components", "missing", false, manifestPath, "未发现随包工具 Mod 清单"));
            return;
        }
        try
        {
            var manifest = ToolModManifestReader.Load(manifestPath);
            checks.Add(new DoctorCheck("tools.mod_components", "available", false, manifestPath, Details: new { count = manifest.Components.Count }));
            foreach (var component in manifest.Components)
            {
                var source = Path.Combine(dataRoot, component.Folder);
                var sourceHash = Directory.Exists(source) ? DirectoryHasher.Compute(source) : null;
                var sourceAvailable = sourceHash?.Equals(component.ContentHash, StringComparison.OrdinalIgnoreCase) == true;
                checks.Add(new DoctorCheck(
                    $"tools.component.{component.PackageId.ToLowerInvariant()}",
                    sourceAvailable ? "available" : Directory.Exists(source) ? "invalid" : "missing",
                    false,
                    source,
                    sourceAvailable ? null : "随包工具 Mod 缺失或内容哈希不匹配",
                    new { component.Version, expectedHash = component.ContentHash, actualHash = sourceHash }));

                var installed = mods.Find(component.PackageId);
                var installedVersion = installed is null ? null : ToolModVersion.Read(installed.Directory, component.PrimaryAssembly);
                var installedStatus = installed is null
                    ? "missing"
                    : installedVersion is null
                        ? "invalid"
                        : ToolModVersion.Compare(installedVersion, component.Version) < 0 ? "stale" : "available";
                checks.Add(new DoctorCheck(
                    $"mods.tool.{component.PackageId.ToLowerInvariant()}",
                    installedStatus,
                    false,
                    installed?.Directory,
                    installed is null ? "工具 Mod 尚未安装；可运行 mods install-tools" : installedStatus == "stale" ? "已安装版本低于随包版本" : null,
                    new { packagedVersion = component.Version, installedVersion }));
            }
        }
        catch (Exception ex)
        {
            checks.Add(new DoctorCheck("tools.mod_components", "invalid", false, manifestPath, ex.Message));
        }
    }

    private static string ValidateRegex(string? pattern, out string? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(pattern)) return "available";
        try { _ = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); return "available"; }
        catch (Exception ex) { message = ex.Message; return "invalid"; }
    }

    private static void AddPathCheck(List<DoctorCheck> checks, string id, string? path, bool required, PathKind kind, object? details = null)
    {
        var full = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        var available = full is not null && (kind == PathKind.File ? File.Exists(full) : Directory.Exists(full));
        checks.Add(new DoctorCheck(id, available ? "available" : required ? "missing" : "not_applicable", required, full, available ? null : required ? "路径不存在" : "未配置或未发现", details));
    }

    private static string? RunText(string fileName, string workingDirectory, IEnumerable<string> arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName) { WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            return process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch { return null; }
    }

    private static string? SafeFind(string root, string pattern)
    {
        try { return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories).FirstOrDefault(); }
        catch { return null; }
    }

    private enum PathKind { File, Directory }
}

internal static class GameEnvironment
{
    public static string DefaultConfigDirectory()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Ludeon Studios", "RimWorld by Ludeon Studios", "Config");
    }

    public static string ConfigDirectory() => DefaultConfigDirectory();
    public static string? ModsConfigPath(ProjectContext context)
    {
        try { return context.Config.ResolveModsConfigPath(context.Root); }
        catch { return null; }
    }

    public static string? ConfigDirectory(ProjectContext context)
    {
        var path = ModsConfigPath(context);
        return path is null ? null : Path.GetDirectoryName(path);
    }

    public static string? PlayerLogPath(ProjectContext context)
    {
        var configDirectory = ConfigDirectory(context);
        return configDirectory is null ? null : Path.Combine(Directory.GetParent(configDirectory)?.FullName ?? configDirectory, "Player.log");
    }

    public static string? LogSyncSettingsPath(ProjectContext context)
    {
        var configDirectory = ConfigDirectory(context);
        return configDirectory is null ? null : Path.Combine(configDirectory, "Mod_LogSyncMod_LogSyncMod.xml");
    }

    public static ModsConfigValidation ValidateModsConfig(ProjectContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Config.ModsConfigPath))
            return new(false, "MODS_CONFIG_PATH_MISSING", null, "mod_config.jsonc 缺少 mods_config_path", new { suggestedPath = CanonicalModsConfigPath() });

        string path;
        try { path = context.Config.ResolveModsConfigPath(context.Root)!; }
        catch (Exception ex)
        {
            return new(false, "MODS_CONFIG_PATH_INVALID", null, $"mods_config_path 无效: {ex.Message}", new { configuredPath = context.Config.ModsConfigPath, suggestedPath = ExistingCanonicalModsConfigPath() });
        }

        if (File.Exists(path)) return new(true, null, path, null);
        return new(false, "MODS_CONFIG_NOT_FOUND", path, RecommendModsConfigPath(path), new { configuredPath = context.Config.ModsConfigPath, suggestedPath = ExistingCanonicalModsConfigPath() });
    }

    public static object? ModsConfigRecommendation(ProjectContext context) => ValidateModsConfig(context).Details;

    private static string CanonicalModsConfigPath() => Path.Combine(DefaultConfigDirectory(), "ModsConfig.xml");
    private static string? ExistingCanonicalModsConfigPath()
    {
        var path = CanonicalModsConfigPath();
        return File.Exists(path) ? path : null;
    }

    private static string RecommendModsConfigPath(string configuredPath)
    {
        var suggestedPath = ExistingCanonicalModsConfigPath();
        return suggestedPath is null || suggestedPath.Equals(configuredPath, StringComparison.OrdinalIgnoreCase)
            ? "mods_config_path 指向的 ModsConfig.xml 不存在"
            : $"mods_config_path 指向的 ModsConfig.xml 不存在；建议改为: {suggestedPath}";
    }
}

internal sealed record ModsConfigValidation(bool Ok, string? Code, string? Path, string? Message, object? Details = null);
