using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

namespace RimDevTools;

public static class TestCommand
{
    public static async Task<int> CleanupAsync(string[] args, CancellationToken cancellationToken = default)
    {
        const string command = "test cleanup";
        var context = CliApplication.RequireProject(command, out var code);
        if (context is null) return code;

        var modsConfigValidation = GameEnvironment.ValidateModsConfig(context);
        if (!modsConfigValidation.Ok)
            return CliResult.WriteFailure(command, ExitCodes.MissingDependency, modsConfigValidation.Code!, modsConfigValidation.Message!, context.Root, modsConfigValidation.Details);

        var dryRun = CliArgs.Has(args, "--dry-run");
        var statusOnly = CliArgs.Has(args, "--status");
        var configDirectory = GameEnvironment.ConfigDirectory(context)!;
        if (statusOnly)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transactionStatus = await TransactionRecovery.CleanupAsync(configDirectory, modsConfigValidation.Path!, dryRun: true);
            IReadOnlyList<DeploymentArtifactAction> deploymentArtifacts = [];
            var environment = RimWorldEnvironmentResolver.Resolve(context);
            if (environment.References.RimWorldDirectory is { } statusRimWorldDirectory && !string.IsNullOrWhiteSpace(context.Config.ModName))
            {
                var modsRoot = Path.GetFullPath(Path.Combine(statusRimWorldDirectory, "Mods"));
                var target = Path.GetFullPath(Path.Combine(modsRoot, context.Config.ModName));
                if (PathSafety.IsWithin(target, modsRoot))
                    deploymentArtifacts = DeploymentArtifactRecovery.Plan(modsRoot, target, context.Config.CurrentPackageId(context.Root));
            }

            var gameRunning = ProcessUtil.IsRimWorldRunning();
            var environmentBusy = EnvironmentLock.IsBusy(configDirectory);
            var lockPath = Path.Combine(configDirectory, "RimDevTools.lock.json");
            var found = transactionStatus.Found || deploymentArtifacts.Count > 0;
            return CliResult.WriteSuccess(command, context.Root, new
            {
                status = true,
                gameRunning,
                environmentBusy,
                lockPath,
                lockInfo = File.Exists(lockPath) ? File.ReadAllText(lockPath) : null,
                found,
                recoverable = transactionStatus.Ok && deploymentArtifacts.All(item => item.Action != "retain"),
                transactions = transactionStatus.Items.Select(item => new
                {
                    runId = Path.GetFileName(item.Directory),
                    directory = item.Directory,
                    item.Status,
                    item.Message
                }).ToArray(),
                deploymentArtifacts,
                nextCommand = found
                    ? environmentBusy ? "请等待当前 RimDevTools 操作释放环境锁后重试"
                    : gameRunning ? "请等待 RimWorld 完全退出后执行 RimDevTools.exe test cleanup --dry-run"
                    : "RimDevTools.exe test cleanup --dry-run"
                    : null
            });
        }

        var runId = Guid.NewGuid().ToString("N");
        EnvironmentLock? gate = null;
        try
        {
            if (!dryRun)
            {
                gate = EnvironmentLock.TryAcquire(configDirectory, command, context.Root, runId, out var busyInfo);
                if (gate is null)
                    return CliResult.WriteFailure(command, ExitCodes.Busy, "ENVIRONMENT_BUSY", "RimWorld 环境正被占用", context.Root, new { busyInfo });

                if (ProcessUtil.IsRimWorldRunning())
                    return CliResult.WriteFailure(command, ExitCodes.Busy, "GAME_RUNNING_CLEANUP_REFUSED", "RimWorld 仍在运行，拒绝清理事务；请等待游戏完全退出后重试", context.Root);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var summary = await TransactionRecovery.CleanupAsync(configDirectory, modsConfigValidation.Path!, dryRun);
            IReadOnlyList<DeploymentArtifactAction> artifactResults = [];
            var environment = RimWorldEnvironmentResolver.Resolve(context);
            if (environment.References.RimWorldDirectory is { } rimWorldDirectory && !string.IsNullOrWhiteSpace(context.Config.ModName))
            {
                var modsRoot = Path.GetFullPath(Path.Combine(rimWorldDirectory, "Mods"));
                var target = Path.GetFullPath(Path.Combine(modsRoot, context.Config.ModName));
                if (PathSafety.IsWithin(target, modsRoot))
                {
                    var artifactPlan = DeploymentArtifactRecovery.Plan(modsRoot, target, context.Config.CurrentPackageId(context.Root));
                    artifactResults = dryRun ? artifactPlan : DeploymentArtifactRecovery.Execute(modsRoot, target, artifactPlan);
                }
            }

            var allOk = summary.Ok && artifactResults.All(x => x.Action != "failed");
            var result = new { dryRun, runId, found = summary.Found || artifactResults.Count > 0, ok = allOk, transactions = summary.Items, deploymentArtifacts = artifactResults };
            return allOk
                ? CliResult.WriteSuccess(command, context.Root, result)
                : CliResult.WriteFailure(command, ExitCodes.TaskFailed, "TRANSACTION_CLEANUP_INCOMPLETE", "部分事务无法安全清理，已保留现场", context.Root, result);
        }
        finally
        {
            gate?.Dispose();
        }
    }

    public static async Task<int> SettingsAsync(string[] args, CancellationToken cancellationToken = default)
    {
        const string command = "test settings";
        var context = CliApplication.RequireProject(command, out var code);
        if (context is null) return code;
        var templateError = ProjectTemplatePreflight.Validate(context);
        if (templateError is not null)
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, templateError.Code, templateError.Message, context.Root, templateError.Details);
        var modsConfigValidation = GameEnvironment.ValidateModsConfig(context);
        if (!modsConfigValidation.Ok)
            return CliResult.WriteFailure(command, ExitCodes.MissingDependency, modsConfigValidation.Code!, modsConfigValidation.Message!, context.Root, modsConfigValidation.Details);
        var configDirectory = GameEnvironment.ConfigDirectory(context)!;

        var runId = Guid.NewGuid().ToString("N");
        using var gate = EnvironmentLock.TryAcquire(configDirectory, command, context.Root, runId, out var busyInfo);
        if (gate is null)
            return CliResult.WriteFailure(command, ExitCodes.Busy, "ENVIRONMENT_BUSY", "RimWorld 环境正被占用", context.Root, new { busyInfo });

        if (!await ProcessUtil.WaitForRimWorldExitAsync(context.Config.Timeouts.ExistingGameExit, context.Config.Timeouts.PostExitDelay, cancellationToken))
            return CliResult.WriteFailure(command, ExitCodes.Timeout, "GAME_EXIT_TIMEOUT", "等待已有 RimWorld 退出超时", context.Root);

        var recovery = await TransactionRecovery.RecoverAsync(configDirectory);
        if (!recovery.Ok)
            return CliResult.WriteFailure(command, ExitCodes.TaskFailed, recovery.Error!.Code, recovery.Error.Message, context.Root, recovery.Error.Details);

        try
        {
            var changed = await SettingsEditor.UpdateAsync(context, "quick", muteAudio: false);
            return CliResult.WriteSuccess(command, context.Root, new
            {
                runId,
                settingsPath = GameEnvironment.LogSyncSettingsPath(context),
                changed,
                recoveredTransaction = recovery.Found
            });
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return CliResult.WriteFailure(command, ExitCodes.TaskFailed, "SETTINGS_UPDATE_FAILED", ex.Message, context.Root);
        }
    }

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        const string command = "test run";
        var context = CliApplication.RequireProject(command, out var code);
        if (context is null) return code;
        var templateError = ProjectTemplatePreflight.Validate(context);
        if (templateError is not null)
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, templateError.Code, templateError.Message, context.Root, templateError.Details);
        var modsConfigValidation = GameEnvironment.ValidateModsConfig(context);
        if (!modsConfigValidation.Ok)
            return CliResult.WriteFailure(command, ExitCodes.MissingDependency, modsConfigValidation.Code!, modsConfigValidation.Message!, context.Root, modsConfigValidation.Details);
        var configDirectory = GameEnvironment.ConfigDirectory(context)!;
        var modsConfigPath = modsConfigValidation.Path!;
        var logSyncSettingsPath = GameEnvironment.LogSyncSettingsPath(context)!;
        var playerLogPath = GameEnvironment.PlayerLogPath(context)!;

        var mode = CliArgs.Value(args, "--mode")?.ToLowerInvariant() ?? "quick";
        if (mode is not ("quick" or "manual" or "auto" or "xml"))
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "MODE_INVALID", $"不支持的测试模式: {mode}", context.Root);
        if (!AutoTestSelectionParser.TryParse(args, mode, out var selection, out var selectionError))
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "INVALID_ARGUMENT", selectionError!, context.Root);
        if (CliArgs.Has(args, "--capture-screenshots") && mode is not ("auto" or "xml"))
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "INVALID_ARGUMENT", "--capture-screenshots 仅适用于 --mode auto 或 --mode xml。示例：RimDevTools.exe test run --mode auto --capture-screenshots", context.Root);
        if (CliArgs.Has(args, "--sync-source") && !CliArgs.Has(args, "--deploy"))
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "SYNC_SOURCE_REQUIRES_DEPLOY", "--sync-source 必须与 --deploy 一起使用", context.Root);
        if (!CliArgs.TryPositiveInt(args, "--timeout-seconds", context.Config.Timeouts.MaxWait, out var timeout, out var timeoutError))
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "TIMEOUT_INVALID", timeoutError!, context.Root);
        var isolation = IsolatedDesktopOptions.Resolve(context.Config, mode, args, out var isolationError);
        if (isolation is null)
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, isolationError!.Code, isolationError.Message, context.Root);

        var targetPackageId = CliArgs.Value(args, "--target-package-id")?.Trim();
        if (string.IsNullOrWhiteSpace(targetPackageId)) targetPackageId = context.Config.CurrentPackageId(context.Root);
        if (string.IsNullOrWhiteSpace(targetPackageId))
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "TARGET_PACKAGE_ID_MISSING", "无法确定自动测试目标；请提供 --target-package-id", context.Root);
        var captureScreenshots = CliArgs.Has(args, "--capture-screenshots");

        var gamePath = Path.Combine(context.Config.RimWorldDir ?? "", "RimWorldWin64.exe");
        if (!File.Exists(gamePath))
            return CliResult.WriteFailure(command, ExitCodes.MissingDependency, "GAME_NOT_FOUND", $"找不到游戏可执行文件: {gamePath}", context.Root);
        var steam = SteamSessionGuard.CheckForLaunch();
        if (!steam.IsLoggedIn)
            return CliResult.WriteFailure(command, ExitCodes.GameFailed, steam.Code, steam.Message, context.Root, new { steam.IsRunning, steam.SteamRoot, steam.LoginUsersPath });

        var modsFileValue = CliArgs.Value(args, "--active-mods-file");
        var modsFile = modsFileValue is null ? null : Path.GetFullPath(modsFileValue, context.Root);
        var modList = ModListParser.Resolve(context, modsFile, targetPackageId, out var modError);
        if (modList is null)
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, modError!.Code, modError.Message, context.Root, modError.Details);
        var dependencyWarnings = modList.DependencyWarnings
            .Select(ModDependencyInspector.FormatWarning)
            .ToArray();
        var runId = Guid.NewGuid().ToString("N");
        using var gate = EnvironmentLock.TryAcquire(configDirectory, command, context.Root, runId, out var busyInfo);
        if (gate is null)
            return CliResult.WriteFailure(command, ExitCodes.Busy, "ENVIRONMENT_BUSY", "RimWorld 环境正被占用", context.Root, new { busyInfo });

        if (!await ProcessUtil.WaitForRimWorldExitAsync(context.Config.Timeouts.ExistingGameExit, context.Config.Timeouts.PostExitDelay, cancellationToken))
            return CliResult.WriteFailure(command, ExitCodes.Timeout, "GAME_EXIT_TIMEOUT", "等待已有 RimWorld 退出超时", context.Root);

        var recovery = await TransactionRecovery.RecoverAsync(configDirectory);
        if (!recovery.Ok)
            return CliResult.WriteFailure(command, ExitCodes.TaskFailed, recovery.Error!.Code, recovery.Error.Message, context.Root, recovery.Error.Details);

        if (CliArgs.Has(args, "--deploy"))
        {
            var deployArgs = new List<string> { "deploy" };
            if (CliArgs.Has(args, "--sync-source")) deployArgs.Add("--sync-source");
            var deploy = await DeployCommand.ExecuteAsync(deployArgs.ToArray(), cancellationToken, environmentPrepared: true);
            if (!deploy.Ok)
                return CliResult.WriteFailure(command, deploy.ExitCode, "DEPLOY_FAILED", "测试前部署失败", context.Root, new { deploy.Error, dependencyWarnings }, dependencyWarnings);
        }

        ConfigTransaction? transaction = null;
        GameProcessSession? gameSession = null;
        TestOutcome outcome;
        var gameExited = true;
        var outputPath = SettingsEditor.AutoTestOutputPath(context);
        var resultSnapshot = AutoResultValidator.Snapshot(outputPath);
        var logOffset = File.Exists(playerLogPath) ? new FileInfo(playerLogPath).Length : 0;
        try
        {
            transaction = await ConfigTransaction.CreateAsync(
                configDirectory,
                runId,
                [modsConfigPath, logSyncSettingsPath]);
            await transaction.WriteAllTextAsync(modsConfigPath, ModListParser.Render(modsConfigPath, modList.Mods));
            await SettingsEditor.UpdateAsync(context, mode, transaction, runId, targetPackageId, captureScreenshots, muteAudio: mode != "manual", selection.Scopes, selection.HasExplicitScopes, selection.DefNames);

            LogArchive.Rotate(SettingsEditor.SyncLogPath(context), context.Config.Retention.MaxLogFiles);

            var gameArguments = new List<string> { "-popupwindow" };
            if (mode is "auto" or "xml") gameArguments.Add("quicktest");
            gameSession = GameProcessLauncher.Start(gamePath, context.Config.RimWorldDir, gameArguments, isolation.Enabled, runId);
            outcome = await WaitForModeAsync(mode, gameSession.Process, timeout, playerLogPath, logOffset, outputPath, targetPackageId, runId, resultSnapshot, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            outcome = TestOutcome.Failure(ExitCodes.Cancelled, "CANCELLED", "操作已取消");
        }
        catch (GameProcessLaunchException ex)
        {
            outcome = TestOutcome.Failure(ExitCodes.GameFailed, ex.Code, ex.Message, new { ex.NativeError });
        }
        catch (Exception ex)
        {
            outcome = TestOutcome.Failure(ExitCodes.GameFailed, "TEST_FAILED", ex.Message);
        }
        finally
        {
            if (gameSession is not null)
            {
                var termination = await ProcessUtil.TerminateAsync(gameSession.Process);
                if (!termination.Exited)
                {
                    gameExited = false;
                    outcome = TestOutcome.Failure(ExitCodes.TaskFailed, "GAME_TERMINATION_FAILED", $"无法终止 RimWorld，已保留配置事务: {termination.Error}");
                }
                gameSession.Dispose();
                if (gameExited && gameSession.CleanupError is { } cleanupError)
                    outcome = TestOutcome.Failure(ExitCodes.TaskFailed, "ISOLATED_DESKTOP_CLEANUP_FAILED", "无法关闭隔离桌面", new { nativeError = cleanupError });
            }
        }

        if (!gameExited)
            return CliResult.WriteFailure(command, outcome.ExitCode, outcome.Code, outcome.Message, context.Root, new { runId, mode, dependencyWarnings, isolatedDesktop = isolation.Enabled, isolatedDesktopSource = isolation.Source, transactionRetained = transaction is not null }, dependencyWarnings);

        if (transaction is not null)
        {
            try { await transaction.RestoreAsync(force: true); }
            catch (Exception ex) { outcome = TestOutcome.Failure(ExitCodes.TaskFailed, "TRANSACTION_RESTORE_FAILED", ex.Message); }
        }

        if (mode is "auto" or "xml")
        {
            try { LogArchive.PruneAutoTestRuns(outputPath, context.Config.Retention.MaxAutoTestRuns); }
            catch (Exception ex) { outcome = TestOutcome.Failure(ExitCodes.TaskFailed, "AUTO_RESULT_RETENTION_FAILED", ex.Message); }
        }

        if (!outcome.Ok)
            return CliResult.WriteFailure(command, outcome.ExitCode, outcome.Code, outcome.Message, context.Root, new { runId, mode, scopes = selection.Scopes, defNames = selection.DefNames, dependencyWarnings, isolatedDesktop = isolation.Enabled, isolatedDesktopSource = isolation.Source, mods = modList.Mods, additions = modList.Additions, outcome.Details }, dependencyWarnings);

        return CliResult.WriteSuccess(command, context.Root, new
        {
            runId,
            mode,
            targetPackageId,
            scopes = selection.Scopes,
            defNames = selection.DefNames,
            captureScreenshots,
            isolatedDesktop = isolation.Enabled,
            isolatedDesktopSource = isolation.Source,
            mods = modList.Mods,
            additions = modList.Additions,
            signal = outcome.Message,
            result = outcome.Details,
            recoveredTransaction = recovery.Found
        }, dependencyWarnings);
    }

    private static async Task<TestOutcome> WaitForModeAsync(
        string mode,
        Process game,
        int timeoutSeconds,
        string playerLogPath,
        long logOffset,
        string outputPath,
        string? packageId,
        string runId,
        IReadOnlyDictionary<string, AutoResultFingerprint> resultSnapshot,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        const string quickSignal = "[LogSyncMod] RUNTIME_READY_TO_CLOSE";
        var autoSignal = $"[LogSyncAutoTest] COMPLETED runId={runId}";
        var scanner = new IncrementalLogSignalScanner(quickSignal, autoSignal);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (mode == "manual")
            {
                if (game.HasExited) return TestOutcome.Success("manual game exit");
            }
            else
            {
                var read = await IncrementalLogReader.ReadAsync(playerLogPath, logOffset, cancellationToken);
                logOffset = read.Offset;
                if (read.Reset) scanner.Reset();
                scanner.Append(read.Text);
                if (mode == "quick" && scanner.HasSeen(quickSignal))
                    return TestOutcome.Success("quick ready signal");

                if (mode is "auto" or "xml")
                {
                    var completed = scanner.HasSeen(autoSignal);
                    if (completed || game.HasExited)
                    {
                        var validation = AutoResultValidator.Validate(outputPath, packageId, runId, resultSnapshot);
                        if (validation.Ok) return TestOutcome.Success(completed ? $"{mode} completed signal" : $"{mode} result after game exit", validation.Details);
                        if (completed) return TestOutcome.Failure(ExitCodes.GameFailed, validation.Code, validation.Message, validation.Details);
                    }
                }
                if (game.HasExited) return TestOutcome.Failure(ExitCodes.GameFailed, "GAME_EXITED_EARLY", "RimWorld 在收到测试完成信号前退出");
            }

            await Task.Delay(500, cancellationToken);
        }
        return TestOutcome.Failure(ExitCodes.Timeout, "TEST_TIMEOUT", $"{mode} 模式等待超时");
    }

    private sealed record TestOutcome(bool Ok, int ExitCode, string Code, string Message, object? Details = null)
    {
        public static TestOutcome Success(string message, object? details = null) => new(true, ExitCodes.Success, "", message, details);
        public static TestOutcome Failure(int exitCode, string code, string message, object? details = null) => new(false, exitCode, code, message, details);
    }
}

public sealed record AutoTestSelection(IReadOnlyList<string> Scopes, IReadOnlyList<string> DefNames, bool HasExplicitScopes);

public static class AutoTestSelectionParser
{
    private static readonly IReadOnlyDictionary<string, string> ScopeNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["standard"] = "Standard",
        ["content"] = "Content",
        ["recipes"] = "Recipes",
        ["hediffs"] = "Hediffs",
        ["turrets"] = "Turrets",
        ["rangedweapons"] = "RangedWeapons",
        ["meleeweapons"] = "MeleeWeapons",
        ["threeviews"] = "ThreeViews",
        ["combatpawns"] = "CombatPawns",
        ["skillpocketmaps"] = "SkillPocketMaps"
    };

    public static bool TryParse(string[] args, string mode, out AutoTestSelection selection, out string? error)
    {
        selection = new AutoTestSelection(["Standard"], [], false);
        error = null;

        var nativeScopes = CliArgs.Values(args, "--scope");
        if (nativeScopes.Count > 0 && mode is not ("auto" or "xml"))
            return Fail("--scope 仅适用于 --mode auto 或 --mode xml。示例：RimDevTools.exe test run --mode auto --scope turrets", out error);

        var rawScopes = nativeScopes;
        var scopes = new List<string>();
        var seenScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in rawScopes)
        {
            var value = raw.Trim();
            if (value.Length == 0 || !ScopeNames.TryGetValue(value, out var normalized))
                return Fail($"未知测试范围: {raw}", out error);
            if (seenScopes.Add(normalized)) scopes.Add(normalized);
        }
        if (scopes.Count == 0) scopes.Add("Standard");

        var nativeDefNames = CliArgs.Values(args, "--defname");
        if (nativeDefNames.Count > 0 && mode != "auto")
            return Fail("--defname 仅适用于 --mode auto。示例：RimDevTools.exe test run --mode auto --defname MyMod_Weapon", out error);

        var defNames = new List<string>();
        var seenDefNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in nativeDefNames)
        {
            var value = raw.Trim();
            if (value.Length == 0) return Fail("--defname 不能为空", out error);
            if (seenDefNames.Add(value)) defNames.Add(value);
        }

        selection = new AutoTestSelection(scopes, defNames, rawScopes.Count > 0);
        return true;
    }

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }
}

public sealed record ModListAddition(string PackageId, int Index, string Reason);
public sealed record ModListResolution(IReadOnlyList<string> Mods, IReadOnlyList<ModListAddition> Additions, IReadOnlyList<ModDependencyWarning> DependencyWarnings);

public static class ModListParser
{
    internal static readonly string[] RequiredStartupMods =
    [
        "brrainz.harmony",
        "sarg.smartspeed",
        "brrainz.cameraplus",
        "erdelf.humanoidalienraces",
        "trigger.logsyncmod"
    ];

    public static ModListResolution? Resolve(ProjectContext context, string? path, out CliError? error)
        => ResolveCore(context, path, "trigger.logsyncmod", "logsync_tooling", context.Config.CurrentPackageId(context.Root), "current_project", out error);

    public static ModListResolution? Resolve(ProjectContext context, string? path, string? targetPackageId, out CliError? error)
        => ResolveCore(context, path, "trigger.logsyncmod", "logsync_tooling", targetPackageId, "target_mod", out error);

    private static ModListResolution? ResolveCore(ProjectContext context, string? path, string toolingPackageId, string toolingReason, string? targetPackageId, string targetReason, out CliError? error)
    {
        error = null;
        List<string> mods;
        if (path is null)
        {
            mods = context.Config.MinActiveMods
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        else
        {
            if (!File.Exists(path))
            {
                error = new CliError("MODS_FILE_NOT_FOUND", $"ModList 文件不存在: {path}");
                return null;
            }
            try
            {
                var document = XDocument.Load(path);
                var active = document.Root?.Elements().FirstOrDefault(x => x.Name.LocalName.Equals("activeMods", StringComparison.OrdinalIgnoreCase));
                if (active is null)
                {
                    error = new CliError("ACTIVE_MODS_MISSING", "ModList XML 缺少 activeMods");
                    return null;
                }
                mods = active.Elements()
                    .Where(x => x.Name.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Value.Trim())
                    .Where(x => x.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                error = new CliError("MODS_XML_INVALID", ex.Message);
                return null;
            }
        }

        var requiredMods = context.Config.MinActiveMods
            .Where(packageId => !string.IsNullOrWhiteSpace(packageId))
            .Concat(RequiredStartupMods)
            .Append(toolingPackageId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var disabledRequiredMods = requiredMods
            .Where(required => !mods.Contains(required, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (disabledRequiredMods.Length > 0)
        {
            error = new CliError(
                "REQUIRED_MODS_DISABLED",
                "测试启动被拒绝：以下 min_active_mods 或启动必须模组未出现在本次 ModList 的 <activeMods> 中。请将它们加入 mod_config.jsonc 的 min_active_mods，或加入 --active-mods-file 指定文件的 <activeMods> 后重试；这不是模组文件缺失错误。",
                new
                {
                    required = requiredMods,
                    missingFromActiveMods = disabledRequiredMods,
                    activationConfig = "min_active_mods",
                    activationModsFileElement = "activeMods"
                });
            return null;
        }

        var additions = new List<ModListAddition>();
        if (!mods.Contains("ludeon.rimworld", StringComparer.OrdinalIgnoreCase))
        {
            var harmonyIndex = mods.FindIndex(mod => mod.Equals("brrainz.harmony", StringComparison.OrdinalIgnoreCase));
            var coreIndex = harmonyIndex >= 0 ? harmonyIndex + 1 : 0;
            mods.Insert(coreIndex, "ludeon.rimworld");
            additions.Add(new ModListAddition("ludeon.rimworld", coreIndex, "rimworld_core"));
        }
        NormalizeDefaultModOrder(mods, context.Config.MinActiveMods);
        AddBeforeTooling(mods, targetPackageId, toolingPackageId, targetReason, additions);
        AddBeforeTooling(mods, toolingPackageId, toolingPackageId, toolingReason, additions);

        var missing = mods.Where(x => !IsResolvable(context, x)).ToArray();
        if (missing.Length > 0)
        {
            error = new CliError("MOD_NOT_RESOLVED", "以下模组无法在游戏 Mods 或 Workshop 中解析", new { missing });
            return null;
        }
        var dependencyWarnings = ModDependencyInspector.Inspect(context, mods);
        return new ModListResolution(mods, additions, dependencyWarnings);
    }

    public static List<string>? Load(ProjectContext context, string? path, out CliError? error)
        => Resolve(context, path, out error)?.Mods.ToList();

    public static string Render(string path, IReadOnlyList<string> mods)
    {
        var document = File.Exists(path) ? XDocument.Load(path) : new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement("ModsConfigData"));
        var root = document.Root ?? throw new InvalidDataException("ModsConfig.xml 缺少根节点");
        var active = root.Elements().FirstOrDefault(x => x.Name.LocalName.Equals("activeMods", StringComparison.OrdinalIgnoreCase)) ?? new XElement("activeMods");
        if (active.Parent is null) root.Add(active);
        active.RemoveNodes();
        foreach (var mod in mods) active.Add(new XElement("li", mod));
        return document.Declaration is null ? document.ToString() : $"{document.Declaration}{Environment.NewLine}{document}";
    }

    public static Task WriteAsync(string path, IReadOnlyList<string> mods)
    {
        AtomicFile.WriteAllText(path, Render(path, mods));
        return Task.CompletedTask;
    }

    private static void AddBeforeTooling(List<string> mods, string? packageId, string toolingPackageId, string reason, List<ModListAddition> additions)
    {
        if (string.IsNullOrWhiteSpace(packageId) || mods.Contains(packageId, StringComparer.OrdinalIgnoreCase)) return;
        var index = mods.FindIndex(x => x.Equals(toolingPackageId, StringComparison.OrdinalIgnoreCase));
        if (index < 0) index = mods.Count;
        mods.Insert(index, packageId);
        additions.Add(new ModListAddition(packageId, index, reason));
    }

    private static void NormalizeDefaultModOrder(List<string> mods, IReadOnlyList<string> defaultOrder)
    {
        var orderedDefaults = defaultOrder
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (orderedDefaults.Count == 0) return;

        var rank = orderedDefaults
            .Select((packageId, index) => new { packageId, index })
            .ToDictionary(x => x.packageId, x => x.index, StringComparer.OrdinalIgnoreCase);
        var defaultIndices = mods
            .Select((packageId, index) => new { packageId, index })
            .Where(x => rank.ContainsKey(x.packageId))
            .Select(x => x.index)
            .ToArray();
        var sortedDefaults = defaultIndices
            .Select(index => mods[index])
            .OrderBy(packageId => rank[packageId])
            .ToArray();

        for (var i = 0; i < defaultIndices.Length; i++) mods[defaultIndices[i]] = sortedDefaults[i];
    }

    private static bool IsResolvable(ProjectContext context, string packageId)
    {
        if (packageId.Equals(context.Config.CurrentPackageId(context.Root), StringComparison.OrdinalIgnoreCase)) return true;
        if (packageId.StartsWith("ludeon.rimworld", StringComparison.OrdinalIgnoreCase)) return true;

        var roots = new[] { Path.Combine(context.Config.RimWorldDir ?? "", "Mods") }
            .Concat(context.Config.WorkshopPaths)
            .Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            foreach (var modDirectory in Directory.EnumerateDirectories(root))
            {
                var about = Path.Combine(modDirectory, "About", "About.xml");
                if (!File.Exists(about)) continue;
                try
                {
                    var candidate = XDocument.Load(about).Root?.Elements().FirstOrDefault(x => x.Name.LocalName.Equals("packageId", StringComparison.OrdinalIgnoreCase))?.Value.Trim();
                    if (candidate?.Equals(packageId, StringComparison.OrdinalIgnoreCase) == true) return true;
                }
                catch { }
            }
        }
        return false;
    }
}

public static class SettingsEditor
{
    public static string SyncLogPath(ProjectContext context) => Path.Combine(context.Root, ".tools", "log", "精选日志.log");
    public static string AutoTestOutputPath(ProjectContext context) => Path.Combine(context.Root, ".tools", "log", "AutoTestOutput");

    public static async Task<bool> UpdateAsync(ProjectContext context, string mode, ConfigTransaction? transaction = null, string? runId = null, string? targetPackageId = null, bool captureScreenshots = false, bool? muteAudio = null, IReadOnlyList<string>? scopes = null, bool hasExplicitScopes = false, IReadOnlyList<string>? defNames = null)
    {
        var path = GameEnvironment.LogSyncSettingsPath(context) ?? throw new InvalidOperationException("mods_config_path 未配置或无效");
        var document = File.Exists(path)
            ? XDocument.Load(path)
            : new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("SettingsBlock", new XElement("ModSettings", new XAttribute("Class", "LogSyncMod.LogSyncSettings"))));
        var settings = document.Root?.DescendantsAndSelf().FirstOrDefault(x => x.Name.LocalName.Equals("ModSettings", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("LogSync 设置缺少 ModSettings 节点");
        var changed = false;
        changed |= Set(settings, "logPath", SyncLogPath(context));
        changed |= SetList(settings, "keywords", context.Config.LogKeywords);

        var auto = mode is "auto" or "xml";
        changed |= Set(settings, "autoTestEnabled", auto ? "True" : "False");
        changed |= Set(settings, "autoTestTargetPackageId", auto ? targetPackageId?.ToLowerInvariant() ?? context.Config.CurrentPackageId(context.Root)?.ToLowerInvariant() ?? "" : "");
        changed |= Set(settings, "autoTestRunId", auto ? runId ?? "" : "");
        changed |= Set(settings, "autoTestOutputPath", AutoTestOutputPath(context));
        changed |= Set(settings, "autoTestMode", mode == "xml" ? "XmlDriven" : "Automatic");
        var effectiveScopes = scopes is { Count: > 0 } ? DistinctValues(scopes) : ["Standard"];
        var effectiveDefNames = defNames is { Count: > 0 } ? DistinctValues(defNames) : [];
        changed |= Set(settings, "autoTestScope", hasExplicitScopes ? effectiveScopes[0] : "Standard");
        changed |= hasExplicitScopes
            ? SetList(settings, "autoTestScopes", effectiveScopes)
            : Remove(settings, "autoTestScopes");
        changed |= effectiveDefNames.Count > 0
            ? SetList(settings, "autoTestTargetDefNames", effectiveDefNames)
            : Remove(settings, "autoTestTargetDefNames");
        changed |= Set(settings, "autoTestCaptureScreenshots", auto && captureScreenshots ? "True" : "False");
        changed |= Set(settings, "autoTestPreventPause", "True");
        changed |= Set(settings, "autoTestUseFastSpeed", "True");
        changed |= Set(settings, "testMuteAudio", muteAudio == true ? "True" : "False");

        if (!changed) return false;
        var content = document.ToString(SaveOptions.None);
        if (transaction is null) AtomicFile.WriteAllText(path, content);
        else await transaction.WriteAllTextAsync(path, content);
        return true;
    }

    private static bool Set(XElement parent, string name, string value)
    {
        var node = parent.Elements().FirstOrDefault(x => x.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (node is null)
        {
            parent.Add(new XElement(name, value));
            return true;
        }
        if (node.Value == value) return false;
        node.Value = value;
        return true;
    }

    private static bool SetList(XElement parent, string name, IEnumerable<string> values)
    {
        var normalized = values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray();
        var node = parent.Elements().FirstOrDefault(x => x.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
        var current = node?.Elements().Where(x => x.Name.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase)).Select(x => x.Value).ToArray() ?? [];
        if (current.SequenceEqual(normalized, StringComparer.Ordinal)) return false;
        node ??= new XElement(name);
        if (node.Parent is null) parent.Add(node);
        node.RemoveNodes();
        foreach (var value in normalized) node.Add(new XElement("li", value));
        return true;
    }

    private static bool Remove(XElement parent, string name)
    {
        var nodes = parent.Elements().Where(x => x.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (nodes.Length == 0) return false;
        foreach (var node in nodes) node.Remove();
        return true;
    }

    private static IReadOnlyList<string> DistinctValues(IEnumerable<string> values)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var normalized = value?.Trim();
            if (!string.IsNullOrEmpty(normalized) && seen.Add(normalized)) result.Add(normalized);
        }
        return result;
    }
}

public sealed record AutoResultFingerprint(long Length, long LastWriteTicks);
public sealed record AutoResultValidation(bool Ok, string Code, string Message, object? Details = null);

public static class AutoResultValidator
{
    public static IReadOnlyDictionary<string, AutoResultFingerprint> Snapshot(string outputPath)
        => !Directory.Exists(outputPath)
            ? new Dictionary<string, AutoResultFingerprint>(StringComparer.OrdinalIgnoreCase)
            : Directory.EnumerateFiles(outputPath, "*_Result.json", SearchOption.TopDirectoryOnly)
                .ToDictionary(Path.GetFullPath, Fingerprint, StringComparer.OrdinalIgnoreCase);

    public static AutoResultValidation Validate(string outputPath, string? expectedPackageId, string expectedRunId, IReadOnlyDictionary<string, AutoResultFingerprint> snapshot)
    {
        if (!Directory.Exists(outputPath)) return Failure("AUTO_RESULT_MISSING", "未找到自动测试输出目录");
        var candidates = Directory.EnumerateFiles(outputPath, "*_Result.json", SearchOption.TopDirectoryOnly)
            .Where(path => !snapshot.TryGetValue(Path.GetFullPath(path), out var old) || old != Fingerprint(path))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
        if (candidates.Length == 0) return Failure("AUTO_RESULT_MISSING", "未找到本轮自动测试结果 JSON");

        foreach (var path in candidates)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var root = document.RootElement;
                var packageId = root.TryGetProperty("packageId", out var packageNode) ? packageNode.GetString() : null;
                if (!string.Equals(packageId, expectedPackageId, StringComparison.OrdinalIgnoreCase)) continue;
                var runId = root.TryGetProperty("runId", out var runIdNode) ? runIdNode.GetString() : null;
                if (!string.Equals(runId, expectedRunId, StringComparison.Ordinal)) continue;
                if (root.TryGetProperty("evidencePath", out var evidencePathNode))
                {
                    var evidencePathValue = evidencePathNode.GetString();
                    if (string.IsNullOrWhiteSpace(evidencePathValue))
                        return Failure("AUTO_EVIDENCE_MISSING", "结果声明了空的 Evidence 路径", new { path });
                    var evidencePath = Path.IsPathRooted(evidencePathValue)
                        ? evidencePathValue
                        : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, evidencePathValue));
                    if (!File.Exists(evidencePath))
                        return Failure("AUTO_EVIDENCE_MISSING", "未找到结构化 Evidence 文件", new { path, evidencePath });
                    try
                    {
                        using var evidenceDocument = JsonDocument.Parse(File.ReadAllText(evidencePath));
                        var evidenceRoot = evidenceDocument.RootElement;
                        if (!evidenceRoot.TryGetProperty("schemaVersion", out var schemaNode) || !schemaNode.TryGetInt32(out var schemaVersion) || schemaVersion != 1)
                            return Failure("AUTO_EVIDENCE_INVALID", "Evidence schemaVersion 不受支持", new { path, evidencePath });
                        var evidenceRunId = evidenceRoot.TryGetProperty("runId", out var evidenceRunNode) ? evidenceRunNode.GetString() : null;
                        var evidencePackageId = evidenceRoot.TryGetProperty("packageId", out var evidencePackageNode) ? evidencePackageNode.GetString() : null;
                        if (!string.Equals(evidenceRunId, runId, StringComparison.Ordinal) || !string.Equals(evidencePackageId, packageId, StringComparison.OrdinalIgnoreCase))
                            return Failure("AUTO_EVIDENCE_MISMATCH", "Evidence 与 Result 的 runId/packageId 不匹配", new { path, evidencePath, runId, evidenceRunId, packageId, evidencePackageId });
                    }
                    catch (JsonException ex)
                    {
                        return Failure("AUTO_EVIDENCE_INVALID", "Evidence JSON 无法解析: " + ex.Message, new { path, evidencePath });
                    }
                    catch (IOException ex)
                    {
                        return Failure("AUTO_EVIDENCE_INVALID", "无法读取 Evidence: " + ex.Message, new { path, evidencePath });
                    }
                }
                if (!root.TryGetProperty("completeness", out var completeness) || completeness.GetString() != "Complete")
                    return Failure("AUTO_RESULT_INCOMPLETE", "自动测试结果不完整", new { path });
                if (!ReadInt(root, "manifestMissing", out var missing) || missing != 0)
                    return Failure("AUTO_RESULT_INCOMPLETE", "自动测试存在缺失用例", new { path, missing });
                var hasExecuted = ReadInt(root, "manifestExecuted", out var executed);
                var hasPlanned = ReadInt(root, "manifestPlanned", out var planned);
                if (!hasExecuted || !hasPlanned || executed < planned)
                    return Failure("AUTO_RESULT_INCOMPLETE", "自动测试未执行全部计划用例", new { path, executed, planned });
                if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                    return Failure("AUTO_RESULT_INVALID", "自动测试结果缺少 results", new { path });

                var failed = results.EnumerateArray()
                    .Where(item => !item.TryGetProperty("status", out var status) || status.GetString() != "passed")
                    .Select(item => item.TryGetProperty("caseId", out var caseId) ? caseId.GetString() : null)
                    .ToArray();
                if (failed.Length > 0) return Failure("AUTO_RESULT_FAILED", "自动测试结果包含失败或跳过用例", new { path, failed });
                return new AutoResultValidation(true, "", "", new { path, packageId, runId, executed, planned });
            }
            catch (JsonException ex)
            {
                return Failure("AUTO_RESULT_INVALID", $"自动测试结果 JSON 无法解析: {ex.Message}", new { path });
            }
            catch (IOException ex)
            {
                return Failure("AUTO_RESULT_INVALID", $"无法读取自动测试结果: {ex.Message}", new { path });
            }
        }
        return Failure("AUTO_RESULT_MISMATCH", "本轮结果不属于当前项目或 runId 不匹配", new { expectedPackageId, expectedRunId });
    }

    private static AutoResultFingerprint Fingerprint(string path)
    {
        var info = new FileInfo(path);
        return new AutoResultFingerprint(info.Length, info.LastWriteTimeUtc.Ticks);
    }

    private static bool ReadInt(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out var node) && node.TryGetInt32(out value);
    }

    private static AutoResultValidation Failure(string code, string message, object? details = null) => new(false, code, message, details);
}

public static class LogArchive
{
    public static void Rotate(string logPath, int keepCount)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        if (File.Exists(logPath) && new FileInfo(logPath).Length > 0)
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var archive = Path.Combine(Path.GetDirectoryName(logPath)!, $"{Path.GetFileNameWithoutExtension(logPath)}_{stamp}{Path.GetExtension(logPath)}");
            var index = 1;
            while (File.Exists(archive)) archive = Path.Combine(Path.GetDirectoryName(logPath)!, $"{Path.GetFileNameWithoutExtension(logPath)}_{stamp}_{index++}{Path.GetExtension(logPath)}");
            File.Move(logPath, archive);
        }
        AtomicFile.WriteAllText(logPath, "");

        var managed = Directory.EnumerateFiles(Path.GetDirectoryName(logPath)!, $"{Path.GetFileNameWithoutExtension(logPath)}_*{Path.GetExtension(logPath)}")
            .Append(logPath)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
        foreach (var obsolete in managed.Skip(Math.Max(1, keepCount))) File.Delete(obsolete);
    }

    public static void PruneAutoTestRuns(string outputPath, int keepCount)
    {
        if (!Directory.Exists(outputPath)) return;
        var suffixes = new[] { "_Result.json", "_Report.md", "_Manifest.json", "_Evidence.json", "_Screenshots" };
        var groups = Directory.EnumerateFileSystemEntries(outputPath)
            .Select(path => new { Path = path, Key = suffixes.FirstOrDefault(suffix => Path.GetFileName(path).EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) is { } suffix ? Path.GetFileName(path)[..^suffix.Length] : null })
            .Where(x => x.Key is not null)
            .GroupBy(x => x.Key!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Max(item => File.GetLastWriteTimeUtc(item.Path)))
            .Skip(Math.Max(0, keepCount));
        foreach (var group in groups)
        {
            foreach (var entry in group)
            {
                if (Directory.Exists(entry.Path)) Directory.Delete(entry.Path, true);
                else File.Delete(entry.Path);
            }
        }
    }
}
