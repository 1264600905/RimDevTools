using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace RimDevTools;

internal sealed record WorkshopPublishExecution(bool Ok, int ExitCode, string? ErrorCode, string? Message, object Details);

internal static class WorkshopPublishingService
{
    internal static Func<ProjectContext?, WorkshopPublishRequest, CancellationToken, Task<WorkshopPublishExecution>>? PublishOverride { get; set; }
    internal static async Task<WorkshopPublishExecution> UploadAsync(ProjectContext context, string[] args, CancellationToken token,
        EnvironmentLock? heldLock = null, string? expectedDeployRunId = null)
    {
        var changeNote = WorkshopMetadata.ChangeNote(args);
        var language = WorkshopMetadata.Language(args);
        var configDirectory = GameEnvironment.ConfigDirectory(context) ?? throw new InvalidDataException("无法解析游戏 Config 目录");
        if (heldLock is not null && !heldLock.Owns(configDirectory)) throw new InvalidDataException("组合操作没有持有目标环境锁");
        var dryRun = CliArgs.Has(args, "--dry-run");
        using var gate = heldLock is null && !dryRun ? EnvironmentLock.TryAcquire(configDirectory, "steamworkshops upload", context.Root, Guid.NewGuid().ToString("N"), out _) : null;
        if (!dryRun && heldLock is null && gate is null) return Fail("ENVIRONMENT_BUSY", "环境被其他操作占用", new { }, ExitCodes.Busy);
        var verification = await DeploymentVerifier.VerifyAsync(context, token);
        if (expectedDeployRunId is not null && verification.DeployRunId != expectedDeployRunId)
            return Fail("DEPLOYMENT_RUN_MISMATCH", "组合操作的收据不属于刚完成的部署", verification);
        if (!verification.Valid) return Fail(verification.Differences.Any(d => d.Scope == "project") ? "PROJECT_NOT_DEPLOYED" : "DEPLOYMENT_VERIFICATION_FAILED", "部署基线校验失败，请重新普通 deploy", verification);
        if (!verification.PublishReady) return Fail("ASSET_PACK_UNVERIFIED", "官方资源包未验证，不能上传", verification);
        var health = await SteamworksSessionProbe.RunAsync(context, cancellationToken: token);
        if (health.WorkshopAvailability != "available" || health.RimWorldInLibrary != true)
            return Fail(health.ErrorCode ?? "STEAM_LIBRARY_UNAVAILABLE", health.Reason ?? "RimWorld 库中不可用", new { verification, environment = health, verification.Artifacts });
        var create = verification.PublishedFileId is null;
        if (!create)
        {
            var detail = await SteamworksSessionProbe.RunAsync(context, "detail", id: ulong.Parse(verification.PublishedFileId!), cancellationToken: token);
            if (detail.ErrorCode is not null) return Fail(detail.ErrorCode, detail.Reason!, new { verification, environment = detail, verification.Artifacts });
            if (detail.Page!.Items.Single().OwnerSteamId != detail.CurrentUserSteamId)
                return Fail("WORKSHOP_OWNER_REQUIRED", "当前账号不是上传 owner；本版不检测贡献者写权限", new { verification, verification.Artifacts });
        }
        var about = XDocument.Load(Path.Combine(context.Root, "About", "About.xml")).Root!;
        var native = NativePath(context);
        var files = WorkshopUploadCommand.InspectContent(verification.Target);
        var description = create ? ValidateDescription(about.Element("description")?.Value.Trim() ?? "") : null;
        var preview = create ? ValidatePreview(Path.Combine(verification.Target, "About", "Preview.png")) : null;
        var title = create ? WorkshopMetadata.Title(about.Element("name")?.Value.Trim() ?? "") : null;
        if (CliArgs.Has(args, "--dry-run")) return new(true, 0, null, null, SteamWorkshopsCommand.Json(new { dryRun = true, verification, action = create ? "create_private" : "update_content_only", changeNote, textLanguage = create ? language : null, verification.Artifacts }));
        if (ProcessUtil.IsRimWorldRunning()) return Fail("RIMWORLD_RUNNING", "请先关闭 RimWorld 再上传", verification, ExitCodes.Busy);
        var request = new WorkshopPublishRequest(Guid.NewGuid().ToString("N"), native, context.Root, verification.Target,
            create ? null : ulong.Parse(verification.PublishedFileId!), title, description, preview, create ? 2 : null,
            create ? new[] { "Mod" }.Concat(about.Element("supportedVersions")?.Elements("li").Select(x => x.Value.Trim()) ?? []).Distinct().ToArray() : null,
            Timeout(args), files, language, changeNote);
        var execution = await RunHelperAsync(context, request, token);
        var publishedDetails = JsonSerializer.SerializeToNode(execution.Details)!.AsObject();
        publishedDetails["deployRunId"] = verification.DeployRunId;
        publishedDetails["contentPath"] = verification.Target;
        execution = execution with { Details = JsonSerializer.SerializeToElement(publishedDetails) };
        if (execution.Ok)
        {
            var after = await DeploymentVerifier.VerifyAsync(context, token);
            if (!after.Valid) return Fail("WORKSHOP_SUBMITTED_CONTENT_CHANGED", "Steam 已接受，但发布后项目/内容校验失败；不要自动重试", new { remoteOutcome = "accepted", publish = execution.Details, verification = after, after.Artifacts }, submissionStarted: true);
        }
        return execution;
    }

    internal static async Task<WorkshopPublishExecution> EditAsync(ProjectContext? context, string[] args, CancellationToken token)
    {
        var raw = CliArgs.Value(args, "--id");
        var id = raw is not null ? SteamWorkshopsCommand.ParseId(raw) ?? throw new ArgumentException("无效的 --id")
            : context is not null ? WorkshopUploadCommand.ReadId(Path.Combine(context.Root, "About", "PublishedFileId.txt")) : null;
        if (id is null) return Fail("WORKSHOP_ID_REQUIRED", "edit 需要项目发布文件或明确 --id", new { });
        var language = WorkshopMetadata.Language(args);
        var changeNote = WorkshopMetadata.ChangeNote(args);
        var title = CliArgs.Value(args, "--title") is { } name ? WorkshopMetadata.Title(name) : null;
        var additions = CliArgs.Values(args, "--add-preview").Select(path => ValidatePreview(Path.GetFullPath(path))).ToArray();
        var replacements = WorkshopMetadata.Replacements(args);
        var removals = CliArgs.Values(args, "--remove-preview").Select(WorkshopMetadata.Index).ToArray();
        var explicitFields = new[] { "--title", "--description-file", "--preview-file", "--visibility", "--add-preview", "--update-preview", "--remove-preview" }.Any(option => CliArgs.Has(args, option));
        string? description = null;
        if (CliArgs.Value(args, "--description-file") is { } descriptionFile)
            description = ValidateDescription(WorkshopMetadata.ReadText(descriptionFile));
        else if (!explicitFields)
        {
            if (context is null) throw new ArgumentException("无项目时更新描述需使用 --description-file");
            description = ValidateDescription(XDocument.Load(Path.Combine(context.Root, "About", "About.xml")).Root?.Element("description")?.Value.Trim() ?? "");
        }
        var preview = CliArgs.Value(args, "--preview-file") is { } file ? ValidatePreview(Path.GetFullPath(file)) : null;
        int? visibility = CliArgs.Value(args, "--visibility")?.ToLowerInvariant() switch
        {
            null => null, "public" => 0, "friends-only" => 1, "private" => 2, "unlisted" => 3,
            _ => throw new ArgumentException("--visibility 必须为 private/public/friends-only/unlisted")
        };
        var configDirectory = context is null ? GameEnvironment.ConfigDirectory() : GameEnvironment.ConfigDirectory(context)!;
        var dryRun = CliArgs.Has(args, "--dry-run");
        using var gate = dryRun ? null : EnvironmentLock.TryAcquire(configDirectory, "steamworkshops edit", context?.Root ?? "", Guid.NewGuid().ToString("N"), out _);
        if (!dryRun && gate is null) return Fail("ENVIRONMENT_BUSY", "环境被其他操作占用", new { }, ExitCodes.Busy);
        var detail = await SteamworksSessionProbe.RunAsync(context, "detail", id: id, cancellationToken: token);
        if (detail.ErrorCode is not null) return Fail(detail.ErrorCode, detail.Reason!, detail);
        if (detail.RimWorldInLibrary != true || detail.Page!.Items.Single().OwnerSteamId != detail.CurrentUserSteamId)
            return Fail("WORKSHOP_OWNER_REQUIRED", "当前账号没有 owner 写权限或 RimWorld 库中不可用", detail);
        _ = WorkshopMetadata.ExpectedPreviews(detail.Page.Items.Single().AdditionalPreviews ?? [], additions, replacements, removals);
        if (CliArgs.Has(args, "--dry-run")) return new(true, 0, null, null, SteamWorkshopsCommand.Json(new
        {
            dryRun = true, publishedFileId = id.ToString(), fields = new { title = title is not null, description = description is not null, preview = preview is not null, visibility, additions, replacements, removals },
            changeNote, textLanguage = title is not null || description is not null ? language : null,
            setItemContentCalled = false, permissions = WorkshopPermissionEvaluator.Evaluate(detail.Page.Items.Single().OwnerSteamId, detail.CurrentUserSteamId)
        }));
        if (ProcessUtil.IsRimWorldRunning()) return Fail("RIMWORLD_RUNNING", "请先关闭 RimWorld 再编辑工坊", new { }, ExitCodes.Busy);
        return await RunHelperAsync(context, new(Guid.NewGuid().ToString("N"), NativePath(context), context?.Root, null, id,
            title, description, preview, visibility, null, Timeout(args), null, language, changeNote, additions, replacements, removals), token);
    }

    internal static int Write(string command, ProjectContext? context, WorkshopPublishExecution execution)
        => execution.Ok ? CliResult.WriteSuccess(command, context?.Root, execution.Details)
            : CliResult.WriteFailure(command, execution.ExitCode, execution.ErrorCode!, execution.Message!, context?.Root, execution.Details);

    internal static int Timeout(string[] args)
    {
        if (!CliArgs.TryPositiveInt(args, "--timeout-seconds", 1800, out var seconds, out var error) || seconds > 86400)
            throw new ArgumentException(error ?? "超时不能超过 86400 秒");
        return seconds;
    }
    internal static string ValidateDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description) || description.Contains('\0') || Encoding.UTF8.GetByteCount(description) >= 8000)
            throw new ArgumentException("描述不能为空、不能包含 NUL 且必须少于 8000 UTF-8 字节；不会隐式清空描述");
        return description;
    }
    internal static string ValidatePreview(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < 10 || info.Length >= 1048576) throw new ArgumentException("预览图必须存在且小于 1 MiB");
        var bytes = File.ReadAllBytes(path);
        var png = bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var jpg = bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
        var gif = Encoding.ASCII.GetString(bytes, 0, 6) is "GIF87a" or "GIF89a";
        if (!png && !jpg && !gif) throw new ArgumentException("预览文件内容必须为 PNG、JPEG 或 GIF");
        return path;
    }
    private static string NativePath(ProjectContext? context)
    {
        var game = SteamworksSessionProbe.GameDirectory(context) ?? throw new InvalidDataException("未找到游戏目录");
        return new[] { Path.Combine(game, "steam_api64.dll"), Path.Combine(game, "RimWorldWin64_Data", "Plugins", "x86_64", "steam_api64.dll") }.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("未找到 steam_api64.dll");
    }
    private static WorkshopPublishExecution Fail(string code, string message, object details, int exit = ExitCodes.TaskFailed, bool? submissionStarted = false)
    {
        var output = JsonSerializer.SerializeToNode(details, JsonDefaults.Storage)!.AsObject();
        output["submissionStarted"] = submissionStarted;
        output["submitCallCount"] = submissionStarted.HasValue ? submissionStarted.Value ? 1 : 0 : null;
        return new(false, exit, code, message, JsonSerializer.SerializeToElement(output));
    }

    private static async Task<WorkshopPublishExecution> RunHelperAsync(ProjectContext? context, WorkshopPublishRequest request, CancellationToken token)
    {
        if (PublishOverride is not null) return await PublishOverride(context, request, token);
        var before = GameProcessIds();
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            WorkingDirectory = Path.GetDirectoryName(request.NativeLibraryPath), UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(typeof(CliApplication).Assembly.Location);
        start.ArgumentList.Add("--workshop-publish-helper"); start.Environment["SteamAppId"] = "294100"; start.Environment["SteamGameId"] = "294100";
        using var helper = Process.Start(start) ?? throw new IOException("无法启动发布 helper");
        WorkshopPublishResult? latest = null;
        var output = Task.Run(async () =>
        {
            while (await helper.StandardOutput.ReadLineAsync() is { } line)
            {
                if (line.StartsWith(WorkshopPublisher.Prefix, StringComparison.Ordinal))
                {
                    latest = JsonSerializer.Deserialize<WorkshopPublishResult>(line[WorkshopPublisher.Prefix.Length..], JsonDefaults.Storage);
                    Console.Error.WriteLine($"Workshop: {latest?.Stage}; id={latest?.PublishedFileId ?? "unknown"}");
                }
            }
        });
        var stderr = helper.StandardError.ReadToEndAsync();
        await helper.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, JsonDefaults.Storage)); helper.StandardInput.Close();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds + 15));
        var interrupted = false;
        try { await helper.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            interrupted = true;
            if (!helper.HasExited) helper.Kill(true);
            await helper.WaitForExitAsync(CancellationToken.None);
        }
        await output; var errors = await stderr;
        if (interrupted)
        {
            latest ??= new() { OperationId = request.OperationId, PublishedFileId = request.PublishedFileId?.ToString(), ChangeNote = request.ChangeNote,
                TextLanguage = request.Name is not null || request.Description is not null ? request.TextLanguage : null };
            var detail = latest.PublishedFileId is null || latest.PostTimeoutQueryAttempted ? null : await SteamworksSessionProbe.RunAsync(context, "detail", id: ulong.Parse(latest.PublishedFileId), seconds: 10);
            latest = latest with
            {
                Succeeded = false, TimedOut = !token.IsCancellationRequested, RemoteOutcome = "unknown", ErrorCode = token.IsCancellationRequested ? "CANCELLED" : "WORKSHOP_UPLOAD_TIMEOUT", Message = "本地 helper 已停止；远端结果由调用方根据时间证据判断，不自动重试",
                LastUpdatedAtUnix = detail?.Page?.Items.SingleOrDefault()?.LastUpdatedAtUnix,
                RemoteQuery = new { checkedAt = DateTimeOffset.UtcNow, source = detail?.Source ?? "not_available", cachedData = false, reason = detail?.Reason ?? (latest.PublishedFileId is null ? "未知目标 ID" : latest.PostTimeoutQueryAttempted ? "已进行过本次超时后查询；helper 中断，没有获得结果，不再重复查询" : null) },
                CurrentTime = DateTimeOffset.UtcNow
            };
        }
        if (latest is null) return Fail("WORKSHOP_HELPER_FAILED", "发布 helper 没有返回结果: " + errors, new { request.OperationId, helperExitCode = helper.ExitCode }, submissionStarted: null);
        var details = JsonSerializer.SerializeToNode(latest, JsonDefaults.Storage)!.AsObject();
        details["backend"] = "steamworks-standalone"; details["helperExitCode"] = helper.ExitCode;
        details["gameProcessIdsBefore"] = JsonSerializer.SerializeToNode(before); details["gameProcessIdsAfter"] = JsonSerializer.SerializeToNode(GameProcessIds());
        details["gameStarted"] = false;
        details["artifacts"] = JsonSerializer.SerializeToNode(context is not null && File.Exists(DeploymentReceiptStore.PathFor(context)) ? new[] { DeploymentReceiptStore.PathFor(context) } : []);
        Console.Error.WriteLine($"Workshop 返回时间: {latest.CurrentTimeLocal}; Steam 最近更新时间: {latest.LastUpdatedAt ?? "unknown"}");
        var json = JsonSerializer.SerializeToElement(details);
        return latest.Succeeded && helper.ExitCode == 0 ? new(true, 0, null, null, json)
            : new(false, interrupted && token.IsCancellationRequested ? ExitCodes.Cancelled : latest.TimedOut ? ExitCodes.Timeout : ExitCodes.TaskFailed, latest.ErrorCode ?? "WORKSHOP_PUBLISH_FAILED", latest.Message ?? "工坊发布失败", json);
    }
    private static int[] GameProcessIds()
    {
        var processes = Process.GetProcessesByName("RimWorld").Concat(Process.GetProcessesByName("RimWorldWin64")).ToArray();
        try { return processes.Select(p => p.Id).Order().ToArray(); }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}
