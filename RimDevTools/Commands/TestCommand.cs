using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace RimDevTools;

public static class TestCommand
{
    public static async Task<int> SettingsAsync(string[] args)
    {
        var context = CliApplication.RequireProject("test settings", out var code); if (context is null) return code;
        var runId = Guid.NewGuid().ToString("N");
        using var gate = EnvironmentLock.TryAcquire(GameEnvironment.ConfigDirectory(), "test settings", context.Root, runId, out var busyInfo);
        if (gate is null) return CliResult.WriteFailure("test settings", ExitCodes.Busy, "ENVIRONMENT_BUSY", "RimWorld 环境正被占用", context.Root, new { busyInfo });
        try
        {
            var settings = await SettingsEditor.UpdateAsync(context, "quick", persistent: true);
            return CliResult.WriteSuccess("test settings", context.Root, new { runId, settingsPath = GameEnvironment.LogSyncSettingsPath(), changed = settings });
        }
        catch (Exception ex) { return CliResult.WriteFailure("test settings", ExitCodes.TaskFailed, "SETTINGS_UPDATE_FAILED", ex.Message, context.Root); }
    }

    public static async Task<int> RunAsync(string[] args)
    {
        var context = CliApplication.RequireProject("test run", out var code); if (context is null) return code;
        var mode = CliArgs.Value(args, "--mode")?.ToLowerInvariant() ?? "quick";
        if (mode is not ("quick" or "manual" or "auto" or "xml")) return CliResult.WriteFailure("test run", ExitCodes.InvalidArguments, "MODE_INVALID", $"不支持的测试模式: {mode}", context.Root);
        if (CliArgs.Has(args, "--deploy"))
        {
            var deployArgs = new List<string> { "deploy" };
            if (CliArgs.Has(args, "--sync-source")) deployArgs.Add("--sync-source");
            var previousSuppression = CliResult.SuppressOutput; CliResult.SuppressOutput = true;
            var deployCode = ExitCodes.Success;
            try
            {
                deployCode = await DeployCommand.RunAsync(deployArgs.ToArray());
            }
            finally { CliResult.SuppressOutput = previousSuppression; }
            if (deployCode != ExitCodes.Success) return CliResult.WriteFailure("test run", deployCode, "DEPLOY_FAILED", "测试前部署失败", context.Root);
        }
        var modsFileValue = CliArgs.Value(args, "--mods-file");
        var modsFile = modsFileValue is null ? null : Path.GetFullPath(Path.Combine(context.Root, modsFileValue));
        var mods = ModListParser.Load(context, modsFile, out var modError);
        if (mods is null) return CliResult.WriteFailure("test run", ExitCodes.InvalidArguments, modError!.Code, modError.Message, context.Root, modError.Details);
        var gamePath = Path.Combine(context.Config.RimWorldDir ?? "", "RimWorldWin64.exe");
        if (!File.Exists(gamePath)) return CliResult.WriteFailure("test run", ExitCodes.MissingDependency, "GAME_NOT_FOUND", $"找不到游戏可执行文件: {gamePath}", context.Root);
        var runId = Guid.NewGuid().ToString("N");
        var testStartedUtc = DateTime.UtcNow;
        using var gate = EnvironmentLock.TryAcquire(GameEnvironment.ConfigDirectory(), "test run", context.Root, runId, out var busyInfo);
        if (gate is null) return CliResult.WriteFailure("test run", ExitCodes.Busy, "ENVIRONMENT_BUSY", "RimWorld 环境正被占用", context.Root, new { busyInfo });
        if (!await ProcessUtil.WaitForRimWorldExitAsync(context.Config.Timeouts.ExistingGameExit, context.Config.Timeouts.PostExitDelay)) return CliResult.WriteFailure("test run", ExitCodes.Timeout, "GAME_EXIT_TIMEOUT", "等待已有 RimWorld 退出超时", context.Root);
        await TransactionRecovery.RecoverAsync(GameEnvironment.ConfigDirectory());
        await using var transaction = await ConfigTransaction.CreateAsync(GameEnvironment.ConfigDirectory(), runId, [GameEnvironment.ModsConfigPath(), GameEnvironment.LogSyncSettingsPath()]);
        Process? game = null;
        try
        {
            await ModListParser.WriteAsync(GameEnvironment.ModsConfigPath(), mods);
            await transaction.MarkWrittenAsync(GameEnvironment.ModsConfigPath());
            await SettingsEditor.UpdateAsync(context, mode, persistent: false);
            await transaction.MarkWrittenAsync(GameEnvironment.LogSyncSettingsPath());
            game = Process.Start(new ProcessStartInfo(gamePath, mode == "quick" ? "-popupwindow quicktest" : "-popupwindow") { WorkingDirectory = context.Config.RimWorldDir, UseShellExecute = false, CreateNoWindow = false });
            if (game is null) return CliResult.WriteFailure("test run", ExitCodes.GameFailed, "GAME_START_FAILED", "无法启动 RimWorld", context.Root);
            var signal = await WaitForModeAsync(mode, game, CliArgs.IntValue(args, "--timeout-seconds", context.Config.Timeouts.MaxWait), testStartedUtc, context.Root);
            if (!signal.ok) return CliResult.WriteFailure("test run", signal.exitCode, signal.code, signal.message, context.Root, new { runId, mode, mods });
            return CliResult.WriteSuccess("test run", context.Root, new { runId, mode, mods, signal = signal.message });
        }
        catch (Exception ex) { return CliResult.WriteFailure("test run", ExitCodes.GameFailed, "TEST_FAILED", ex.Message, context.Root, new { runId, mode }); }
        finally
        {
            if (game is not null && !game.HasExited && (mode != "manual" || CliArgs.Has(args, "--kill-on-timeout"))) try { game.Kill(true); } catch { }
        }
    }

    private static async Task<(bool ok, int exitCode, string code, string message)> WaitForModeAsync(string mode, Process game, int timeout, DateTime startedUtc, string projectRoot)
    {
        if (mode == "manual") { await game.WaitForExitAsync(); return (true, 0, "", "manual game exit"); }
        var log = GameEnvironment.PlayerLogPath(); var deadline = DateTime.UtcNow.AddSeconds(Math.Max(1, timeout)); var offset = File.Exists(log) ? new FileInfo(log).Length : 0;
        while (DateTime.UtcNow < deadline)
        {
            if (game.HasExited) return (false, ExitCodes.GameFailed, "GAME_EXITED_EARLY", "RimWorld 在收到测试完成信号前退出");
            if (File.Exists(log))
            {
                using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                stream.Seek(Math.Min(offset, stream.Length), SeekOrigin.Begin);
                using var reader = new StreamReader(stream, Encoding.UTF8, true);
                var text = await reader.ReadToEndAsync(); offset = stream.Position;
                if (mode == "quick" && text.Contains("[LogSyncMod] RUNTIME_READY_TO_CLOSE", StringComparison.Ordinal)) return (true, 0, "", "quick ready signal");
                if ((mode is "auto" or "xml") && text.Contains("[LogSyncAutoTest] COMPLETED", StringComparison.Ordinal))
                {
                    var validation = ValidateAutoResult(projectRoot, startedUtc);
                    return validation.ok ? (true, 0, "", $"{mode} completed signal") : (false, ExitCodes.GameFailed, validation.code, validation.message);
                }
            }
            await Task.Delay(500);
        }
        return (false, ExitCodes.Timeout, "TEST_TIMEOUT", $"{mode} 模式等待超时");
    }

    private static (bool ok, string code, string message) ValidateAutoResult(string projectRoot, DateTime startedUtc)
    {
        var outputRoot = Path.Combine(projectRoot, ".tools", "test", "AutoTestOutput");
        if (!Directory.Exists(outputRoot)) return (false, "AUTO_RESULT_MISSING", "未找到自动测试输出目录");
        var candidates = Directory.EnumerateFiles(outputRoot, "*_Result.json", SearchOption.TopDirectoryOnly).Where(x => File.GetLastWriteTimeUtc(x) >= startedUtc).OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
        if (candidates.Length == 0) return (false, "AUTO_RESULT_MISSING", "未找到本轮自动测试结果 JSON");
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(candidates[0]));
            if (document.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == System.Text.Json.JsonValueKind.False) return (false, "AUTO_RESULT_FAILED", "自动测试结果报告失败");
            return (true, "", "");
        }
        catch (Exception ex) { return (false, "AUTO_RESULT_INVALID", $"自动测试结果 JSON 无法解析: {ex.Message}"); }
    }
}

public static class ModListParser
{
    public static List<string>? Load(ProjectContext context, string? path, out CliError? error)
    {
        error = null;
        List<string> mods;
        if (path is null) mods = context.Config.MinActiveMods.ToList();
        else
        {
            if (!File.Exists(path)) { error = new("MODS_FILE_NOT_FOUND", $"ModList 文件不存在: {path}"); return null; }
            try
            {
                var doc = XDocument.Load(path); var active = doc.Root?.Element("activeMods");
                if (active is null) { error = new("ACTIVE_MODS_MISSING", "ModList XML 缺少 activeMods"); return null; }
                mods = active.Elements("li").Select(x => x.Value.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception ex) { error = new("MODS_XML_INVALID", ex.Message); return null; }
        }
        var package = context.Config.CurrentPackageId(context.Root);
        AddBeforeLogSync(mods, package); AddBeforeLogSync(mods, "trigger.logsyncmod");
        var missing = mods.Where(x => !IsResolvable(context, x)).ToArray();
        if (missing.Length > 0) { error = new("MOD_NOT_RESOLVED", "以下模组无法在游戏 Mods 或 Workshop 中解析", missing); return null; }
        return mods;
    }

    private static void AddBeforeLogSync(List<string> mods, string? package)
    {
        if (string.IsNullOrWhiteSpace(package) || mods.Contains(package, StringComparer.OrdinalIgnoreCase)) return;
        var index = mods.FindIndex(x => x.Equals("trigger.logsyncmod", StringComparison.OrdinalIgnoreCase)); if (index < 0) mods.Add(package); else mods.Insert(index, package);
    }

    private static bool IsResolvable(ProjectContext context, string package)
    {
        if (package.Equals(context.Config.CurrentPackageId(context.Root), StringComparison.OrdinalIgnoreCase)) return true;
        if (package.StartsWith("ludeon.rimworld", StringComparison.OrdinalIgnoreCase)) return true;
        if (package.Equals("brrainz.harmony", StringComparison.OrdinalIgnoreCase) && !Directory.Exists(context.Config.RimWorldDir)) return false;
        var roots = new[] { Path.Combine(context.Config.RimWorldDir ?? "", "Mods") }.Concat(context.Config.WorkshopPaths).Where(Directory.Exists);
        foreach (var root in roots) foreach (var about in Directory.EnumerateFiles(root, "About.xml", SearchOption.AllDirectories)) try { if (XDocument.Load(about).Root?.Element("packageId")?.Value.Trim().Equals(package, StringComparison.OrdinalIgnoreCase) == true) return true; } catch { }
        return false;
    }

    public static async Task WriteAsync(string path, IReadOnlyList<string> mods)
    {
        var doc = File.Exists(path) ? XDocument.Load(path) : new XDocument(new XElement("ModsConfigData"));
        var root = doc.Root ?? throw new InvalidDataException("ModsConfig.xml 缺少根节点"); var active = root.Element("activeMods") ?? new XElement("activeMods");
        if (active.Parent is null) root.Add(active); active.RemoveNodes(); foreach (var mod in mods) active.Add(new XElement("li", mod));
        await using var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None); doc.Save(stream); await stream.FlushAsync(); stream.Close(); File.Move(path + ".tmp", path, true);
    }
}

public static class SettingsEditor
{
    public static Task<bool> UpdateAsync(ProjectContext context, string mode, bool persistent)
    {
        var path = GameEnvironment.LogSyncSettingsPath(); if (!File.Exists(path)) throw new FileNotFoundException("找不到 LogSyncMod 设置文件", path);
        var doc = XDocument.Load(path); var root = doc.Root ?? throw new InvalidDataException("LogSync 设置缺少根节点"); var settings = root.Descendants("ModSettings").FirstOrDefault() ?? root;
        var changed = false; changed |= Set(settings, "logPath", Path.Combine(context.Root, ".tools", "test", "精选日志.log"));
        var keywords = settings.Element("keywords") ?? new XElement("keywords"); if (keywords.Parent is null) settings.Add(keywords); keywords.RemoveNodes(); foreach (var keyword in context.Config.LogKeywords.Where(x => !string.IsNullOrWhiteSpace(x))) keywords.Add(new XElement("li", keyword)); changed = true;
        changed |= Set(settings, "autoTestEnabled", mode is "auto" or "xml" ? "True" : "False"); changed |= Set(settings, "autoTestMode", mode == "xml" ? "XmlDriven" : "Automatic");
        if (changed) AtomicFile.WriteAllText(path, doc.ToString(SaveOptions.None));
        return Task.FromResult(changed);
    }
    private static bool Set(XElement parent, string name, string value) { var node = parent.Element(name) ?? new XElement(name); if (node.Parent is null) parent.Add(node); if (node.Value == value) return false; node.Value = value; return true; }
}

public sealed class ConfigTransaction : IAsyncDisposable
{
    private readonly string _dir; private readonly List<BackupEntry> _entries;
    private ConfigTransaction(string dir, List<BackupEntry> entries) { _dir = dir; _entries = entries; }
    public static Task<ConfigTransaction> CreateAsync(string configDir, string runId, IEnumerable<string> paths)
    {
        var dir = Path.Combine(configDir, "RimDevToolsTransactions", runId); Directory.CreateDirectory(dir); var entries = new List<BackupEntry>();
        foreach (var path in paths)
        {
            var exists = File.Exists(path); var original = exists ? File.ReadAllBytes(path) : []; var backup = Path.Combine(dir, Path.GetFileName(path) + ".original"); if (exists) File.WriteAllBytes(backup, original);
            entries.Add(new BackupEntry { Path = path, Backup = backup, Exists = exists, OriginalHash = exists ? Convert.ToHexString(SHA256.HashData(original)) : "" });
        }
        File.WriteAllText(Path.Combine(dir, "transaction.json"), System.Text.Json.JsonSerializer.Serialize(entries));
        return Task.FromResult(new ConfigTransaction(dir, entries));
    }
    public Task MarkWrittenAsync(string path)
    {
        var entry = _entries.SingleOrDefault(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException($"路径不属于当前事务: {path}");
        entry.AppliedHash = File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : "";
        File.WriteAllText(Path.Combine(_dir, "transaction.json"), System.Text.Json.JsonSerializer.Serialize(_entries));
        return Task.CompletedTask;
    }
    public ValueTask DisposeAsync() { foreach (var entry in _entries) { if (entry.Exists) File.Copy(entry.Backup, entry.Path, true); else if (File.Exists(entry.Path)) File.Delete(entry.Path); } if (Directory.Exists(_dir)) Directory.Delete(_dir, true); return ValueTask.CompletedTask; }
    private sealed class BackupEntry { public string Path { get; set; } = ""; public string Backup { get; set; } = ""; public bool Exists { get; set; } public string OriginalHash { get; set; } = ""; public string? AppliedHash { get; set; } }
}

public static class TransactionRecovery
{
    public static Task RecoverAsync(string configDir)
    {
        var root = Path.Combine(configDir, "RimDevToolsTransactions"); if (!Directory.Exists(root)) return Task.CompletedTask;
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var file = Path.Combine(dir, "transaction.json"); if (!File.Exists(file)) continue;
            try
            {
                var entries = System.Text.Json.JsonSerializer.Deserialize<List<RecoveryEntry>>(File.ReadAllText(file)) ?? [];
                foreach (var entry in entries)
                {
                    var currentHash = File.Exists(entry.Path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry.Path))) : "";
                    var expectedHash = entry.AppliedHash ?? entry.OriginalHash;
                    if (currentHash != expectedHash) throw new IOException($"检测到用户修改，拒绝自动恢复: {entry.Path}");
                    if (entry.Exists) File.Copy(entry.Backup, entry.Path, true); else if (File.Exists(entry.Path)) File.Delete(entry.Path);
                }
                Directory.Delete(dir, true);
            }
            catch (Exception ex) { Console.Error.WriteLine($"事务恢复失败: {ex.Message}"); }
        }
        return Task.CompletedTask;
    }
    private sealed class RecoveryEntry { public string Path { get; set; } = ""; public string Backup { get; set; } = ""; public bool Exists { get; set; } public string OriginalHash { get; set; } = ""; public string? AppliedHash { get; set; } }
}
