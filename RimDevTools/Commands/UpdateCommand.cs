using System.Text.Json;

namespace RimDevTools;

internal static class UpdateCommand
{
    internal static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var command = CliApplication.CommandPath(args);
        var check = CliArgs.Has(args, "--check");
        var dryRun = CliArgs.Has(args, "--dry-run");
        if (check && dryRun) return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "INVALID_ARGUMENT", "--check 与 --dry-run 不能同时使用");
        var installation = UpdateInstallation.Locate();
        if (installation is null) return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "UPDATE_INSTALLATION_UNRECOGNIZED", "请在正式解压安装的 .tools/RimDevTools.exe 中使用更新命令；源码运行或缺少可信清单的旧安装需先手动安装一次新版。");
        if (command == "update status")
        {
            var state = installation.ReadState();
            var observed = state?.Status ?? "none";
            if (state is { Status: "scheduled" or "applying" }
                && !UpdateInstallation.IsProcessAlive(state.ParentPid, state.ParentStartedUtcTicks)
                && !UpdateInstallation.IsProcessAlive(state.HelperPid, state.HelperStartedUtcTicks)) observed = "recoveryRequired";
            return CliResult.WriteSuccess(command, installation.Root, new
            {
                status = observed, storedStatus = state?.Status, currentVersion = installation.CurrentVersion,
                targetVersion = state?.TargetVersion, transactionId = state?.TransactionId, error = state?.Error,
                recoveryCommand = observed == "recoveryRequired" ? state?.RecoveryCommand : null,
                artifacts = File.Exists(installation.StatePath) ? new[] { installation.StatePath } : []
            }, observed == "succeeded" ? ["工具磁盘文件已更新，请让 AI 重新读取 rimdevtools skill；游戏内工具 Mod 仍由 mods install-tools 管理。"] : null);
        }

        ToolUpdatePlan? plan = null;
        string? directory = null;
        var scheduled = false;
        try
        {
            var release = await ToolReleaseSource.LatestAsync(cancellationToken);
            var available = ToolReleaseSource.ParseVersion(release.Version) > ToolReleaseSource.ParseVersion(installation.CurrentVersion);
            if (check || !available)
                return CliResult.WriteSuccess(command, installation.Root, new { status = available ? "updateAvailable" : "upToDate", currentVersion = installation.CurrentVersion, latestVersion = release.Version, available });
            if (installation.ReadState()?.BlocksBusiness == true) throw new ToolInstallationBusyException("已有更新等待完成或恢复，请运行 update status");
            var id = Guid.NewGuid().ToString("N");
            directory = Path.Combine(installation.Data, "tool-update-" + id);
            PathSafety.EnsureNoReparsePoints(installation.Root, directory);
            Directory.CreateDirectory(directory);
            await ToolReleaseSource.DownloadAsync(release, Path.Combine(directory, "package.zip"), cancellationToken);
            plan = ToolUpdatePlan.Prepare(installation, release, id);
            if (dryRun)
                return CliResult.WriteSuccess(command, installation.Root, new
                {
                    dryRun = true, status = "ready", currentVersion = plan.CurrentVersion, targetVersion = plan.TargetVersion,
                    changes = plan.Changes.Select(c => new { path = c.Path, action = c.OldHash is null ? "add" : c.NewHash is null ? "remove" : "replace" }),
                    configuration = plan.Changes.Any(c => c.Path == "mod_config.jsonc") ? "add_missing_existing_project_mode_false" : "preserved"
                });
            var state = await ToolUpdateTransaction.ScheduleAsync(installation, plan, cancellationToken);
            scheduled = true;
            return CliResult.WriteSuccess(command, installation.Root, new
            {
                status = state.Status, currentVersion = state.CurrentVersion, targetVersion = state.TargetVersion,
                transactionId = state.TransactionId, artifacts = new[] { installation.StatePath, plan.JournalPath },
                nextCommand = "RimDevTools.exe update status"
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            if (!check && !dryRun)
            {
                using var gate = installation.TryGate();
                if (gate is not null && installation.ReadState()?.BlocksBusiness != true)
                {
                    var state = installation.ReadState();
                    if (plan is null || state?.TransactionId != plan.TransactionId)
                        state = new UpdateState { CurrentVersion = installation.CurrentVersion, TargetVersion = plan?.TargetVersion ?? "", TransactionId = plan?.TransactionId ?? "", TransactionDirectory = plan is null ? "" : plan.DirectoryPath };
                    state.Status = "failed";
                    state.Error = ex.Message;
                    installation.WriteState(state);
                }
            }
            return CliResult.WriteFailure(command, ex is ToolInstallationBusyException ? ExitCodes.Busy : ExitCodes.TaskFailed, check ? "UPDATE_CHECK_FAILED" : ex is ToolUpdateConflictException ? "UPDATE_CONFLICT" : ex is ToolInstallationBusyException ? "TOOL_UPDATE_BUSY" : "UPDATE_FAILED", ex.Message, installation.Root,
                new { conflicts = (ex as ToolUpdateConflictException)?.Files, artifacts = File.Exists(installation.StatePath) ? new[] { installation.StatePath } : [] });
        }
        finally
        {
            if (!scheduled && directory is not null && Directory.Exists(directory) && !File.Exists(Path.Combine(directory, "journal.json")))
            {
                try { DeletePrepared(installation, directory); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Console.Error.WriteLine("更新暂存清理未完成: " + ex.Message); }
            }
        }
    }

    internal static void DeletePrepared(UpdateInstallation installation, string directory)
    {
        var full = Path.GetFullPath(directory);
        if (Path.GetDirectoryName(full) != installation.Data || !Path.GetFileName(full).StartsWith("tool-update-", StringComparison.Ordinal)
            || !Guid.TryParseExact(Path.GetFileName(full)[12..], "N", out _)) throw new IOException("清理路径不属于工具更新事务");
        CheckTree(full);
        Directory.Delete(full, true);
    }

    private static void CheckTree(string directory)
    {
        if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("更新清理拒绝 Reparse Point");
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if (attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("更新清理拒绝 Reparse Point");
            if (attributes.HasFlag(FileAttributes.Directory)) CheckTree(entry);
        }
    }
}

internal sealed record UpdateCheckCache(DateTimeOffset NextCheckUtc, string? LatestVersion);

internal static class ToolStartupUpdateCheck
{
    internal static async Task RunAsync(UpdateInstallation installation, CancellationToken cancellationToken)
    {
        try
        {
            UpdateCheckCache? cache = null;
            try
            {
                if (File.Exists(installation.CachePath)) cache = JsonSerializer.Deserialize<UpdateCheckCache>(File.ReadAllText(installation.CachePath), JsonDefaults.Storage);
                if (cache?.LatestVersion is { } cachedVersion) _ = ToolReleaseSource.ParseVersion(cachedVersion);
            }
            catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or FormatException) { cache = null; }
            if (cache is null || cache.NextCheckUtc <= DateTimeOffset.UtcNow)
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                budget.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    var release = await ToolReleaseSource.LatestAsync(budget.Token);
                    cache = new(DateTimeOffset.UtcNow.AddHours(24), release.Version);
                }
                catch { cache = new(DateTimeOffset.UtcNow.AddHours(1), cache?.LatestVersion); }
                AtomicFile.WriteAllText(installation.CachePath, JsonSerializer.Serialize(cache, JsonDefaults.Storage));
            }
            if (cache.LatestVersion is { } version && ToolReleaseSource.ParseVersion(version) > ToolReleaseSource.ParseVersion(installation.CurrentVersion))
                Console.Error.WriteLine($"RimDevTools {version} 可用；执行 update 安装更新。");
        }
        catch (Exception) { /* Startup checks never alter the business command's result. */ }
    }
}
