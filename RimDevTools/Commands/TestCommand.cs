using System.Diagnostics;
using System.Text;
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
            var changed = await SettingsEditor.UpdateAsync(context, "quick");
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

        var mode = CliArgs.Value(args, "--mode")?.ToLowerInvariant() ?? "quick";
        if (mode is not ("quick" or "manual" or "auto" or "xml"))
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "MODE_INVALID", $"不支持的测试模式: {mode}", context.Root);
        if (CliArgs.Has(args, "--sync-source") && !CliArgs.Has(args, "--deploy"))
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "SYNC_SOURCE_REQUIRES_DEPLOY", "--sync-source 必须与 --deploy 一起使用", context.Root);
        if (!CliArgs.TryPositiveInt(args, "--timeout-seconds", context.Config.Timeouts.MaxWait, out var timeout, out var timeoutError))
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "TIMEOUT_INVALID", timeoutError!, context.Root);

        var modsFileValue = CliArgs.Value(args, "--mods-file");
        var modsFile = modsFileValue is null ? null : Path.GetFullPath(modsFileValue, context.Root);
        var modList = ModListParser.Resolve(context, modsFile, out var modError);
        if (modList is null)
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, modError!.Code, modError.Message, context.Root, modError.Details);

        var gamePath = Path.Combine(context.Config.RimWorldDir ?? "", "RimWorldWin64.exe");
        if (!File.Exists(gamePath))
            return CliResult.WriteFailure(command, ExitCodes.MissingDependency, "GAME_NOT_FOUND", $"找不到游戏可执行文件: {gamePath}", context.Root);
        if (!File.Exists(GameEnvironment.LogSyncSettingsPath()))
            return CliResult.WriteFailure(command, ExitCodes.MissingDependency, "LOGSYNC_SETTINGS_NOT_FOUND", $"找不到 LogSyncMod 设置文件: {GameEnvironment.LogSyncSettingsPath()}", context.Root);

        if (CliArgs.Has(args, "--deploy"))
        {
            var deployArgs = new List<string> { "deploy" };
            if (CliArgs.Has(args, "--sync-source")) deployArgs.Add("--sync-source");
            var deploy = await DeployCommand.ExecuteAsync(deployArgs.ToArray(), cancellationToken);
            if (!deploy.Ok)
                return CliResult.WriteFailure(command, deploy.ExitCode, "DEPLOY_FAILED", "测试前部署失败", context.Root, deploy.Error);
        }

        var runId = Guid.NewGuid().ToString("N");
        using var gate = EnvironmentLock.TryAcquire(GameEnvironment.ConfigDirectory(), command, context.Root, runId, out var busyInfo);
        if (gate is null)
            return CliResult.WriteFailure(command, ExitCodes.Busy, "ENVIRONMENT_BUSY", "RimWorld 环境正被占用", context.Root, new { busyInfo });

        if (!await ProcessUtil.WaitForRimWorldExitAsync(context.Config.Timeouts.ExistingGameExit, context.Config.Timeouts.PostExitDelay, cancellationToken))
            return CliResult.WriteFailure(command, ExitCodes.Timeout, "GAME_EXIT_TIMEOUT", "等待已有 RimWorld 退出超时", context.Root);

        var recovery = await TransactionRecovery.RecoverAsync(GameEnvironment.ConfigDirectory());
        if (!recovery.Ok)
            return CliResult.WriteFailure(command, ExitCodes.TaskFailed, recovery.Error!.Code, recovery.Error.Message, context.Root, recovery.Error.Details);

        ConfigTransaction? transaction = null;
        Process? game = null;
        TestOutcome outcome;
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
            await SettingsEditor.UpdateAsync(context, mode, transaction);

            LogArchive.Rotate(SettingsEditor.SyncLogPath(context), context.Config.Retention.MaxLogFiles);
            if (mode is "auto" or "xml") LogArchive.PruneAutoTestRuns(outputPath, context.Config.Retention.MaxAutoTestRuns);

            var startInfo = new ProcessStartInfo(gamePath)
            {
                WorkingDirectory = context.Config.RimWorldDir,
                UseShellExecute = false,
                CreateNoWindow = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-popupwindow");
            if (mode is "auto" or "xml") startInfo.ArgumentList.Add("quicktest");
            game = new Process { StartInfo = startInfo };
            game.OutputDataReceived += (_, eventArgs) => { if (eventArgs.Data is not null) Console.Error.WriteLine(eventArgs.Data); };
            game.ErrorDataReceived += (_, eventArgs) => { if (eventArgs.Data is not null) Console.Error.WriteLine(eventArgs.Data); };
            if (!game.Start())
            {
                outcome = TestOutcome.Failure(ExitCodes.GameFailed, "GAME_START_FAILED", "无法启动 RimWorld");
            }
            else
            {
                game.BeginOutputReadLine();
                game.BeginErrorReadLine();
                outcome = await WaitForModeAsync(mode, game, timeout, logOffset, outputPath, context.Config.CurrentPackageId(context.Root), resultSnapshot, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            outcome = TestOutcome.Failure(ExitCodes.Cancelled, "CANCELLED", "操作已取消");
        }
        catch (Exception ex)
        {
            outcome = TestOutcome.Failure(ExitCodes.GameFailed, "TEST_FAILED", ex.Message);
        }
        finally
        {
            if (game is not null)
            {
                try
                {
                    if (!game.HasExited) game.Kill(true);
                    await game.WaitForExitAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"终止 RimWorld 失败: {ex.Message}");
                }
                game.Dispose();
            }
        }

        if (transaction is not null)
        {
            try { await transaction.RestoreAsync(force: true); }
            catch (Exception ex) { outcome = TestOutcome.Failure(ExitCodes.TaskFailed, "TRANSACTION_RESTORE_FAILED", ex.Message); }
        }

        if (!outcome.Ok)
            return CliResult.WriteFailure(command, outcome.ExitCode, outcome.Code, outcome.Message, context.Root, new { runId, mode, mods = modList.Mods, additions = modList.Additions, outcome.Details });

        return CliResult.WriteSuccess(command, context.Root, new
        {
            runId,
            mode,
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
        IReadOnlyDictionary<string, AutoResultFingerprint> resultSnapshot,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (mode == "manual")
            {
                if (game.HasExited) return TestOutcome.Success("manual game exit");
            }
            else
            {
                var read = await ReadNewLogAsync(GameEnvironment.PlayerLogPath(), logOffset, cancellationToken);
                logOffset = read.Offset;
                if (mode == "quick" && read.Text.Contains("[LogSyncMod] RUNTIME_READY_TO_CLOSE", StringComparison.Ordinal))
                    return TestOutcome.Success("quick ready signal");

                if (mode is "auto" or "xml")
                {
                    var completed = read.Text.Contains("[LogSyncAutoTest] COMPLETED", StringComparison.Ordinal);
                    if (completed || game.HasExited)
                    {
                        var validation = AutoResultValidator.Validate(outputPath, packageId, resultSnapshot);
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

    private static async Task<(string Text, long Offset)> ReadNewLogAsync(string path, long offset, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return ("", 0);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
        if (stream.Length < offset) offset = 0;
        stream.Seek(offset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        return (text, stream.Position);
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
    public static ModListResolution? Resolve(ProjectContext context, string? path, out CliError? error)
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

        var additions = new List<ModListAddition>();
        AddBeforeLogSync(mods, context.Config.CurrentPackageId(context.Root), "current_project", additions);
        AddBeforeLogSync(mods, "trigger.logsyncmod", "logsync_tooling", additions);

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

    private static void AddBeforeLogSync(List<string> mods, string? packageId, string reason, List<ModListAddition> additions)
    {
        if (string.IsNullOrWhiteSpace(packageId) || mods.Contains(packageId, StringComparer.OrdinalIgnoreCase)) return;
        var index = mods.FindIndex(x => x.Equals("trigger.logsyncmod", StringComparison.OrdinalIgnoreCase));
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
    public static string SyncLogPath(ProjectContext context) => Path.Combine(context.Root, ".tools", "test", "精选日志.log");
    public static string AutoTestOutputPath(ProjectContext context) => Path.Combine(context.Root, ".tools", "test", "AutoTestOutput");

    public static async Task<bool> UpdateAsync(ProjectContext context, string mode, ConfigTransaction? transaction = null)
    {
        var path = GameEnvironment.LogSyncSettingsPath();
        if (!File.Exists(path)) throw new FileNotFoundException("找不到 LogSyncMod 设置文件", path);

        var document = XDocument.Load(path);
        var settings = document.Root?.DescendantsAndSelf().FirstOrDefault(x => x.Name.LocalName.Equals("ModSettings", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("LogSync 设置缺少 ModSettings 节点");
        var changed = false;
        changed |= Set(settings, "logPath", SyncLogPath(context));
        changed |= SetList(settings, "keywords", context.Config.LogKeywords);

        var auto = mode is "auto" or "xml";
        changed |= Set(settings, "autoTestEnabled", auto ? "True" : "False");
        changed |= Set(settings, "autoTestTargetPackageId", auto ? context.Config.CurrentPackageId(context.Root)?.ToLowerInvariant() ?? "" : "");
        changed |= Set(settings, "autoTestOutputPath", AutoTestOutputPath(context));
        changed |= Set(settings, "autoTestMode", mode == "xml" ? "XmlDriven" : "Automatic");
        changed |= Set(settings, "autoTestScope", "Standard");
        changed |= Set(settings, "autoTestCaptureScreenshots", "False");
        changed |= Set(settings, "autoTestPreventPause", "True");
        changed |= Set(settings, "autoTestUseFastSpeed", "True");

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

    public static AutoResultValidation Validate(string outputPath, string? expectedPackageId, IReadOnlyDictionary<string, AutoResultFingerprint> snapshot)
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
                return new AutoResultValidation(true, "", "", new { path, packageId, executed, planned });
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
        return Failure("AUTO_RESULT_MISMATCH", "本轮结果不属于当前项目", new { expectedPackageId });
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
