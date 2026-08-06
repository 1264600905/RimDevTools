using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RimDevTools;

public sealed class DataExportRequest
{
    public int SchemaVersion { get; set; } = 1;
    public string RunId { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
    public string OutputPath { get; set; } = "";
    public bool AutoExit { get; set; } = true;
    public string InputModsHash { get; set; } = "";
    public string RequestedModsHash { get; set; } = "";
    public string ProjectPackageId { get; set; } = "";
}

public sealed class DataExportResult
{
    public int SchemaVersion { get; set; }
    public string RunId { get; set; } = "";
    public bool Ok { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string DatabasePath { get; set; } = "";
    public long DatabaseBytes { get; set; }
    public string ExporterVersion { get; set; } = "";
    public string GameVersion { get; set; } = "";
    public string Language { get; set; } = "";
    public string EffectiveModsHash { get; set; } = "";
    public DateTimeOffset CompletedUtc { get; set; }
}

public static class DataCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static Task<int> StatusAsync(string[] args)
    {
        const string command = "data status";
        var context = CliApplication.RequireProject(command, out var code);
        if (context is null) return Task.FromResult(code);
        var resolved = ResolveOutputPath(context, null, out var error);
        if (resolved is null)
            return Task.FromResult(CliResult.WriteFailure(command, ExitCodes.InvalidArguments, error!.Code, error.Message, context.Root));
        var modList = ModListParser.ResolveData(context, null, out _);
        var status = DatabaseInspector.Inspect(
            resolved,
            "project_config",
            context.Config.GameVersion,
            context.Config.CurrentPackageId(context.Root),
            modList is null ? null : HashLines(modList.Mods));
        return Task.FromResult(CliResult.WriteSuccess(command, context.Root, status));
    }

    public static async Task<int> RefreshAsync(string[] args, CancellationToken cancellationToken = default)
    {
        const string command = "data refresh";
        var context = CliApplication.RequireProject(command, out var code);
        if (context is null) return code;
        if (!CliArgs.TryPositiveInt(args, "--timeout-seconds", context.Config.Timeouts.MaxWait, out var timeoutSeconds, out var timeoutError))
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "TIMEOUT_INVALID", timeoutError!, context.Root);

        var outputPath = ResolveOutputPath(context, CliArgs.Value(args, "--output"), out var outputError);
        if (outputPath is null)
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, outputError!.Code, outputError.Message, context.Root);
        var gamePath = Path.Combine(context.Config.RimWorldDir ?? "", "RimWorldWin64.exe");
        if (!File.Exists(gamePath))
            return CliResult.WriteFailure(command, ExitCodes.MissingDependency, "GAME_NOT_FOUND", $"找不到游戏可执行文件: {gamePath}", context.Root);
        var rimSearcherPath = ComponentLocator.RimSearcherPath();
        if (!File.Exists(rimSearcherPath))
            return CliResult.WriteFailure(command, ExitCodes.MissingDependency, "RIMSEARCHER_CLI_MISSING", $"找不到配套 rimsearcher.exe: {rimSearcherPath}", context.Root);

        var modsFileValue = CliArgs.Value(args, "--mods-file");
        var modsFile = modsFileValue is null ? null : Path.GetFullPath(modsFileValue, context.Root);
        var modList = ModListParser.ResolveData(context, modsFile, out var modError);
        if (modList is null)
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, modError!.Code, modError.Message, context.Root, modError.Details);

        try
        {
            var outputDirectory = Path.GetDirectoryName(outputPath)!;
            PathSafety.EnsureNoReparsePoints(outputDirectory, outputPath);
        }
        catch (Exception ex)
        {
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "DATABASE_PATH_UNSAFE", ex.Message, context.Root, new { outputPath });
        }

        var runId = Guid.NewGuid().ToString("N");
        var configDirectory = GameEnvironment.ConfigDirectory();
        var requestPath = Path.Combine(configDirectory, "RimDevTools.ExportRequest.json");
        var processingPath = Path.Combine(configDirectory, $"RimDevTools.ExportRequest.processing.{runId}.json");
        var resultPath = Path.Combine(configDirectory, $"RimDevTools.ExportResult.{runId}.json");
        using var gate = EnvironmentLock.TryAcquire(configDirectory, command, context.Root, runId, out var busyInfo);
        if (gate is null)
            return CliResult.WriteFailure(command, ExitCodes.Busy, "ENVIRONMENT_BUSY", "RimWorld 环境正被占用", context.Root, new { busyInfo });
        if (!await ProcessUtil.WaitForRimWorldExitAsync(context.Config.Timeouts.ExistingGameExit, context.Config.Timeouts.PostExitDelay, cancellationToken))
            return CliResult.WriteFailure(command, ExitCodes.Timeout, "GAME_EXIT_TIMEOUT", "等待已有 RimWorld 退出超时", context.Root);
        var recovery = await TransactionRecovery.RecoverAsync(configDirectory);
        if (!recovery.Ok)
            return CliResult.WriteFailure(command, ExitCodes.TaskFailed, recovery.Error!.Code, recovery.Error.Message, context.Root, recovery.Error.Details);
        var orphanedProcessing = Directory.Exists(configDirectory)
            ? Directory.EnumerateFiles(configDirectory, "RimDevTools.ExportRequest.processing.*.json").ToArray()
            : [];
        if (File.Exists(requestPath) || orphanedProcessing.Length > 0)
            return CliResult.WriteFailure(command, ExitCodes.TaskFailed, "EXPORT_REQUEST_CONFLICT", "存在不属于可恢复事务的导出请求，拒绝覆盖", context.Root, new { requestPath, orphanedProcessing });

        var requestedModsHash = HashLines(modList.Mods);
        var inputModsHash = modsFile is null ? HashLines(context.Config.MinActiveMods) : HashBytes(File.ReadAllBytes(modsFile));
        var request = new DataExportRequest
        {
            RunId = runId,
            CreatedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds),
            OutputPath = outputPath,
            InputModsHash = inputModsHash,
            RequestedModsHash = requestedModsHash,
            ProjectPackageId = context.Config.CurrentPackageId(context.Root) ?? ""
        };

        ConfigTransaction? transaction = null;
        Process? game = null;
        DataExportResult? exportResult = null;
        var completionLogSeen = false;
        var failureLogSeen = false;
        var outcomeCode = "";
        var outcomeMessage = "";
        var outcomeExitCode = ExitCodes.Success;
        var logOffset = File.Exists(GameEnvironment.PlayerLogPath()) ? new FileInfo(GameEnvironment.PlayerLogPath()).Length : 0;
        var logBuffer = "";
        try
        {
            transaction = await ConfigTransaction.CreateAsync(configDirectory, runId, [GameEnvironment.ModsConfigPath(), requestPath, processingPath]);
            await transaction.WriteAllTextAsync(GameEnvironment.ModsConfigPath(), ModListParser.Render(GameEnvironment.ModsConfigPath(), modList.Mods));
            await transaction.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request, JsonOptions));

            var startInfo = new ProcessStartInfo(gamePath)
            {
                WorkingDirectory = context.Config.RimWorldDir,
                UseShellExecute = false,
                CreateNoWindow = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-popupwindow");
            game = new Process { StartInfo = startInfo };
            game.OutputDataReceived += (_, eventArgs) => { if (eventArgs.Data is not null) Console.Error.WriteLine(eventArgs.Data); };
            game.ErrorDataReceived += (_, eventArgs) => { if (eventArgs.Data is not null) Console.Error.WriteLine(eventArgs.Data); };
            if (!game.Start()) throw new InvalidOperationException("无法启动 RimWorld");
            game.BeginOutputReadLine();
            game.BeginErrorReadLine();

            var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (exportResult is null && File.Exists(resultPath)) exportResult = TryReadResult(resultPath, runId);
                var read = await ReadNewLogAsync(GameEnvironment.PlayerLogPath(), logOffset, cancellationToken);
                logOffset = read.Offset;
                logBuffer = (logBuffer + read.Text);
                if (logBuffer.Length > 32768) logBuffer = logBuffer[^32768..];
                completionLogSeen = logBuffer.Contains($"[RimSearcher] EXPORT_COMPLETE runId={runId}", StringComparison.Ordinal);
                failureLogSeen = logBuffer.Contains($"[RimSearcher] EXPORT_FAILED runId={runId}", StringComparison.Ordinal);

                if (exportResult is not null && (completionLogSeen || failureLogSeen)) break;
                if (game.HasExited && exportResult is null)
                {
                    outcomeExitCode = ExitCodes.GameFailed;
                    outcomeCode = "GAME_EXITED_EARLY";
                    outcomeMessage = "RimWorld 在生成数据库导出结果前退出";
                    break;
                }
                await Task.Delay(500, cancellationToken);
            }

            if (outcomeExitCode == ExitCodes.Success && (exportResult is null || (!completionLogSeen && !failureLogSeen)))
            {
                outcomeExitCode = ExitCodes.Timeout;
                outcomeCode = "DATA_REFRESH_TIMEOUT";
                outcomeMessage = "等待数据库导出结果超时";
            }
            else if (outcomeExitCode == ExitCodes.Success && exportResult is not null && exportResult.Ok != completionLogSeen)
            {
                outcomeExitCode = ExitCodes.GameFailed;
                outcomeCode = "EXPORT_SIGNAL_MISMATCH";
                outcomeMessage = "DataMod 结果与专用完成/失败日志不一致";
            }
            else if (outcomeExitCode == ExitCodes.Success && exportResult is { Ok: false })
            {
                outcomeExitCode = ExitCodes.GameFailed;
                outcomeCode = exportResult.ErrorCode ?? "EXPORT_FAILED";
                outcomeMessage = exportResult.ErrorMessage ?? "DataMod 导出失败";
            }
        }
        catch (OperationCanceledException)
        {
            outcomeExitCode = ExitCodes.Cancelled;
            outcomeCode = "CANCELLED";
            outcomeMessage = "操作已取消";
        }
        catch (Exception ex)
        {
            outcomeExitCode = ExitCodes.GameFailed;
            outcomeCode = "DATA_REFRESH_FAILED";
            outcomeMessage = ex.Message;
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
                catch (Exception ex) { Console.Error.WriteLine($"终止 RimWorld 失败: {ex.Message}"); }
                game.Dispose();
            }
        }

        if (transaction is not null)
        {
            try { await transaction.RestoreAsync(force: true); }
            catch (Exception ex)
            {
                return CliResult.WriteFailure(command, ExitCodes.TaskFailed, "TRANSACTION_RESTORE_FAILED", ex.Message, context.Root, new { runId, outputPath });
            }
        }
        if (outcomeExitCode != ExitCodes.Success)
            return CliResult.WriteFailure(command, outcomeExitCode, outcomeCode, outcomeMessage, context.Root, new { runId, outputPath, resultPath });

        var validationError = ValidateRefreshResult(
            exportResult!, outputPath, runId, inputModsHash, requestedModsHash,
            context.Config.GameVersion, context.Config.CurrentPackageId(context.Root), out var status);
        if (validationError is not null)
            return CliResult.WriteFailure(command, ExitCodes.GameFailed, validationError.Code, validationError.Message, context.Root, validationError.Details);
        return CliResult.WriteSuccess(command, context.Root, new
        {
            databasePath = outputPath,
            runId,
            mods = modList.Mods,
            additions = modList.Additions,
            requestPath,
            processingPath,
            resultPath,
            export = exportResult,
            database = status,
            recoveredTransaction = recovery.Found
        });
    }

    private static string? ResolveOutputPath(ProjectContext context, string? overridePath, out CliError? error)
    {
        error = null;
        var value = overridePath ?? context.Config.RimSearcherDbPath;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = new CliError("DATABASE_PATH_MISSING", "需要 rimsearcher_db_path 或 --output");
            return null;
        }
        return Path.GetFullPath(value, context.Root);
    }

    private static DataExportResult? TryReadResult(string path, string runId)
    {
        try
        {
            var result = JsonSerializer.Deserialize<DataExportResult>(File.ReadAllText(path), JsonOptions);
            return result?.SchemaVersion == 1 && result.RunId == runId ? result : null;
        }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }

    private static CliError? ValidateRefreshResult(
        DataExportResult result,
        string outputPath,
        string runId,
        string inputModsHash,
        string requestedModsHash,
        string? configuredGameVersion,
        string? projectPackageId,
        out DatabaseStatus status)
    {
        status = DatabaseInspector.Inspect(outputPath, "refresh_output", configuredGameVersion, projectPackageId, requestedModsHash);
        if (result.RunId != runId) return new CliError("EXPORT_RUN_ID_MISMATCH", "导出结果 runId 不匹配");
        if (string.IsNullOrWhiteSpace(result.DatabasePath) || !Path.GetFullPath(result.DatabasePath).Equals(Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            return new CliError("EXPORT_PATH_MISMATCH", "导出结果数据库路径与请求不一致", new { result.DatabasePath, outputPath });
        if (!status.Exists || !status.CanOpen || status.Integrity != "ok") return new CliError("DATABASE_INVALID", "导出数据库不存在、无法打开或完整性检查失败", status);
        if (status.Size != result.DatabaseBytes) return new CliError("DATABASE_SIZE_MISMATCH", "数据库大小与结果文件不一致", new { status.Size, result.DatabaseBytes });
        if (!status.VersionMatches) return new CliError("VERSION_MISMATCH", "数据库 user_version 与 rimsearcher.exe 不匹配", status);
        if (status.Metadata is null) return new CliError("EXPORT_METADATA_MISSING", "数据库缺少 export_metadata", status);
        var rimSearcherVersion = status.RimSearcherVersion;
        if (!VersionsEqual(result.ExporterVersion, rimSearcherVersion) || !VersionsEqual(status.Metadata.ExporterVersion, rimSearcherVersion))
            return new CliError("EXPORTER_VERSION_MISMATCH", "结果或数据库中的导出器版本与 rimsearcher.exe 不匹配", new { resultExporterVersion = result.ExporterVersion, metadataExporterVersion = status.Metadata.ExporterVersion, rimSearcherVersion });
        if (status.Metadata.RunId != runId) return new CliError("METADATA_RUN_ID_MISMATCH", "数据库元数据 runId 不匹配", status.Metadata);
        if (status.Metadata.InputModsHash != inputModsHash) return new CliError("INPUT_MODS_HASH_MISMATCH", "数据库元数据输入模组哈希不匹配", status.Metadata);
        if (status.Metadata.RequestedModsHash != requestedModsHash) return new CliError("REQUESTED_MODS_HASH_MISMATCH", "数据库元数据请求模组哈希不匹配", status.Metadata);
        if (status.Metadata.EffectiveModsHash != result.EffectiveModsHash) return new CliError("EFFECTIVE_MODS_HASH_MISMATCH", "数据库元数据与结果文件的有效模组哈希不匹配", status.Metadata);
        if (!status.Metadata.GameVersion.Equals(result.GameVersion, StringComparison.OrdinalIgnoreCase) || !status.Metadata.Language.Equals(result.Language, StringComparison.Ordinal))
            return new CliError("EXPORT_ENVIRONMENT_MISMATCH", "结果文件与数据库元数据的游戏版本或语言不一致", new { result, status.Metadata });
        if (!string.IsNullOrWhiteSpace(configuredGameVersion) && !result.GameVersion.StartsWith(configuredGameVersion, StringComparison.OrdinalIgnoreCase))
            return new CliError("GAME_VERSION_MISMATCH", "导出结果游戏版本与项目配置不匹配", new { configuredGameVersion, result.GameVersion });
        if (!string.IsNullOrWhiteSpace(projectPackageId) && !status.Metadata.ProjectPackageId.Equals(projectPackageId, StringComparison.OrdinalIgnoreCase))
            return new CliError("PROJECT_PACKAGE_ID_MISMATCH", "数据库元数据项目 packageId 不匹配", status.Metadata);
        return null;
    }

    private static bool VersionsEqual(string? left, string? right)
        => Version.TryParse(left, out var leftVersion) && Version.TryParse(right, out var rightVersion)
            && leftVersion.Major == rightVersion.Major
            && leftVersion.Minor == rightVersion.Minor
            && leftVersion.Build == rightVersion.Build;

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

    public static string HashLines(IEnumerable<string> values) => HashBytes(Encoding.UTF8.GetBytes(string.Join("\n", values)));
    public static string HashBytes(ReadOnlySpan<byte> bytes) => "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
