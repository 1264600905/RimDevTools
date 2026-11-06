using System.Diagnostics;
using System.Text.Json;

namespace RimDevTools;

internal static class ToolUpdateTransaction
{
    internal static Action<int>? AfterWriteOverride;

    internal static async Task<UpdateState> ScheduleAsync(UpdateInstallation installation, ToolUpdatePlan plan, CancellationToken cancellationToken, Process? parentOverride = null)
    {
        using var gate = installation.TryGate() ?? throw new ToolInstallationBusyException("工具安装繁忙，请稍后重试");
        if (installation.ReadState()?.BlocksBusiness == true || installation.HasBusinessLeases()) throw new ToolInstallationBusyException("工具安装仍有业务命令或未完成更新");
        plan.Validate();
        CheckOriginals(plan);
        plan.Save();
        using var ownProcess = Process.GetCurrentProcess();
        var parent = parentOverride ?? ownProcess;
        var helperDirectory = Path.Combine(plan.DirectoryPath, "helper");
        Directory.CreateDirectory(helperDirectory);
        var helper = Path.Combine(helperDirectory, "RimDevTools.UpdateHelper.exe");
        File.Copy(installation.Executable, helper, false);
        var state = new UpdateState
        {
            Status = "scheduled", TransactionId = plan.TransactionId, TransactionDirectory = plan.DirectoryPath,
            CurrentVersion = plan.CurrentVersion, TargetVersion = plan.TargetVersion,
            ParentPid = parent.Id, ParentStartedUtcTicks = parent.StartTime.ToUniversalTime().Ticks,
            RecoveryCommand = $"\"{helper}\" --internal-update-recover \"{plan.JournalPath}\""
        };
        installation.WriteState(state);
        try
        {
            var start = new ProcessStartInfo(helper)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = plan.DirectoryPath
            };
            start.ArgumentList.Add("--internal-update-helper");
            start.ArgumentList.Add(plan.JournalPath);
            using var child = Process.Start(start) ?? throw new IOException("无法启动更新助手");
            var ready = Path.Combine(plan.DirectoryPath, "helper-ready.json");
            var stopwatch = Stopwatch.StartNew();
            while (!File.Exists(ready))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (child.HasExited || stopwatch.Elapsed > TimeSpan.FromSeconds(10)) throw new IOException("更新助手未确认交接");
                await Task.Delay(50, cancellationToken);
            }
            using var readyJson = JsonDocument.Parse(File.ReadAllText(ready));
            state.HelperPid = readyJson.RootElement.GetProperty("pid").GetInt32();
            state.HelperStartedUtcTicks = readyJson.RootElement.GetProperty("startedUtcTicks").GetInt64();
            if (state.HelperPid != child.Id || !UpdateInstallation.IsProcessAlive(state.HelperPid, state.HelperStartedUtcTicks)) throw new IOException("更新助手身份验证失败");
            installation.WriteState(state);
            return state;
        }
        catch (Exception ex)
        {
            state.Status = "failed";
            state.Error = ex.Message;
            plan.Outcome = "failed";
            plan.Save();
            installation.WriteState(state);
            throw;
        }
    }

    internal static async Task<int> RunHelperAsync(string journalPath, bool recover)
    {
        UpdateInstallation? installation = null;
        UpdateState? state = null;
        ToolUpdatePlan? plan = null;
        var ownsTransaction = false;
        try
        {
            plan = JsonSerializer.Deserialize<ToolUpdatePlan>(File.ReadAllText(journalPath), JsonDefaults.Storage) ?? throw new InvalidDataException("更新事务为空");
            plan.Validate();
            if (!Path.GetFullPath(journalPath).Equals(plan.JournalPath, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFullPath(Environment.ProcessPath!).Equals(Path.Combine(plan.DirectoryPath, "helper", "RimDevTools.UpdateHelper.exe"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("助手与更新事务的位置不匹配");
            installation = new(plan.Root);
            state = installation.ReadState() ?? throw new InvalidDataException("更新状态不存在");
            if (state.TransactionId != plan.TransactionId) throw new InvalidDataException("更新事务已经改变");
            using var current = Process.GetCurrentProcess();
            if (!recover)
            {
                AtomicFile.WriteAllText(Path.Combine(plan.DirectoryPath, "helper-ready.json"), JsonSerializer.Serialize(new { pid = current.Id, startedUtcTicks = current.StartTime.ToUniversalTime().Ticks }));
                var wait = Stopwatch.StartNew();
                while (UpdateInstallation.IsProcessAlive(state.ParentPid, state.ParentStartedUtcTicks))
                {
                    if (wait.Elapsed > TimeSpan.FromMinutes(2)) throw new IOException("原进程在交接后仍未退出，未进行替换");
                    await Task.Delay(50);
                }
            }
            using var gate = installation.TryGate() ?? throw new IOException("更新恢复/提交门禁繁忙");
            state = installation.ReadState() ?? throw new InvalidDataException("更新状态不存在");
            if (state.TransactionId != plan.TransactionId) throw new InvalidDataException("更新事务已经改变");
            if (recover)
            {
                if (!state.BlocksBusiness) return 0;
                if (UpdateInstallation.IsProcessAlive(state.ParentPid, state.ParentStartedUtcTicks)
                    || state.HelperPid != current.Id && UpdateInstallation.IsProcessAlive(state.HelperPid, state.HelperStartedUtcTicks)
                    || installation.HasBusinessLeases()) return ExitCodes.Busy;
                ownsTransaction = true;
                RollBack(plan);
                state.Status = "rolledBack";
                state.Error = "已按事务恢复原安装";
                plan.Outcome = state.Status;
                plan.Save();
                installation.WriteState(state);
                return 0;
            }
            if (state.Status != "scheduled") return ExitCodes.TaskFailed;
            if (installation.HasBusinessLeases()) return ExitCodes.Busy;
            ownsTransaction = true;
            await ApplyAsync(installation, plan, state);
            return state.Status == "succeeded" ? 0 : ExitCodes.TaskFailed;
        }
        catch (Exception ex)
        {
            if (ownsTransaction && installation is not null && state is not null && plan is not null)
            {
                using var failureGate = installation.TryGate();
                var latest = installation.ReadState();
                if (failureGate is not null && latest?.TransactionId == plan.TransactionId && latest.BlocksBusiness)
                {
                    latest.Status = "recoveryRequired";
                    latest.Error = ex.Message;
                    installation.WriteState(latest);
                }
            }
            return ExitCodes.TaskFailed;
        }
    }

    internal static async Task ApplyAsync(UpdateInstallation installation, ToolUpdatePlan plan, UpdateState state)
    {
        var started = false;
        try
        {
            plan.Validate();
            CheckOriginals(plan);
            ToolPackageManifest.Load(plan.Root).VerifyFiles(plan.Root);
            ToolPackageManifest.Load(plan.Staged("")).VerifyFiles(plan.Staged(""));
            foreach (var change in plan.Changes.Where(c => c.OldHash is not null))
            {
                using (var probe = new FileStream(plan.Destination(change.Path), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                var backup = plan.Backup(change.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(plan.Destination(change.Path), backup, false);
                if (!ToolPackageManifest.Hash(backup).Equals(change.OldHash, StringComparison.OrdinalIgnoreCase)) throw new IOException("原文件在备份时发生变化");
            }
            state.Status = "applying";
            installation.WriteState(state);
            started = true;
            var index = 0;
            foreach (var change in plan.Changes)
            {
                CheckHash(plan.Destination(change.Path), change.OldHash);
                if (change.NewHash is not null) CheckHash(plan.Staged(change.Path), change.NewHash);
                change.Applied = true;
                plan.Save(); // Write intent first, so a crash after replacement remains recoverable.
                if (change.NewHash is null) File.Delete(plan.Destination(change.Path));
                else AtomicFile.WriteAllBytes(plan.Destination(change.Path), File.ReadAllBytes(plan.Staged(change.Path)));
                AfterWriteOverride?.Invoke(++index);
            }
            var installed = ToolPackageManifest.Load(plan.Root);
            installed.VerifyFiles(plan.Root);
            _ = JsoncConfigMigration.Prepare(File.ReadAllBytes(installation.Config), installed);
            var version = await ReadVersionAsync(installation.Executable, plan.Root);
            if (ToolReleaseSource.ParseVersion(version) != ToolReleaseSource.ParseVersion(plan.TargetVersion)) throw new IOException("更新后 EXE 版本验证失败");
            state.Status = "succeeded";
            state.Error = null;
        }
        catch (Exception ex)
        {
            state.Error = ex.Message;
            if (!started) state.Status = "failed";
            else
            {
                try { RollBack(plan); state.Status = "rolledBack"; }
                catch (Exception rollback) { state.Status = "recoveryRequired"; state.Error += "; 恢复失败: " + rollback.Message; }
            }
        }
        state.UpdatedUtc = DateTimeOffset.UtcNow;
        plan.Outcome = state.Status;
        plan.Save();
        installation.WriteState(state);
        CleanupCompleted(installation);
    }

    internal static void RollBack(ToolUpdatePlan plan)
    {
        plan.Validate();
        foreach (var change in plan.Changes.Where(c => c.Applied).Reverse())
        {
            var destination = plan.Destination(change.Path);
            if (Matches(destination, change.OldHash)) continue;
            if (!Matches(destination, change.NewHash)) throw new IOException($"恢复目标含未知修改: {change.Path}");
            if (change.OldHash is null) File.Delete(destination);
            else
            {
                CheckHash(plan.Backup(change.Path), change.OldHash);
                AtomicFile.WriteAllBytes(destination, File.ReadAllBytes(plan.Backup(change.Path)));
            }
        }
        ToolPackageManifest.Load(plan.Root).VerifyFiles(plan.Root);
        CheckHash(Path.Combine(plan.Root, "mod_config.jsonc"), plan.ConfigHash);
    }

    private static void CheckOriginals(ToolUpdatePlan plan)
    {
        CheckHash(Path.Combine(plan.Root, "mod_config.jsonc"), plan.ConfigHash);
        foreach (var change in plan.Changes) CheckHash(plan.Destination(change.Path), change.OldHash);
    }

    internal static bool Matches(string path, string? hash) => hash is null ? !File.Exists(path) && !Directory.Exists(path) : File.Exists(path) && ToolPackageManifest.Hash(path).Equals(hash, StringComparison.OrdinalIgnoreCase);

    internal static void CleanupCompleted(UpdateInstallation installation)
    {
        // Keep three completed transactions plus every incomplete/unrecognized transaction.
        // Cleanup failure never invalidates a successfully committed update.
        try
        {
            var current = installation.ReadState()?.TransactionDirectory;
            var completed = new List<string>();
            foreach (var directory in Directory.EnumerateDirectories(installation.Data, "tool-update-*"))
            {
                if (directory == current) continue;
                PathSafety.EnsureNoReparsePoints(installation.Root, directory);
                var journal = Path.Combine(directory, "journal.json");
                if (!File.Exists(journal)) continue;
                ToolUpdatePlan? old;
                try { old = JsonSerializer.Deserialize<ToolUpdatePlan>(File.ReadAllText(journal), JsonDefaults.Storage); }
                catch (JsonException) { continue; }
                if (old is not null && old.Root == installation.Root && old.DirectoryPath == directory && old.Outcome is "succeeded" or "failed" or "rolledBack") completed.Add(directory);
            }
            foreach (var directory in completed.OrderByDescending(Directory.GetCreationTimeUtc).Skip(2))
                UpdateCommand.DeletePrepared(installation, directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException) { }
    }
    private static void CheckHash(string path, string? hash)
    {
        if (!Matches(path, hash)) throw new IOException($"文件缺失、冲突或并发修改: {path}");
    }

    internal static async Task<string> ReadVersionAsync(string executable, string workingDirectory)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--version");
        using var process = Process.Start(start) ?? throw new IOException("无法运行更新后的工具");
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        var stdout = (await output).TrimStart('\uFEFF');
        _ = await error;
        if (process.ExitCode != 0) throw new IOException("更新后的工具无法运行");
        using var json = JsonDocument.Parse(stdout);
        return json.RootElement.GetProperty("rimDevToolsVersion").GetString() ?? throw new IOException("版本输出无效");
    }
}

internal sealed class ToolInstallationBusyException(string message) : IOException(message);
