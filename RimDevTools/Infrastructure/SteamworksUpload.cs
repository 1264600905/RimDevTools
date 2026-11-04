using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace RimDevTools;

internal sealed record WorkshopRemoteItem(string PublishedFileId, string OwnerSteamId, uint ConsumerAppId, string Title, string Description, int Visibility, int FileBytes, int PreviewBytes, uint UpdatedAt);
internal sealed record WorkshopUploadResult
{
    public string Stage { get; init; } = "preflight";
    public bool Succeeded { get; init; }
    public bool CreationAttempted { get; init; }
    public bool Created { get; init; }
    public bool SubmissionStarted { get; init; }
    public bool OutcomeDefinitive { get; init; }
    public bool NeedsWorkshopAgreement { get; init; }
    public string? PublishedFileId { get; init; }
    public string? SteamId { get; init; }
    public int? CreateResult { get; init; }
    public int? SubmitResult { get; init; }
    public int ContentFileCount { get; init; }
    public long ContentBytes { get; init; }
    public uint? PreviousUpdatedAt { get; init; }
    public WorkshopRemoteItem? VerifiedItem { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
}

internal static class SteamworksUpload
{
    public static WorkshopUploadResult Run(WorkshopUploadRequest request, Action<WorkshopUploadResult> report)
    {
        var result = new WorkshopUploadResult { PublishedFileId = request.PublishedFileId?.ToString(CultureInfo.InvariantCulture) };
        var heldFiles = new List<FileStream>();
        IntPtr library = IntPtr.Zero;
        VoidCall? shutdown = null;
        var initialized = false;
        var elapsed = Stopwatch.StartNew();
        try
        {
            foreach (var file in request.Files) heldFiles.Add(File.Open(Path.Combine(request.ContentPath, file.Path), FileMode.Open, FileAccess.Read, FileShare.Read));
            if (!WorkshopUploadCommand.InspectContent(request.ContentPath).SequenceEqual(request.Files)) throw new InvalidDataException("发布内容在预检后发生变化，拒绝上传");
            library = NativeLibrary.Load(request.NativeLibraryPath);
            shutdown = Bind<VoidCall>("SteamAPI_Shutdown");
            if (!(initialized = Bind<InitCall>("SteamAPI_Init")())) throw new InvalidOperationException("SteamAPI_Init 失败");
            var callbacks = Bind<VoidCall>("SteamAPI_RunCallbacks");
            var user = Bind<InterfaceCall>("SteamAPI_SteamUser_v021")();
            var utils = Bind<InterfaceCall>("SteamAPI_SteamUtils_v010")();
            var ugc = Bind<InterfaceCall>("SteamAPI_SteamUGC_v016")();
            if (user == IntPtr.Zero || utils == IntPtr.Zero || ugc == IntPtr.Zero) throw new InvalidOperationException("Steamworks 接口不可用");
            if (!Bind<LoggedOnCall>("SteamAPI_ISteamUser_BLoggedOn")(user) || Bind<AppIdCall>("SteamAPI_ISteamUtils_GetAppID")(utils) != 294100)
                throw new InvalidOperationException("Steam 账号未在线认证，或实际 App ID 不是 RimWorld");
            var steamId = Bind<SteamIdCall>("SteamAPI_ISteamUser_GetSteamID")(user);
            if (steamId == 0) throw new InvalidOperationException("无效的当前 Steam 用户");
            result = result with { SteamId = steamId.ToString(CultureInfo.InvariantCulture) };
            var complete = Bind<CompletedCall>("SteamAPI_ISteamUtils_IsAPICallCompleted");
            var callResult = Bind<ResultCall>("SteamAPI_ISteamUtils_GetAPICallResult");
            var createItem = Bind<CreateCall>("SteamAPI_ISteamUGC_CreateItem");
            var startUpdate = Bind<StartUpdateCall>("SteamAPI_ISteamUGC_StartItemUpdate");
            var setTitle = Bind<SetStringCall>("SteamAPI_ISteamUGC_SetItemTitle");
            var setDescription = Bind<SetStringCall>("SteamAPI_ISteamUGC_SetItemDescription");
            var setContent = Bind<SetStringCall>("SteamAPI_ISteamUGC_SetItemContent");
            var setPreview = Bind<SetStringCall>("SteamAPI_ISteamUGC_SetItemPreview");
            var setVisibility = Bind<VisibilityCall>("SteamAPI_ISteamUGC_SetItemVisibility");
            var setTags = Bind<TagsCall>("SteamAPI_ISteamUGC_SetItemTags");
            var submit = Bind<SubmitCall>("SteamAPI_ISteamUGC_SubmitItemUpdate");
            var progress = Bind<ProgressCall>("SteamAPI_ISteamUGC_GetItemUpdateProgress");
            var createQuery = Bind<QueryCall>("SteamAPI_ISteamUGC_CreateQueryUGCDetailsRequest");
            var sendQuery = Bind<SendQueryCall>("SteamAPI_ISteamUGC_SendQueryUGCRequest");
            var readQuery = Bind<ReadQueryCall>("SteamAPI_ISteamUGC_GetQueryUGCResult");
            var releaseQuery = Bind<ReleaseQueryCall>("SteamAPI_ISteamUGC_ReleaseQueryUGCRequest");
            var cachePolicy = Bind<CachePolicyCall>("SteamAPI_ISteamUGC_SetAllowCachedResponse");
            var longDescription = Bind<LongDescriptionCall>("SteamAPI_ISteamUGC_SetReturnLongDescription");
            if (Marshal.SizeOf<ItemDetails>() != 9776 || Marshal.SizeOf<CreatedItem>() != 24 || Marshal.SizeOf<SubmittedItem>() != 16)
                throw new InvalidOperationException("Steamworks Windows x64 ABI 布局不匹配");

            var id = request.PublishedFileId ?? 0;
            if (request.CreatePrivate)
            {
                if (WorkshopUploadCommand.ReadId(IdPath(request.ProjectRoot)).HasValue || WorkshopUploadCommand.ReadId(IdPath(request.ContentPath)).HasValue)
                    throw new InvalidDataException("PublishedFileId 已存在，拒绝重复创建");
                result = result with { CreationAttempted = true };
                Stage("creating_private_item");
                var created = Wait<CreatedItem>(createItem(ugc, 294100, 0), 3403);
                result = result with { CreateResult = created.Result, NeedsWorkshopAgreement = created.NeedsAgreement != 0 };
                if (created.Result != 1 || created.Id is 0 or ulong.MaxValue) throw new InvalidOperationException("CreateItem 失败，Steam EResult=" + created.Result);
                id = created.Id;
                result = result with { Created = true, PublishedFileId = id.ToString(CultureInfo.InvariantCulture) };
                Stage("created");
                // 这两个文件是模组身份元数据，不是日志或测试证据；创建后立即保存以防重复创建。
                SaveNewId(IdPath(request.ProjectRoot), id);
                SaveNewId(IdPath(request.ContentPath), id);
                heldFiles.Add(File.Open(IdPath(request.ContentPath), FileMode.Open, FileAccess.Read, FileShare.Read));
            }
            else if (WorkshopUploadCommand.ReadId(IdPath(request.ProjectRoot)) != id || WorkshopUploadCommand.ReadId(IdPath(request.ContentPath)) != id)
                throw new InvalidDataException("上传前 PublishedFileId 校验失败");

            Stage("checking_owner");
            var ownerCheck = Query(id);
            if (ownerCheck.Result != 1 || ownerCheck.Id != id || ownerCheck.ConsumerAppId != 294100 || ownerCheck.Owner != steamId)
                throw new InvalidDataException("条目不属于 RimWorld 或当前 Steam 账号，拒绝修改");
            result = result with { PreviousUpdatedAt = ownerCheck.UpdatedAt };
            var expectedVisibility = request.Visibility ?? ownerCheck.Visibility;
            var files = WorkshopUploadCommand.InspectContent(request.ContentPath);
            result = result with { ContentFileCount = files.Length, ContentBytes = files.Sum(f => f.Bytes) };
            var handle = startUpdate(ugc, 294100, id);
            if (handle is 0 or ulong.MaxValue) throw new InvalidOperationException("StartItemUpdate 返回无效句柄");
            Require(setTitle(ugc, handle, request.Name), "SetItemTitle");
            Require(setContent(ugc, handle, request.ContentPath), "SetItemContent");
            Require(setPreview(ugc, handle, Path.Combine(request.ContentPath, "About", "Preview.png")), "SetItemPreview");
            if (request.Visibility.HasValue) Require(setVisibility(ugc, handle, request.Visibility.Value), "SetItemVisibility");
            if (request.UpdateDescription) Require(setDescription(ugc, handle, request.Description), "SetItemDescription");
            var tagPointers = request.Tags.Select(Marshal.StringToCoTaskMemUTF8).ToArray();
            var tagArray = Marshal.AllocHGlobal(IntPtr.Size * tagPointers.Length);
            try
            {
                Marshal.Copy(tagPointers, 0, tagArray, tagPointers.Length);
                var tags = new TagArray { Strings = tagArray, Count = tagPointers.Length };
                Require(setTags(ugc, handle, ref tags), "SetItemTags");
            }
            finally
            {
                Marshal.FreeHGlobal(tagArray);
                foreach (var pointer in tagPointers) Marshal.FreeCoTaskMem(pointer);
            }

            // 从调用前开始保守标记为已提交；任何异常均不自动重试。
            result = result with { SubmissionStarted = true };
            Stage("submitting");
            var submission = submit(ugc, handle, "RimDevTools standalone Steamworks upload test.");
            if (submission == 0)
            {
                result = result with { SubmissionStarted = false, OutcomeDefinitive = true };
                throw new InvalidOperationException("SubmitItemUpdate 返回无效调用句柄");
            }
            var lastProgress = -1;
            var submitted = Wait<SubmittedItem>(submission, 3404, () =>
            {
                var stage = progress(ugc, handle, out _, out _);
                if (stage != lastProgress) { lastProgress = stage; Stage("uploading_" + stage); }
            });
            result = result with { SubmitResult = submitted.Result, OutcomeDefinitive = true, NeedsWorkshopAgreement = result.NeedsWorkshopAgreement || submitted.NeedsAgreement != 0 };
            if (submitted.Result != 1 || submitted.Id != id) throw new InvalidOperationException("SubmitItemUpdate 未成功，Steam EResult=" + submitted.Result);
            Stage("verifying");
            var verified = Query(id);
            result = result with { VerifiedItem = new(id.ToString(CultureInfo.InvariantCulture), verified.Owner.ToString(CultureInfo.InvariantCulture), verified.ConsumerAppId,
                Utf8(verified.Title), Utf8(verified.Description), verified.Visibility, verified.FileBytes, verified.PreviewBytes, verified.UpdatedAt) };
            if (verified.Result != 1 || verified.Id != id || verified.Owner != steamId || verified.ConsumerAppId != 294100
                || Utf8(verified.Title) != request.Name || verified.FileBytes != result.ContentBytes || verified.Visibility != expectedVisibility
                || verified.UpdatedAt < ownerCheck.UpdatedAt || verified.PreviewBytes <= 0
                || request.UpdateDescription && Utf8(verified.Description) != request.Description)
                throw new InvalidDataException("Steam 已接受提交，但远端条目核验未通过；不要重复提交");
            return result with { Stage = "succeeded", Succeeded = true, Message = "Steam 已接受完整内容上传，远端条目身份、标题、可见性与内容大小已核验" };

            T Bind<T>(string export) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, export));
            void Stage(string stage) { result = result with { Stage = stage }; report(result); }
            T Wait<T>(ulong call, int callback, Action? tick = null) where T : struct
            {
                if (call == 0) throw new InvalidOperationException("Steam 返回无效异步句柄");
                var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
                try
                {
                    while (elapsed.Elapsed < TimeSpan.FromSeconds(request.TimeoutSeconds))
                    {
                        callbacks();
                        tick?.Invoke();
                        if (complete(utils, call, out var failed))
                        {
                            if (failed || !callResult(utils, call, buffer, Marshal.SizeOf<T>(), callback, out var ioFailure) || ioFailure)
                                throw new IOException("Steam 异步结果 I/O 失败，callback=" + callback);
                            return Marshal.PtrToStructure<T>(buffer);
                        }
                        Thread.Sleep(100);
                    }
                    throw new TimeoutException("等待 Steam 回调超时，callback=" + callback);
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            ItemDetails Query(ulong targetId)
            {
                var query = createQuery(ugc, [targetId], 1);
                if (query is 0 or ulong.MaxValue) throw new IOException("无法创建 UGC 查询");
                try
                {
                    Require(cachePolicy(ugc, query, 0), "SetAllowCachedResponse");
                    Require(longDescription(ugc, query, true), "SetReturnLongDescription");
                    var done = Wait<QueryCompleted>(sendQuery(ugc, query), 3401);
                    if (done.Handle != query || done.Result != 1 || done.ReturnedResults != 1) throw new IOException("UGC 查询未返回唯一条目，EResult=" + done.Result);
                    Require(readQuery(ugc, query, 0, out var details), "GetQueryUGCResult");
                    return details;
                }
                finally { releaseQuery(ugc, query); }
            }
        }
        catch (Exception ex)
        {
            return result with { Stage = "failed", ErrorCode = ex is TimeoutException ? "WORKSHOP_OUTCOME_UNCONFIRMED" : "WORKSHOP_UPLOAD_FAILED", Message = ex.Message };
        }
        finally
        {
            try { if (initialized) shutdown?.Invoke(); }
            finally
            {
                if (library != IntPtr.Zero) NativeLibrary.Free(library);
                foreach (var file in heldFiles) file.Dispose();
            }
        }
    }

    private static string IdPath(string root) => Path.Combine(root, "About", "PublishedFileId.txt");
    private static void SaveNewId(string path, ulong id)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var bytes = Encoding.UTF8.GetBytes(id.ToString(CultureInfo.InvariantCulture));
        file.Write(bytes);
        file.Flush(true);
    }
    private static void Require(bool success, string operation) { if (!success) throw new IOException(operation + " 返回 false"); }
    private static string Utf8(byte[] text) => Encoding.UTF8.GetString(text, 0, Array.IndexOf(text, (byte)0) is var end && end >= 0 ? end : text.Length);

    // Windows x64 布局已对照本机 Steamworks.NET 程序集及 20.2.0 的原始定义核对。
    [StructLayout(LayoutKind.Sequential, Pack = 8)] private struct CreatedItem { public int Result; public ulong Id; public byte NeedsAgreement; }
    [StructLayout(LayoutKind.Sequential, Pack = 8)] private struct SubmittedItem { public int Result; public byte NeedsAgreement; public ulong Id; }
    [StructLayout(LayoutKind.Sequential, Pack = 8)] private struct TagArray { public IntPtr Strings; public int Count; }
    [StructLayout(LayoutKind.Sequential, Pack = 8)] private struct QueryCompleted
    {
        public ulong Handle; public int Result; public uint ReturnedResults; public uint TotalResults; public byte Cached;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public byte[] Cursor;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 8)] private struct ItemDetails
    {
        public ulong Id; public int Result; public int FileType; public uint CreatorAppId; public uint ConsumerAppId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 129)] public byte[] Title;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8000)] public byte[] Description;
        public ulong Owner; public uint CreatedAt; public uint UpdatedAt; public uint AddedAt; public int Visibility;
        public byte Banned; public byte Accepted; public byte TagsTruncated;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1025)] public byte[] Tags;
        public ulong File; public ulong Preview;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 260)] public byte[] FileName;
        public int FileBytes; public int PreviewBytes;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public byte[] Url;
        public uint VotesUp; public uint VotesDown; public float Score; public uint Children;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool InitCall();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidCall();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr InterfaceCall();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool LoggedOnCall(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong SteamIdCall(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AppIdCall(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool CompletedCall(IntPtr self, ulong call, [MarshalAs(UnmanagedType.I1)] out bool failed);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool ResultCall(IntPtr self, ulong call, IntPtr buffer, int size, int callback, [MarshalAs(UnmanagedType.I1)] out bool failed);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong CreateCall(IntPtr self, uint appId, int fileType);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong StartUpdateCall(IntPtr self, uint appId, ulong id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool SetStringCall(IntPtr self, ulong update, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool VisibilityCall(IntPtr self, ulong update, int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool TagsCall(IntPtr self, ulong update, ref TagArray tags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong SubmitCall(IntPtr self, ulong update, [MarshalAs(UnmanagedType.LPUTF8Str)] string note);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ProgressCall(IntPtr self, ulong update, out ulong processed, out ulong total);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong QueryCall(IntPtr self, [In] ulong[] ids, uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong SendQueryCall(IntPtr self, ulong query);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool ReadQueryCall(IntPtr self, ulong query, uint index, out ItemDetails result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool ReleaseQueryCall(IntPtr self, ulong query);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool CachePolicyCall(IntPtr self, ulong query, uint seconds);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool LongDescriptionCall(IntPtr self, ulong query, [MarshalAs(UnmanagedType.I1)] bool enabled);
}
