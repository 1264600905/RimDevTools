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
        "missing", "invalid", "stale", "busy", "unsupported", "warning"
    };
    private static readonly HashSet<string> AlwaysVisibleChecks = new(StringComparer.OrdinalIgnoreCase)
    {
        "config.game_version",
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
        var supportedGameVersion = RimWorldGameLayout.TryGet(config.GameVersion, out _);
        var modernGameVersion = supportedGameVersion && RimWorldGameLayout.TryGet(config.GameVersion, out var configuredLayout) && configuredLayout.UsesModernRuntime;
        checks.Add(new DoctorCheck(
            "config.game_version",
            supportedGameVersion ? "available" : "unsupported",
            true,
            context.ConfigPath,
            supportedGameVersion ? null : $"game_version 必须明确为 {RimWorldGameLayout.SupportedVersionDescription}，当前为 {config.GameVersion ?? "未配置"}",
            new { configured = config.GameVersion, supportedLayouts = RimWorldGameLayout.SupportedLayouts }));
        var templateIssues = ProjectTemplatePreflight.Inspect(context);
        checks.Add(new DoctorCheck(
            "project.template_configuration",
            templateIssues.Count == 0 ? "available" : "invalid",
            true,
            context.Root,
            templateIssues.Count == 0 ? null : "检测到模板默认值；在替换名称、packageId、日志关键词和程序集名称前，项目不能部署或测试。",
            templateIssues.Count == 0 ? null : new { issues = templateIssues }));
        AddAboutChecks(checks, context.Root);

        var build = DotNetBuildEnvironmentInspector.InspectCurrent();
        var hasCsproj = config.CsprojPaths.Count > 0;
        var requiresNetFrameworkPack = hasCsproj && config.GameVersion == "1.6";
        var hasVersionSdk = modernGameVersion
            ? build.InstalledSdks.Any(sdk => sdk.StartsWith("10.", StringComparison.OrdinalIgnoreCase))
            : build.HasSdk;
        var targetingPackMessage = build.TargetFrameworkVersion == "v4.7.2"
            ? @"已找到 .NET Framework 4.7.2 Developer/Targeting Pack，可满足编译条件；仍建议安装 .NET Framework 4.8。AI 可能需要将当前 C# 工程的 TargetFrameworkVersion 修改为 v4.7.2。"
            : build.HasSupportedNetFrameworkTargetingPack ? null : "未找到 .NET Framework 4.8 或 4.7.2 Developer/Targeting Pack";
        if (!modernGameVersion || verbose)
        {
            checks.Add(new DoctorCheck(
                "build.netfx_targeting_pack",
                !requiresNetFrameworkPack ? "not_applicable" : build.HasSupportedNetFrameworkTargetingPack ? "available" : "missing",
                requiresNetFrameworkPack,
                build.TargetingPackPath,
                requiresNetFrameworkPack ? targetingPackMessage : modernGameVersion && hasCsproj ? ".NET Framework targeting pack 不用于 RimWorld 1.7+ 布局" : null,
                new { frameworkVersion = build.TargetFrameworkVersion, preferredFrameworkVersion = "v4.8", supportedFrameworkVersions = DotNetBuildEnvironmentInspector.SupportedTargetFrameworkVersions }));
        }
        checks.Add(new DoctorCheck("build.dotnet", !hasCsproj ? "not_applicable" : build.HasDotNet ? "available" : "missing", hasCsproj, build.DotNetPath, hasCsproj && !build.HasDotNet ? "PATH 中未找到 dotnet.exe" : null));
        checks.Add(new DoctorCheck("build.dotnet_sdk", !hasCsproj ? "not_applicable" : hasVersionSdk ? "available" : "missing", hasCsproj, build.DotNetPath, !hasCsproj || hasVersionSdk ? null : modernGameVersion ? "RimWorld 1.7+ 布局工程需要 .NET 10 SDK" : "dotnet 可用，但没有已安装的 SDK", new { sdks = build.InstalledSdks, requiredMajor = modernGameVersion ? 10 : (int?)null }));
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
        checks.Add(new DoctorCheck("steam.rimworld_app", steam.AppManifestPath is null ? "missing" : "available", true, steam.AppManifestPath, steam.AppManifestPath is null ? $"未发现与 RimWorld {config.GameVersion ?? "未配置"} 匹配的完整 Steam app 294100 安装" : null, new { expectedGameVersion = config.GameVersion, diagnostics = steam.Diagnostics }));
        var configuredGameDirectory = ResolveConfiguredPath(config.RimWorldDir, context.Root);
        var gameDirectoryStatus = steam.RimWorldDirectory is not null
            ? "available"
            : configuredGameDirectory is not null && Directory.Exists(configuredGameDirectory) ? "invalid" : "missing";
        var gameDirectoryMessage = steam.RimWorldDirectory is not null
            ? null
            : RimWorldGameLayout.ValidateGameDirectory(configuredGameDirectory, config.GameVersion) ?? "未解析到与配置版本匹配的 RimWorld 安装目录";
        checks.Add(new DoctorCheck("game.directory", gameDirectoryStatus, true, steam.RimWorldDirectory ?? configuredGameDirectory, gameDirectoryMessage, new { source = steam.RimWorldSource, configured = config.RimWorldDir, expectedGameVersion = config.GameVersion, detectedGameVersion = configuredGameDirectory is null ? null : RimWorldGameLayout.DetectVersion(configuredGameDirectory) }));
        AddPathCheck(checks, "game.executable", environment.References.ExecutablePath, true, PathKind.File);
        AddPathCheck(checks, "game.assembly_directory", environment.References.AssemblyDirectory, hasCsproj, PathKind.Directory, new { gameVersion = config.GameVersion });
        checks.Add(new DoctorCheck("steam.workshop", steam.WorkshopPaths.Count > 0 ? "available" : "missing", false, steam.WorkshopPaths.FirstOrDefault(), steam.WorkshopPaths.Count == 0 ? "未发现有效的 RimWorld Workshop content/294100 目录" : null, new { paths = steam.WorkshopPaths }));
        foreach (var assembly in environment.References.RequiredGameAssemblies)
            AddPathCheck(checks, $"build.game_assembly.{Path.GetFileNameWithoutExtension(assembly)}", assembly, config.CsprojPaths.Count > 0, PathKind.File);
        var harmonyMod = environment.Mods.Find("brrainz.harmony");
        checks.Add(new DoctorCheck("mods.harmony", harmonyMod is null ? "missing" : "available", config.CsprojPaths.Count > 0, harmonyMod?.Directory, harmonyMod is null ? "未找到 brrainz.harmony" : null));
        checks.Add(new DoctorCheck("build.harmony_assembly", environment.References.HarmonyDllPath is null ? "missing" : "available", config.CsprojPaths.Count > 0, environment.References.HarmonyDllPath, environment.References.HarmonyDllPath is null ? environment.HarmonyResolution?.Error ?? "未找到 0Harmony.dll" : null, environment.HarmonyResolution));
        var harmonyVersion = HarmonyVersionInspector.Inspect(environment.References.HarmonyDllPath);
        checks.Add(new DoctorCheck(
            "mods.harmony_version",
            harmonyVersion.Status,
            false,
            harmonyVersion.Path,
            harmonyVersion.Message,
            new { installedVersion = harmonyVersion.InstalledVersion?.ToString(), requiredVersion = harmonyVersion.RequiredVersion.ToString() }));
        checks.Add(new DoctorCheck("mods.metadata", environment.Mods.Diagnostics.Count == 0 ? "available" : "invalid", false, null, environment.Mods.Diagnostics.Count == 0 ? null : "部分模组 About.xml 无法解析", new { diagnostics = environment.Mods.Diagnostics }));

        AddConfigPathChecks(checks, context, environment);
        AddUnityChecks(checks, context, config.GameVersion);
        AddGitChecks(checks, context);
        AddRequiredModsCheck(checks, config, environment.Mods);
        var configDirectory = GameEnvironment.ConfigDirectory(context);
        AddPathCheck(checks, "game.config_directory", configDirectory, true, PathKind.Directory);
        AddModsConfigPathCheck(checks, context);
        AddPathCheck(checks, "game.player_log", GameEnvironment.PlayerLogPath(context), false, PathKind.File);
        AddActiveModsCheck(checks, context);
        var regexStatus = ValidateRegex(config.ProtectedFilesRegex, out var regexMessage);
        checks.Add(new DoctorCheck("deploy.protected_files_regex", regexStatus, true, null, regexMessage));
        AddPathCheck(checks, "mod.ilspy", ResolveIlspyPath(context), false, PathKind.File);

        var testHost = TestHostResolver.Resolve(environment, config.GameVersion);
        checks.Add(new DoctorCheck("test.logsync_mod", testHost.LogSyncMod is null ? "missing" : testHost.LogSyncAssemblyPath is null ? "unsupported" : "available", false, testHost.LogSyncAssemblyPath ?? testHost.LogSyncMod?.Directory, testHost.LogSyncMessage, new { gameVersion = config.GameVersion }));
        var logSyncSettings = GameEnvironment.LogSyncSettingsPath(context);
        checks.Add(new DoctorCheck("test.logsync_settings", File.Exists(logSyncSettings) ? "available" : "not_applicable", false, logSyncSettings, File.Exists(logSyncSettings) ? null : "设置文件尚不存在；test run 会在事务内临时创建"));
        checks.Add(new DoctorCheck("test.autotest_extension", testHost.AutoTestAssemblyPath is null ? "missing" : "available", false, testHost.AutoTestAssemblyPath, testHost.AutoTestMessage ?? "AutoTestRunner 仅在显式运行 auto/XML 测试时需要；项目不要求 AutoTest XML", new { gameVersion = config.GameVersion }));

        AddToolModChecks(checks, environment.Mods, config.GameVersion);
        var lockPath = configDirectory is null ? null : Path.Combine(configDirectory, "RimDevTools.lock.json");
        var lockBusy = configDirectory is not null && EnvironmentLock.IsBusy(configDirectory);
        checks.Add(new DoctorCheck("environment.lock", lockBusy ? "busy" : File.Exists(lockPath) ? "stale" : "available", false, lockPath, lockBusy ? "RimWorld 环境互斥锁正被占用" : File.Exists(lockPath) ? "互斥锁未占用，但存在遗留诊断文件" : null));
        var pendingTransactions = configDirectory is null ? [] : TransactionRecovery.PendingDirectories(configDirectory);
        checks.Add(new DoctorCheck("environment.transactions", pendingTransactions.Count > 0 ? "stale" : "available", false, null, pendingTransactions.Count > 0 ? "存在未完成事务；请在 RimWorld 完全退出后执行 RimDevTools.exe test cleanup" : null, new { directories = pendingTransactions, cleanupCommand = "RimDevTools.exe test cleanup" }));

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

    internal static void AddAboutChecks(List<DoctorCheck> checks, string projectRoot)
    {
        var aboutXml = Path.Combine(projectRoot, "About", "About.xml");
        checks.Add(new DoctorCheck(
            "project.about_xml",
            File.Exists(aboutXml) ? "available" : "warning",
            false,
            aboutXml,
            File.Exists(aboutXml) ? null : "缺少 About/About.xml；请补充模组元数据。"));

        var preview = Path.Combine(projectRoot, "About", "Preview.png");
        if (!File.Exists(preview))
        {
            checks.Add(new DoctorCheck("project.preview_image", "warning", false, preview, "缺少 About/Preview.png；请补充模组预览图。", new { maxBytes = 1_048_576 }));
            return;
        }

        try
        {
            var size = new FileInfo(preview).Length;
            checks.Add(new DoctorCheck(
                "project.preview_image",
                size < 1_048_576 ? "available" : "warning",
                false,
                preview,
                size < 1_048_576 ? null : "About/Preview.png 必须小于 1 MiB（1,048,576 字节）。",
                new { sizeBytes = size, maxBytes = 1_048_576 }));
        }
        catch (Exception ex)
        {
            checks.Add(new DoctorCheck("project.preview_image", "warning", false, preview, $"无法检查 About/Preview.png: {ex.Message}", new { maxBytes = 1_048_576 }));
        }
    }

    private static void AddProjectChecks(List<DoctorCheck> checks, ProjectContext context)
    {
        foreach (var projectFile in context.Config.CsprojPaths)
            checks.Add(EvaluateProjectCheck(context.Root, projectFile, context.Config.GameVersion));
    }

    private static void AddConfigPathChecks(List<DoctorCheck> checks, ProjectContext context, RimWorldEnvironmentResolution environment)
    {
        var configuredGame = context.Config.RimWorldDir;
        var gamePath = ResolveConfiguredPath(configuredGame, context.Root);
        var gameValidation = RimWorldGameLayout.ValidateGameDirectory(gamePath, context.Config.GameVersion);
        var gameValid = gameValidation is null;
        var gameStatus = gameValid ? "available" : gamePath is null || !Directory.Exists(gamePath) ? "missing" : "invalid";
        checks.Add(new DoctorCheck(
            "config.rimworld_dir",
            gameStatus,
            true,
            gamePath,
            gameValid ? null : RecommendPath($"rimworld_dir 与 game_version={context.Config.GameVersion ?? "未配置"} 不匹配: {gameValidation}", environment.References.RimWorldDirectory),
            new { expectedGameVersion = context.Config.GameVersion, detectedGameVersion = gamePath is null ? null : RimWorldGameLayout.DetectVersion(gamePath), suggestedPath = environment.References.RimWorldDirectory }));

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
        var resolution = ModListParser.Resolve(context, null, out var error);
        if (resolution is null)
        {
            checks.Add(new DoctorCheck(
                "mods.min_active_enabled",
                "invalid",
                false,
                null,
                error?.Message ?? "无法解析测试启用模组列表",
                error?.Details));
            return;
        }

        checks.Add(new DoctorCheck(
            "mods.min_active_enabled",
            "available",
            false,
            null,
            "测试会用此列表替换 ModsConfig.xml 的 activeMods",
            new { activeMods = resolution.Mods, additions = resolution.Additions }));
        var dependencyWarnings = resolution.DependencyWarnings;
        checks.Add(new DoctorCheck(
            "mods.min_active_dependencies",
            dependencyWarnings.Count == 0 ? "available" : "warning",
            false,
            null,
            dependencyWarnings.Count == 0
                ? "min_active_mods 中启用模组的必要上级依赖均已启用"
                : string.Join(" ", dependencyWarnings.Select(ModDependencyInspector.FormatWarning)),
            new { warnings = dependencyWarnings }));
    }

    private static object? SuggestedPathDetails(string? path)
        => !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path)) ? new { suggestedPath = path } : null;

    private static string RecommendPath(string message, string? suggestedPath)
        => !string.IsNullOrWhiteSpace(suggestedPath) && (File.Exists(suggestedPath) || Directory.Exists(suggestedPath))
            ? $"{message}；建议改为: {suggestedPath}"
            : message;

    internal static DoctorCheck EvaluateProjectCheck(string projectRoot, string projectFile, string? gameVersion = "1.6")
    {
        var path = Path.GetFullPath(projectFile, projectRoot);
        if (!PathSafety.IsWithin(path, projectRoot))
            return new DoctorCheck($"build.csproj.{projectFile}", "invalid", true, path, "工程路径越出项目目录");
        if (!File.Exists(path))
            return new DoctorCheck($"build.csproj.{projectFile}", "missing", true, path, "工程文件不存在");

        try
        {
            var targets = XDocument.Load(path).Descendants()
                .Where(element => element.Name.LocalName is "TargetFrameworkVersion" or "TargetFramework" or "TargetFrameworks")
                .SelectMany(element => element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var isSupportedVersion = RimWorldGameLayout.TryGet(gameVersion, out var layout);
            var supported = isSupportedVersion && (layout.UsesNetFramework
                ? targets.Any(IsSupportedNetFrameworkTarget)
                : targets.Any(IsSupportedNet10Target));
            var hasNet472 = targets.Any(target => target.Equals("v4.7.2", StringComparison.OrdinalIgnoreCase) || target.Equals("net472", StringComparison.OrdinalIgnoreCase));
            var targetDescription = targets.Length == 0 ? "未声明" : string.Join(", ", targets);
            var message = gameVersion == "1.6" && hasNet472 ? "工程目标为 .NET Framework 4.7.2，满足编译条件；仍建议优先使用 .NET Framework 4.8" : null;
            var expected = isSupportedVersion && layout.UsesNetFramework
                ? ".NET Framework 4.8、4.7.2、net48 或 net472"
                : isSupportedVersion ? ".NET 10 (net10.0；适用于 1.7+ 布局)" : "明确支持的 game_version 与对应目标框架";
            return new DoctorCheck(
                $"build.csproj.{projectFile}",
                supported ? "available" : "unsupported",
                true,
                path,
                supported ? message : $"RimWorld {gameVersion ?? "未知版本"} 工程需要 {expected}，当前为 {targetDescription}");
        }
        catch (Exception ex)
        {
            return new DoctorCheck($"build.csproj.{projectFile}", "invalid", true, path, ex.Message);
        }
    }

    private static bool IsSupportedNetFrameworkTarget(string target)
        => DotNetBuildEnvironmentInspector.SupportedTargetFrameworkVersions.Contains(target, StringComparer.OrdinalIgnoreCase)
            || target.Equals("net48", StringComparison.OrdinalIgnoreCase)
            || target.Equals("net472", StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportedNet10Target(string target)
        => target.Equals("net10.0", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("net10.0-", StringComparison.OrdinalIgnoreCase);

    private static void AddUnityChecks(List<DoctorCheck> checks, ProjectContext context, string? gameVersion)
    {
        if (context.Config.ShaderBundle.Enabled && gameVersion != "1.6")
        {
            checks.Add(new DoctorCheck("deploy.shader_bundle_version", "unsupported", true, null, "RimDevTools 的 Unity Shader Bundle 构建仅适用于 RimWorld 1.6；1.7 应使用原生 GLSL/ShaderDef 流程。", new { gameVersion }));
            return;
        }

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
            false,
            null,
            missingBootstrap.Length == 0 ? null : "min_active_mods 未包含测试启动必须模组",
            new { requiredPackageIds = ModListParser.RequiredStartupMods, missingPackageIds = missingBootstrap }));

        var missing = config.MinActiveMods.Where(packageId => mods.Find(packageId) is null).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        checks.Add(new DoctorCheck("mods.required", missing.Length == 0 ? "available" : "missing", false, null, missing.Length == 0 ? null : "部分测试用 min_active_mods 无法解析；这不会阻止普通构建或部署", new { missingPackageIds = missing }));
    }

    private static void AddToolModChecks(List<DoctorCheck> checks, ModInstallationIndex mods, string? gameVersion)
    {
        var dataRoot = ToolComponentLocator.ToolDataPath();
        var manifestPath = Path.Combine(dataRoot, "tool-mods.manifest.json");
        if (!File.Exists(manifestPath))
        {
            checks.Add(new DoctorCheck("tools.mod_components", "missing", false, manifestPath, "未发现可选测试工具 Mod 清单"));
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
                var sourceAssembly = Directory.Exists(source) && RimWorldGameLayout.TryGet(gameVersion, out _)
                    ? ModAssemblyResolver.Resolve(source, gameVersion, Path.GetFileName(component.PrimaryAssembly)).AssemblyPath
                    : null;
                var sourceStatus = !RimWorldGameLayout.TryGet(gameVersion, out _) ? "not_applicable"
                    : !Directory.Exists(source) ? "missing"
                    : sourceAssembly is null ? "unsupported"
                    : sourceAvailable ? "available" : "invalid";
                checks.Add(new DoctorCheck(
                    $"tools.component.{component.PackageId.ToLowerInvariant()}",
                    sourceStatus,
                    false,
                    source,
                    sourceStatus switch
                    {
                        "missing" => "随包测试工具 Mod 文件缺失",
                        "unsupported" => $"随包工具 Mod 没有 RimWorld {gameVersion} 对应程序集",
                        "invalid" => "随包工具 Mod 内容哈希不匹配",
                        _ => null
                    },
                    new { component.Version, gameVersion, expectedHash = component.ContentHash, actualHash = sourceHash }));

                var installed = mods.Find(component.PackageId);
                var installedAssembly = installed is null || !RimWorldGameLayout.TryGet(gameVersion, out _)
                    ? null
                    : ModAssemblyResolver.Resolve(installed.Directory, gameVersion, Path.GetFileName(component.PrimaryAssembly)).AssemblyPath;
                var installedVersion = installedAssembly is null ? null : ToolModVersion.Read(installed!.Directory, component.PrimaryAssembly);
                var installedStatus = installed is null
                    ? "missing"
                    : installedAssembly is null ? "unsupported"
                    : installedVersion is null
                        ? "invalid"
                        : ToolModVersion.Compare(installedVersion, component.Version) < 0 ? "stale" : "available";
                checks.Add(new DoctorCheck(
                    $"mods.tool.{component.PackageId.ToLowerInvariant()}",
                    installedStatus,
                    false,
                    installedAssembly ?? installed?.Directory,
                    installed is null ? "可选测试工具 Mod 尚未安装" : installedAssembly is null ? $"已安装工具 Mod 不含 RimWorld {gameVersion} 对应程序集" : installedStatus == "stale" ? "已安装版本低于随包版本" : null,
                    new { gameVersion, packagedVersion = component.Version, installedVersion }));
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
