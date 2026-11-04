using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace RimDevTools;

internal sealed record WorkshopPublishRequest(string OperationId, string NativeLibraryPath, string? ProjectRoot,
    string? ContentPath, ulong? PublishedFileId, string? Name, string? Description, string? PreviewPath,
    int? Visibility, string[]? Tags, int TimeoutSeconds, WorkshopContentFile[]? Files,
    string TextLanguage = "english", string? ChangeNote = null, string[]? AddPreviews = null,
    WorkshopPreviewReplacement[]? ReplacePreviews = null, uint[]? RemovePreviews = null);
internal sealed record WorkshopPublishResult
{
    public string OperationId { get; init; } = "";
    public string Stage { get; init; } = "preflight";
    public bool Succeeded { get; init; }
    public bool Created { get; init; }
    public bool CreationAttempted { get; init; }
    public bool SubmissionStarted { get; init; }
    public bool SetItemContentCalled { get; init; }
    public int SubmitCallCount { get; init; }
    public bool TimedOut { get; init; }
    public bool PostTimeoutQueryAttempted { get; init; }
    public string RemoteOutcome { get; init; } = "not_submitted";
    public string? PublishedFileId { get; init; }
    public bool NeedsWorkshopAgreement { get; init; }
    public int? CreateResult { get; init; }
    public int? SubmitResult { get; init; }
    public long ContentBytes { get; init; }
    public DateTimeOffset OperationStartedAt { get; init; } = DateTimeOffset.UtcNow;
    public long OperationStartedAtUnix => OperationStartedAt.ToUnixTimeSeconds();
    public DateTimeOffset? SubmittedAt { get; init; }
    public long? SubmittedAtUnix => SubmittedAt?.ToUnixTimeSeconds();
    public DateTimeOffset CurrentTime { get; init; } = DateTimeOffset.UtcNow;
    public long CurrentTimeUnix => CurrentTime.ToUnixTimeSeconds();
    public string CurrentTimeLocal => CurrentTime.ToOffset(TimeSpan.FromHours(8)).ToString("O");
    public uint? PreviousUpdatedAtUnix { get; init; }
    public uint? LastUpdatedAtUnix { get; init; }
    public string? LastUpdatedAt => LastUpdatedAtUnix.HasValue ? DateTimeOffset.FromUnixTimeSeconds(LastUpdatedAtUnix.Value).ToString("O") : null;
    public object? RemoteQuery { get; init; }
    public WorkshopItemInfo? VerifiedItem { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public string? ChangeNote { get; init; }
    public string? TextLanguage { get; init; }
}

internal static class WorkshopPublisher
{
    internal const string Prefix = "RIMDEVTOOLS_WORKSHOP_PUBLISH=";

    internal static int RunHelper()
    {
        var request = JsonSerializer.Deserialize<WorkshopPublishRequest>(Console.ReadLine() ?? "", JsonDefaults.Storage)
            ?? throw new InvalidDataException("缺少发布请求");
        var result = Publish(request, value => Console.WriteLine(Prefix + JsonSerializer.Serialize(value, JsonDefaults.Storage)));
        Console.WriteLine(Prefix + JsonSerializer.Serialize(result, JsonDefaults.Storage));
        return result.Succeeded ? 0 : result.TimedOut ? ExitCodes.Timeout : ExitCodes.TaskFailed;
    }

    internal static WorkshopPublishResult Publish(WorkshopPublishRequest request, Action<WorkshopPublishResult> report)
    {
        var result = new WorkshopPublishResult { OperationId = request.OperationId, PublishedFileId = request.PublishedFileId?.ToString(CultureInfo.InvariantCulture),
            ChangeNote = request.ChangeNote, TextLanguage = request.Name is not null || request.Description is not null ? request.TextLanguage : null };
        var held = new List<FileStream>();
        try
        {
            var expectedFiles = request.Files ?? [];
            if (request.ContentPath is not null)
            {
                foreach (var file in expectedFiles)
                    held.Add(File.Open(Path.Combine(request.ContentPath, file.Path), FileMode.Open, FileAccess.Read, FileShare.Read));
                CheckFiles();
            }
            if (request.PreviewPath is not null && request.ContentPath is null)
                held.Add(File.Open(request.PreviewPath, FileMode.Open, FileAccess.Read, FileShare.Read));
            foreach (var path in (request.AddPreviews ?? []).Concat((request.ReplacePreviews ?? []).Select(x => x.Path)).Distinct())
                held.Add(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read));
            using var client = new SteamworksClient(request.NativeLibraryPath, request.TimeoutSeconds);
            if (!client.InLibrary || !client.LoggedOn) throw new InvalidDataException("Steam 账号未在线，或库中不可用 RimWorld");
            var id = request.PublishedFileId ?? 0;
            if (id == 0)
            {
                if (request.ContentPath is null || request.ProjectRoot is null) throw new InvalidDataException("首次发布需要项目与部署目录");
                var context = ProjectDiscovery.Find(request.ProjectRoot, out _) ?? throw new InvalidDataException("首次发布项目无效");
                if (WorkshopUploadCommand.ReadId(IdPath(context.Root)).HasValue || WorkshopUploadCommand.ReadId(IdPath(request.ContentPath)).HasValue
                    || DeploymentReceiptStore.KnownBinding(context) is not null) throw new InvalidDataException("已有绑定或绑定丢失，拒绝创建");
                result = result with { CreationAttempted = true }; Stage("creating_private_item");
                var created = client.Wait<CreatedItem>(client.Bind<CreateCall>("SteamAPI_ISteamUGC_CreateItem")(client.Ugc, SteamworksClient.AppId, 0), 3403);
                result = result with { CreateResult = created.Result, NeedsWorkshopAgreement = created.NeedsAgreement != 0 };
                if (created.Result != 1 || created.Id is 0 or ulong.MaxValue) throw new IOException("CreateItem 失败: EResult=" + created.Result);
                id = created.Id;
                result = result with { Created = true, PublishedFileId = id.ToString(CultureInfo.InvariantCulture) }; Stage("created_saving_binding");
                // Retain the genuine known id before any further operation; never keep timeout tombstones.
                var targetIdentity = DeploymentReceiptStore.SaveCreatedIdentity(context, request.ContentPath, result.PublishedFileId!);
                held.Add(File.Open(IdPath(request.ContentPath), FileMode.Open, FileAccess.Read, FileShare.Read));
                expectedFiles = expectedFiles.Append(new("About/PublishedFileId.txt", targetIdentity.Bytes, targetIdentity.Sha256)).OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
            }
            Stage("checking_owner");
            var before = client.Query(id: id, language: request.TextLanguage).Items.Single();
            if (before.OwnerSteamId != client.SteamId || before.ConsumerAppId != SteamworksClient.AppId)
                throw new InvalidDataException("条目不属于 RimWorld 或当前 Steam owner；不检测贡献者写权限");
            result = result with { PreviousUpdatedAtUnix = before.LastUpdatedAtUnix };
            var expectedPreviews = WorkshopMetadata.ExpectedPreviews(before.AdditionalPreviews ?? [], request.AddPreviews ?? [], request.ReplacePreviews ?? [], request.RemovePreviews ?? []);
            CheckFiles();
            var update = client.Bind<StartUpdateCall>("SteamAPI_ISteamUGC_StartItemUpdate")(client.Ugc, SteamworksClient.AppId, id);
            if (update is 0 or ulong.MaxValue) throw new IOException("StartItemUpdate 句柄无效");
            if (request.ContentPath is not null)
            {
                SteamworksClient.Require(client.Bind<SetStringCall>("SteamAPI_ISteamUGC_SetItemContent")(client.Ugc, update, request.ContentPath), "SetItemContent");
                result = result with { SetItemContentCalled = true, ContentBytes = expectedFiles.Sum(x => x.Bytes) };
            }
            // Steam replaces a localized text pair: omitted localized fields can otherwise become empty.
            // Seed the other field from this language's live query (Steam fallback for a new translation).
            if (request.Name is not null || request.Description is not null)
            {
                Set("SetItemUpdateLanguage", request.TextLanguage);
                Set("SetItemTitle", request.Name ?? before.Title);
                Set("SetItemDescription", request.Description ?? before.Description);
            }
            // Existing content uploads never set metadata; creation initializes it once.
            if (request.PreviewPath is not null) Set("SetItemPreview", request.PreviewPath);
            if (request.Visibility.HasValue) SteamworksClient.Require(client.Bind<VisibilityCall>("SteamAPI_ISteamUGC_SetItemVisibility")(client.Ugc, update, request.Visibility.Value), "SetItemVisibility");
            foreach (var replacement in request.ReplacePreviews ?? [])
                SteamworksClient.Require(client.Bind<UpdatePreviewCall>("SteamAPI_ISteamUGC_UpdateItemPreviewFile")(client.Ugc, update, replacement.Index, replacement.Path), "UpdateItemPreviewFile");
            foreach (var index in (request.RemovePreviews ?? []).OrderDescending())
                SteamworksClient.Require(client.Bind<RemovePreviewCall>("SteamAPI_ISteamUGC_RemoveItemPreview")(client.Ugc, update, index), "RemoveItemPreview");
            foreach (var path in request.AddPreviews ?? [])
                SteamworksClient.Require(client.Bind<AddPreviewCall>("SteamAPI_ISteamUGC_AddItemPreviewFile")(client.Ugc, update, path, 0), "AddItemPreviewFile");
            if (request.Tags is not null)
            {
                var pointers = request.Tags.Select(Marshal.StringToCoTaskMemUTF8).ToArray();
                var array = Marshal.AllocHGlobal(IntPtr.Size * pointers.Length);
                try
                {
                    Marshal.Copy(pointers, 0, array, pointers.Length);
                    var tags = new TagArray { Strings = array, Count = pointers.Length };
                    SteamworksClient.Require(client.Bind<TagsCall>("SteamAPI_ISteamUGC_SetItemTags")(client.Ugc, update, ref tags), "SetItemTags");
                }
                finally { Marshal.FreeHGlobal(array); foreach (var p in pointers) Marshal.FreeCoTaskMem(p); }
            }
            CheckFiles();
            if (request.ContentPath is not null && request.ProjectRoot is not null)
            {
                var context = ProjectDiscovery.Find(request.ProjectRoot, out _) ?? throw new InvalidDataException("提交前项目配置不可读取");
                var verification = DeploymentVerifier.VerifyAsync(context).GetAwaiter().GetResult();
                if (!verification.Valid || !verification.PublishReady || verification.PublishedFileId != result.PublishedFileId)
                    throw new InvalidDataException("提交前项目输入/收据/绑定复核失败: " + JsonSerializer.Serialize(verification.Differences, JsonDefaults.Storage));
            }
            if (!client.HasTimeRemaining) throw new TimeoutException("提交前等待预算已用尽；尚未发出 SubmitItemUpdate");
            Stage("submitting");
            result = result with { SubmissionStarted = true, SubmittedAt = DateTimeOffset.UtcNow, RemoteOutcome = "unknown", SubmitCallCount = 1 };
            report(result);
            var call = client.Bind<SubmitCall>("SteamAPI_ISteamUGC_SubmitItemUpdate")(client.Ugc, update, request.ChangeNote);
            if (call == 0) throw new IOException("SubmitItemUpdate 返回无效句柄");
            var submitted = client.Wait<SubmittedItem>(call, 3404);
            result = result with { SubmitResult = submitted.Result, NeedsWorkshopAgreement = result.NeedsWorkshopAgreement || submitted.NeedsAgreement != 0,
                RemoteOutcome = submitted.Result == 1 ? "accepted" : "rejected" };
            if (submitted.Result != 1 || submitted.Id != id) throw new IOException("SubmitItemUpdate 未成功: EResult=" + submitted.Result);
            Stage("verifying");
            var page = client.Query(id: id, language: request.TextLanguage);
            var verified = page.Items.Single();
            result = result with { VerifiedItem = verified, LastUpdatedAtUnix = verified.LastUpdatedAtUnix,
                RemoteQuery = new { page.FetchedAt, source = "native_live_query", cachedData = false } };
            if (verified.OwnerSteamId != client.SteamId || verified.ConsumerAppId != SteamworksClient.AppId
                || request.ContentPath is not null && verified.FileBytes != result.ContentBytes
                || request.ContentPath is null && verified.FileBytes != before.FileBytes
                || verified.Title != (request.Name ?? before.Title) || verified.Description != (request.Description ?? before.Description)
                || verified.Visibility != (request.Visibility.HasValue ? SteamworksClient.VisibilityName(request.Visibility.Value) : before.Visibility)
                || request.PreviewPath is null && verified.PreviewUrl != before.PreviewUrl
                || !PreviewsMatch(expectedPreviews, verified.AdditionalPreviews ?? [])
                || request.Tags is null && verified.Tags != before.Tags)
                throw new InvalidDataException("Steam 已接受提交，但远端身份/内容大小/元数据回读不一致；不要自动重试");
            CheckFiles();
            return result with { Succeeded = true, Stage = "succeeded", CurrentTime = DateTimeOffset.UtcNow, Message = "提交回调和远端核验通过" };

            void Set(string operation, string text) => SteamworksClient.Require(client.Bind<SetStringCall>("SteamAPI_ISteamUGC_" + operation)(client.Ugc, update, text), operation);
            void Stage(string stage) { result = result with { Stage = stage, CurrentTime = DateTimeOffset.UtcNow }; report(result); }
            void CheckFiles()
            {
                if (request.ContentPath is not null && !WorkshopUploadCommand.InspectContent(request.ContentPath).SequenceEqual(expectedFiles))
                    throw new InvalidDataException("发布目录文件集合或哈希变化，拒绝接受为已验证内容");
                if (request.ContentPath is not null && result.PublishedFileId is { } knownId
                    && (WorkshopUploadCommand.ReadId(IdPath(request.ContentPath))?.ToString() != knownId
                        || request.ProjectRoot is not null && WorkshopUploadCommand.ReadId(IdPath(request.ProjectRoot))?.ToString() != knownId))
                    throw new InvalidDataException("提交前工坊发布 ID 发生变化");
            }
        }
        catch (TimeoutException ex)
        {
            result = MarkTimedOut(result, ex.Message);
            report(result);
            // The timed-out session is disposed before this independent bounded query.
            return CollectTimeoutEvidence(result, id =>
            {
                using var probe = new SteamworksClient(request.NativeLibraryPath, 10);
                return probe.Query(id: id);
            });
        }
        catch (Exception ex)
        {
            return result with { ErrorCode = result.Created && !result.SubmissionStarted ? "WORKSHOP_CREATED_ID_SAVE_OR_PREFLIGHT_FAILED" : "WORKSHOP_PUBLISH_FAILED", Message = ex.Message, CurrentTime = DateTimeOffset.UtcNow };
        }
        finally { foreach (var stream in held) stream.Dispose(); }
    }

    internal static bool PreviewsMatch(WorkshopPreview[] expected, WorkshopPreview[] actual)
        => expected.Length == actual.Length && expected.Zip(actual).All(pair => pair.First.Index == pair.Second.Index
            && pair.First.Type == pair.Second.Type && pair.First.OriginalFileName == pair.Second.OriginalFileName
            && (pair.First.Url.Length == 0 ? pair.Second.Url.Length > 0 : pair.First.Url == pair.Second.Url));

    private static string IdPath(string root) => Path.Combine(root, "About", "PublishedFileId.txt");
    internal static WorkshopPublishResult MarkTimedOut(WorkshopPublishResult result, string message)
        => result with { Succeeded = false, TimedOut = true, RemoteOutcome = "unknown", ErrorCode = "WORKSHOP_UPLOAD_TIMEOUT", Message = message,
            LastUpdatedAtUnix = null, RemoteQuery = null, PostTimeoutQueryAttempted = result.PublishedFileId is not null };
    internal static WorkshopPublishResult CollectTimeoutEvidence(WorkshopPublishResult result, Func<ulong, WorkshopQueryPage> query)
    {
        try
        {
            if (result.PublishedFileId is null) result = result with { RemoteQuery = new { reason = "首次创建尚未返回条目 ID，无法按 ID 查询", source = "not_queried", cachedData = false } };
            else
            {
                var page = query(ulong.Parse(result.PublishedFileId, CultureInfo.InvariantCulture));
                result = result with { LastUpdatedAtUnix = page.Items.Single().LastUpdatedAtUnix, RemoteQuery = new { page.FetchedAt, source = "native_live_query", cachedData = false } };
            }
        }
        catch (Exception ex) { result = result with { LastUpdatedAtUnix = null, RemoteQuery = new { checkedAt = DateTimeOffset.UtcNow, source = "native_query_failed", cachedData = false, reason = ex.Message } }; }
        return result with { CurrentTime = DateTimeOffset.UtcNow };
    }
    [StructLayout(LayoutKind.Sequential, Pack = 8)] private struct CreatedItem { public int Result; public ulong Id; public byte NeedsAgreement; }
    [StructLayout(LayoutKind.Sequential, Pack = 8)] private struct SubmittedItem { public int Result; public byte NeedsAgreement; public ulong Id; }
    [StructLayout(LayoutKind.Sequential, Pack = 8)] private struct TagArray { public IntPtr Strings; public int Count; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong CreateCall(IntPtr self, uint appId, int type);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong StartUpdateCall(IntPtr self, uint appId, ulong id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool SetStringCall(IntPtr self, ulong update, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool VisibilityCall(IntPtr self, ulong update, int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool TagsCall(IntPtr self, ulong update, ref TagArray tags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong SubmitCall(IntPtr self, ulong update, [MarshalAs(UnmanagedType.LPUTF8Str)] string? note);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool AddPreviewCall(IntPtr self, ulong update, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int type);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool UpdatePreviewCall(IntPtr self, ulong update, uint index, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool RemovePreviewCall(IntPtr self, ulong update, uint index);
}
