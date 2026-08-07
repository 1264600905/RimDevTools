using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

namespace RimDevTools;

public static class TestCommand
{
    public static async Task<int> SettingsAsync(string[] args, CancellationToken cancellationToken = default)
    {
        const string command = "test settings";
        var context = CliApplication.RequireProject(command, out var code);
        if (context is null) return code;
        var templateError = ProjectTemplatePreflight.Validate(context);
        if (templateError is not null)
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, templateError.Code, templateError.Message, context.Root, templateError.Details);

        var runId = Guid.NewGuid().ToString("N");
        using var gate = EnvironmentLock.TryAcquire(GameEnvironment.ConfigDirectory(), command, context.Root, runId, out var busyInfo);
        if (gate is null)
            return CliResult.WriteFailure(command, ExitCodes.Busy, "ENVIRONMENT_BUSY", "RimWorld 环境正被占用", context.Root, new { busyInfo });

        if (!await ProcessUtil.WaitForRimWorldExitAsync(context.Config.Timeouts.ExistingGameExit, context.Config.Timeouts.PostExitDelay, cancellationToken))
            return CliResult.WriteFailure(command, ExitCodes.Timeout, "GAME_EXIT_TIMEOUT", "等待已有 RimWorld 退出超时", context.Root);

        var recovery = await TransactionRecovery.RecoverAsync(GameEnvironment.ConfigDirectory());
        if (!recovery.Ok)
            return CliResult.WriteFailure(command, ExitCodes.TaskFailed, recovery.Error!.Code, recovery.Error.Message, context.Root, recovery.Error.Details);

        try
        {
            var changed = await SettingsEditor.UpdateAsync(context, "quick", muteAudio: false);
            return CliResult.WriteSuccess(command, context.Root, new
            {
                runId,
                settingsPath = GameEnvironment.LogSyncSettingsPath(),
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

        var mode = CliArgs.Value(args, "--mode")?.ToLowerInvariant() ?? "quick";
        if (mode is not ("quick" or "manual" or "auto" or "xml"))
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "MODE_INVALID", $"不支持的测试模式: {mode}", context.Root);
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

        var modsFileValue = CliArgs.Value(args, "--mods-file");
        var modsFile = modsFileValue is null ? null : Path.GetFullPath(modsFileValue, context.Root);
        var modList = ModListParser.Resolve(context, modsFile, targetPackageId, out var modError);
        if (modList is null)
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, modError!.Code, modError.Message, context.Root, modError.Details);
        var runId = Guid.NewGuid().ToString("N");
        using var gate = EnvironmentLock.TryAcquire(GameEnvironment.ConfigDirectory(), command, context.Root, runId, out var busyInfo);
        if (gate is null)
            return CliResult.WriteFailure(command, ExitCodes.Busy, "ENVIRONMENT_BUSY", "RimWorld 环境正被占用", context.Root, new { busyInfo });

        if (!await ProcessUtil.WaitForRimWorldExitAsync(context.Config.Timeouts.ExistingGameExit, context.Config.Timeouts.PostExitDelay, cancellationToken))
            return CliResult.WriteFailure(command, ExitCodes.Timeout, "GAME_EXIT_TIMEOUT", "等待已有 RimWorld 退出超时", context.Root);

        var recovery = await TransactionRecovery.RecoverAsync(GameEnvironment.ConfigDirectory());
        if (!recovery.Ok)
            return CliResult.WriteFailure(command, ExitCodes.TaskFailed, recovery.Error!.Code, recovery.Error.Message, context.Root, recovery.Error.Details);

        if (CliArgs.Has(args, "--deploy"))
        {
            var deployArgs = new List<string> { "deploy" };
            if (CliArgs.Has(args, "--sync-source")) deployArgs.Add("--sync-source");
            var deploy = await DeployCommand.ExecuteAsync(deployArgs.ToArray(), cancellationToken, environmentPrepared: true);
            if (!deploy.Ok)
                return CliResult.WriteFailure(command, deploy.ExitCode, "DEPLOY_FAILED", "测试前部署失败", context.Root, deploy.Error);
        }

        ConfigTransaction? transaction = null;
        GameProcessSession? gameSession = null;
        TestOutcome outcome;
        var gameExited = true;
        var outputPath = SettingsEditor.AutoTestOutputPath(context);
        var resultSnapshot = AutoResultValidator.Snapshot(outputPath);
        var logOffset = File.Exists(GameEnvironment.PlayerLogPath()) ? new FileInfo(GameEnvironment.PlayerLogPath()).Length : 0;
        try
        {
            transaction = await ConfigTransaction.CreateAsync(
                GameEnvironment.ConfigDirectory(),
                runId,
                [GameEnvironment.ModsConfigPath(), GameEnvironment.LogSyncSettingsPath()]);
            await transaction.WriteAllTextAsync(GameEnvironment.ModsConfigPath(), ModListParser.Render(GameEnvironment.ModsConfigPath(), modList.Mods));
            await SettingsEditor.UpdateAsync(context, mode, transaction, runId, targetPackageId, captureScreenshots, muteAudio: mode != "manual");

            LogArchive.Rotate(SettingsEditor.SyncLogPath(context), context.Config.Retention.MaxLogFiles);

            var gameArguments = new List<string> { "-popupwindow" };
            if (mode is "auto" or "xml") gameArguments.Add("quicktest");
            gameSession = GameProcessLauncher.Start(gamePath, context.Config.RimWorldDir, gameArguments, isolation.Enabled, runId);
            outcome = await WaitForModeAsync(mode, gameSession.Process, timeout, logOffset, outputPath, targetPackageId, runId, resultSnapshot, cancellationToken);
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
            return CliResult.WriteFailure(command, outcome.ExitCode, outcome.Code, outcome.Message, context.Root, new { runId, mode, isolatedDesktop = isolation.Enabled, isolatedDesktopSource = isolation.Source, transactionRetained = transaction is not null });

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
            return CliResult.WriteFailure(command, outcome.ExitCode, outcome.Code, outcome.Message, context.Root, new { runId, mode, isolatedDesktop = isolation.Enabled, isolatedDesktopSource = isolation.Source, mods = modList.Mods, additions = modList.Additions, outcome.Details });

        return CliResult.WriteSuccess(command, context.Root, new
        {
            runId,
            mode,
            targetPackageId,
            captureScreenshots,
            isolatedDesktop = isolation.Enabled,
            isolatedDesktopSource = isolation.Source,
            mods = modList.Mods,
            additions = modList.Additions,
            signal = outcome.Message,
            result = outcome.Details,
            recoveredTransaction = recovery.Found
        });
    }

    private static async Task<TestOutcome> WaitForModeAsync(
        string mode,
        Process game,
        int timeoutSeconds,
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
                var read = await IncrementalLogReader.ReadAsync(GameEnvironment.PlayerLogPath(), logOffset, cancellationToken);
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

public sealed record ModListAddition(string PackageId, int Index, string Reason);
public sealed record ModListResolution(IReadOnlyList<string> Mods, IReadOnlyList<ModListAddition> Additions);

public static class ModListParser
{
    private static readonly string[] RequiredStartupMods =
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

    public static ModListResolution? ResolveData(ProjectContext context, string? path, out CliError? error)
        => ResolveCore(context, path, "kearril.rimsearcherdatamod", "rimsearcher_tooling", context.Config.CurrentPackageId(context.Root), "current_project", out error);

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

        var requiredMods = RequiredStartupMods
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
                "测试启动被拒绝：以下默认模组未出现在本次 ModList 的 <activeMods> 中。请将它们加入 mod_config.jsonc 的 min_active_mods，或加入 --mods-file 指定文件的 <activeMods> 后重试；这不是模组文件缺失错误。",
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
        AddBeforeTooling(mods, targetPackageId, toolingPackageId, targetReason, additions);
        AddBeforeTooling(mods, toolingPackageId, toolingPackageId, toolingReason, additions);

        var missing = mods.Where(x => !IsResolvable(context, x)).ToArray();
        if (missing.Length > 0)
        {
            error = new CliError("MOD_NOT_RESOLVED", "以下模组无法在游戏 Mods 或 Workshop 中解析", new { missing });
            return null;
        }
        return new ModListResolution(mods, additions);
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

    public static async Task<bool> UpdateAsync(ProjectContext context, string mode, ConfigTransaction? transaction = null, string? runId = null, string? targetPackageId = null, bool captureScreenshots = false, bool? muteAudio = null)
    {
        var path = GameEnvironment.LogSyncSettingsPath();
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
        changed |= Set(settings, "autoTestScope", "Standard");
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
        var suffixes = new[] { "_Result.json", "_Report.md", "_Manifest.json", "_Screenshots" };
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
