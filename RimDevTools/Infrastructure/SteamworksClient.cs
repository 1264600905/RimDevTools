using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace RimDevTools;

internal sealed record WorkshopItemInfo(string PublishedFileId, string OwnerSteamId, uint ConsumerAppId,
    string Title, string Description, string DescriptionMode, string Visibility, ulong? SubscriptionCount,
    string? SubscriptionCountReason, uint? LastUpdatedAtUnix, string? LastUpdatedAt, string? LastUpdatedAtLocal,
    int FileBytes, string? PreviewUrl, string Tags);
internal sealed record WorkshopQueryPage(int NativePage, uint TotalMatchingResults, bool CachedData,
    DateTimeOffset FetchedAt, WorkshopItemInfo[] Items);
internal sealed record WorkshopDownloadState(uint Flags, bool Subscribed, bool Installed, bool NeedsUpdate,
    bool Downloading, bool DownloadPending, bool DownloadComplete, bool Ready, bool InstallInfoValid,
    string? InstallPath, bool LegacyItem, ulong? BytesDownloaded, ulong? BytesTotal, double? Progress);

// Native ABI is Windows x64 Steamworks (the same versioned interfaces used by the game).
// One session lives in an isolated helper, so it cannot bind another game's loaded libraries.
internal sealed class SteamworksClient : IDisposable
{
    internal const uint AppId = 294100;
    private IntPtr library;
    private bool initialized;
    internal IntPtr Ugc { get; private set; }
    private IntPtr utils;
    internal string SteamId { get; private set; } = "";
    internal bool LoggedOn { get; private set; }
    internal bool InLibrary { get; private set; }
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private readonly TimeSpan budget;

    internal SteamworksClient(string nativePath, int seconds)
    {
        budget = TimeSpan.FromSeconds(seconds);
        try
        {
            if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
                throw new PlatformNotSupportedException("Steamworks 仅支持 Windows x64");
            if (Marshal.SizeOf<ItemDetails>() != 9776 || Marshal.SizeOf<QueryCompleted>() != 280)
                throw new InvalidDataException("Steamworks Windows x64 ABI 布局不匹配");
            library = NativeLibrary.Load(nativePath);
            initialized = Bind<NativeBool>("SteamAPI_Init")();
            if (!initialized) throw new IOException("SteamAPI_Init 失败");
            var user = Bind<InterfaceCall>("SteamAPI_SteamUser_v021")();
            utils = Bind<InterfaceCall>("SteamAPI_SteamUtils_v010")();
            Ugc = Bind<InterfaceCall>("SteamAPI_SteamUGC_v016")();
            var apps = Bind<InterfaceCall>("SteamAPI_SteamApps_v008")();
            if (user == IntPtr.Zero || utils == IntPtr.Zero || Ugc == IntPtr.Zero || apps == IntPtr.Zero)
                throw new IOException("Steamworks 接口不可用");
            if (Bind<AppIdCall>("SteamAPI_ISteamUtils_GetAppID")(utils) != AppId)
                throw new InvalidDataException("Steam 实际 App ID 不是 RimWorld");
            SteamId = Bind<SteamIdCall>("SteamAPI_ISteamUser_GetSteamID")(user).ToString(CultureInfo.InvariantCulture);
            LoggedOn = Bind<SelfBool>("SteamAPI_ISteamUser_BLoggedOn")(user);
            InLibrary = Bind<SelfBool>("SteamAPI_ISteamApps_BIsSubscribed")(apps);
        }
        catch { Dispose(); throw; }
    }

    internal T Bind<T>(string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    internal void Pump() => Bind<VoidCall>("SteamAPI_RunCallbacks")();

    internal T Wait<T>(ulong call, int callback) where T : struct
    {
        if (call == 0) throw new IOException("Steam 返回无效异步句柄");
        var complete = Bind<CompletedCall>("SteamAPI_ISteamUtils_IsAPICallCompleted");
        var read = Bind<ResultCall>("SteamAPI_ISteamUtils_GetAPICallResult");
        var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
        try
        {
            while (elapsed.Elapsed < budget)
            {
                Pump();
                if (complete(utils, call, out var failed))
                {
                    if (failed || !read(utils, call, buffer, Marshal.SizeOf<T>(), callback, out var ioFailed) || ioFailed)
                        throw new IOException("Steam 异步结果读取失败: " + callback);
                    return Marshal.PtrToStructure<T>(buffer);
                }
                Thread.Sleep(50);
            }
            throw new TimeoutException("等待 Steam 回调超时: " + callback);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    internal WorkshopQueryPage Query(string? keyword = null, int page = 1, ulong? id = null)
    {
        if (!LoggedOn) throw new IOException("Steam 当前账号未在线登录");
        var handle = id.HasValue
            ? Bind<DetailsQuery>("SteamAPI_ISteamUGC_CreateQueryUGCDetailsRequest")(Ugc, [id.Value], 1)
            : Bind<AllQuery>("SteamAPI_ISteamUGC_CreateQueryAllUGCRequestPage")(Ugc, keyword is null ? 0 : 11, 0, AppId, AppId, (uint)page);
        if (handle is 0 or ulong.MaxValue) throw new IOException("创建 Steam UGC 查询失败");
        try
        {
            Require(Bind<QueryUInt>("SteamAPI_ISteamUGC_SetAllowCachedResponse")(Ugc, handle, 0), "SetAllowCachedResponse");
            Require(Bind<QueryBool>("SteamAPI_ISteamUGC_SetReturnLongDescription")(Ugc, handle, id.HasValue), "SetReturnLongDescription");
            if (keyword is not null) Require(Bind<QueryString>("SteamAPI_ISteamUGC_SetSearchText")(Ugc, handle, keyword), "SetSearchText");
            var done = Wait<QueryCompleted>(Bind<SendQuery>("SteamAPI_ISteamUGC_SendQueryUGCRequest")(Ugc, handle), 3401);
            if (done.Handle != handle || done.Result != 1 || done.Cached != 0)
                throw new IOException($"Steam 查询未取得本次在线结果: EResult={done.Result}, cached={done.Cached}");
            var items = new List<WorkshopItemInfo>();
            var read = Bind<ReadQuery>("SteamAPI_ISteamUGC_GetQueryUGCResult");
            var statistic = Bind<Statistic>("SteamAPI_ISteamUGC_GetQueryUGCStatistic");
            for (uint i = 0; i < done.ReturnedResults; i++)
            {
                Require(read(Ugc, handle, i, out var d), "GetQueryUGCResult");
                if (d.Result != 1 || d.ConsumerAppId != AppId || id.HasValue && d.Id != id.Value)
                    throw new IOException("条目查询失败或条目不属于 RimWorld: EResult=" + d.Result);
                var countOk = statistic(Ugc, handle, i, 0, out var count);
                var url = new byte[4096];
                var previewOk = Bind<PreviewUrl>("SteamAPI_ISteamUGC_GetQueryUGCPreviewURL")(Ugc, handle, i, url, (uint)url.Length);
                DateTimeOffset? updated = d.UpdatedAt == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(d.UpdatedAt);
                items.Add(new(d.Id.ToString(CultureInfo.InvariantCulture), d.Owner.ToString(CultureInfo.InvariantCulture),
                    d.ConsumerAppId, Utf8(d.Title), Utf8(d.Description), id.HasValue ? "full" : "official_summary_255_bytes",
                    VisibilityName(d.Visibility), countOk ? count : null, countOk ? null : "GetQueryUGCStatistic 返回 false",
                    d.UpdatedAt == 0 ? null : d.UpdatedAt, updated?.ToString("O"), updated?.ToOffset(TimeSpan.FromHours(8)).ToString("O"),
                    d.FileBytes, previewOk ? Utf8(url) : null, Utf8(d.Tags)));
            }
            if (id.HasValue && items.Count != 1) throw new IOException("未取得目标条目；可能不存在或当前账号无权访问");
            return new(page, done.TotalResults, false, DateTimeOffset.UtcNow, items.ToArray());
        }
        finally { Bind<ReleaseQuery>("SteamAPI_ISteamUGC_ReleaseQueryUGCRequest")(Ugc, handle); }
    }

    internal WorkshopDownloadState DownloadState(ulong id)
    {
        var flags = Bind<ItemState>("SteamAPI_ISteamUGC_GetItemState")(Ugc, id);
        var folder = new byte[32768];
        var info = (flags & 4) != 0 && Bind<InstallInfo>("SteamAPI_ISteamUGC_GetItemInstallInfo")(Ugc, id, out _, folder, (uint)folder.Length, out _);
        var path = info ? Utf8(folder) : null;
        var progressOk = Bind<DownloadInfo>("SteamAPI_ISteamUGC_GetItemDownloadInfo")(Ugc, id, out var downloaded, out var total);
        return EvaluateDownload(flags, info, path, progressOk ? downloaded : null, progressOk ? total : null);
    }

    internal static WorkshopDownloadState EvaluateDownload(uint flags, bool info, string? path, ulong? downloaded, ulong? total)
    {
        var legacy = (flags & 2) != 0;
        var complete = (flags & 4) != 0 && (flags & (8 | 16 | 32)) == 0 && info && !legacy && Directory.Exists(path);
        return new(flags, (flags & 1) != 0, (flags & 4) != 0, (flags & 8) != 0, (flags & 16) != 0, (flags & 32) != 0,
            complete, (flags & 1) != 0 && complete, info, path, legacy, downloaded, total,
            total > 0 && downloaded.HasValue ? Math.Clamp((double)downloaded.Value / total.Value, 0, 1) : null);
    }

    internal static string VisibilityName(int value) => value switch { 0 => "public", 1 => "friends-only", 2 => "private", 3 => "unlisted", _ => "unknown" };
    internal static void Require(bool success, string operation) { if (!success) throw new IOException(operation + " 返回 false"); }
    internal static string Utf8(byte[] value) => Encoding.UTF8.GetString(value, 0, Array.IndexOf(value, (byte)0) is var end && end >= 0 ? end : value.Length);
    public void Dispose()
    {
        try { if (initialized) { initialized = false; Bind<VoidCall>("SteamAPI_Shutdown")(); } }
        finally { if (library != IntPtr.Zero) { NativeLibrary.Free(library); library = IntPtr.Zero; } }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)] internal struct QueryCompleted
    {
        public ulong Handle; public int Result; public uint ReturnedResults; public uint TotalResults; public byte Cached;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public byte[] Cursor;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 8)] internal struct ItemDetails
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
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal delegate bool NativeBool();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void VoidCall();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate IntPtr InterfaceCall();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal delegate bool SelfBool(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate ulong SteamIdCall(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate uint AppIdCall(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal delegate bool CompletedCall(IntPtr self, ulong call, [MarshalAs(UnmanagedType.I1)] out bool failed);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal delegate bool ResultCall(IntPtr self, ulong call, IntPtr buffer, int size, int callback, [MarshalAs(UnmanagedType.I1)] out bool failed);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate ulong DetailsQuery(IntPtr self, [In] ulong[] ids, uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate ulong AllQuery(IntPtr self, int sort, int matching, uint creator, uint consumer, uint page);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate ulong SendQuery(IntPtr self, ulong query);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal delegate bool QueryUInt(IntPtr self, ulong query, uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal delegate bool QueryBool(IntPtr self, ulong query, [MarshalAs(UnmanagedType.I1)] bool value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal delegate bool QueryString(IntPtr self, ulong query, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal delegate bool ReadQuery(IntPtr self, ulong query, uint index, out ItemDetails details);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal delegate bool ReleaseQuery(IntPtr self, ulong query);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal delegate bool Statistic(IntPtr self, ulong query, uint index, int statistic, out ulong value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal delegate bool PreviewUrl(IntPtr self, ulong query, uint index, [Out] byte[] buffer, uint size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate uint ItemState(IntPtr self, ulong id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal delegate bool InstallInfo(IntPtr self, ulong id, out ulong size, [Out] byte[] folder, uint capacity, out uint timestamp);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal delegate bool DownloadInfo(IntPtr self, ulong id, out ulong downloaded, out ulong total);
}
