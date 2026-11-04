using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RimDevTools;

internal sealed record SteamworksProbeResult
{
    public bool Initialized { get; init; }
    public bool UserLoggedOn { get; init; }
    public ulong SteamId { get; init; }
    public uint AppId { get; init; }
    public bool UgcAvailable { get; init; }
    public ulong? PublishedFileId { get; init; }
    public int? QueryResult { get; init; }
    public uint? ReturnedResults { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public bool Ready => Initialized && UserLoggedOn && SteamId != 0 && AppId == SteamworksProbe.RimWorldAppId
        && UgcAvailable && ErrorCode is null && (!PublishedFileId.HasValue || QueryResult == 1 && ReturnedResults == 1);
}

// 只读原型：不绑定 CreateItem、StartItemUpdate 或 SubmitItemUpdate。
internal static class SteamworksProbe
{
    internal const uint RimWorldAppId = 294100;

    public static SteamworksProbeResult Run(string nativePath, ulong? publishedFileId, TimeSpan timeout)
    {
        var result = new SteamworksProbeResult { PublishedFileId = publishedFileId };
        IntPtr library = IntPtr.Zero;
        NativeVoid? shutdown = null;
        var elapsed = Stopwatch.StartNew();
        try
        {
            library = NativeLibrary.Load(nativePath);
            var init = Bind<NativeBool>(library, "SteamAPI_Init");
            shutdown = Bind<NativeVoid>(library, "SteamAPI_Shutdown");
            if (!init()) return result with { ErrorCode = "STEAMWORKS_INIT_FAILED", Message = "SteamAPI_Init 失败；请检查 Steam 登录、游戏许可与进程权限" };
            result = result with { Initialized = true };
            var callbacks = Bind<NativeVoid>(library, "SteamAPI_RunCallbacks");
            var user = Bind<GetInterface>(library, "SteamAPI_SteamUser_v021")();
            var utils = Bind<GetInterface>(library, "SteamAPI_SteamUtils_v010")();
            var ugc = Bind<GetInterface>(library, "SteamAPI_SteamUGC_v016")();
            if (user == IntPtr.Zero || utils == IntPtr.Zero || ugc == IntPtr.Zero)
                return result with { ErrorCode = "STEAMWORKS_INTERFACE_UNAVAILABLE", Message = "Steam User、Utils 或 UGC 接口不可用" };
            var loggedOn = Bind<UserLoggedOn>(library, "SteamAPI_ISteamUser_BLoggedOn");
            var loginWait = TimeSpan.FromSeconds(Math.Min(3, timeout.TotalSeconds));
            while (!loggedOn(user) && elapsed.Elapsed < loginWait)
            {
                callbacks();
                Thread.Sleep(100);
            }
            result = result with
            {
                UserLoggedOn = loggedOn(user), SteamId = Bind<GetSteamId>(library, "SteamAPI_ISteamUser_GetSteamID")(user),
                AppId = Bind<GetAppId>(library, "SteamAPI_ISteamUtils_GetAppID")(utils), UgcAvailable = true
            };
            if (result.AppId != RimWorldAppId)
                return result with { ErrorCode = "STEAMWORKS_APP_ID_MISMATCH", Message = "Steamworks 实际 App ID 与 RimWorld 不一致" };
            if (!result.UserLoggedOn || result.SteamId == 0)
                return result with { ErrorCode = "STEAMWORKS_NOT_LOGGED_ON", Message = "Steamworks 已初始化，但当前用户没有在线认证" };
            if (!publishedFileId.HasValue)
                return result with { Message = "独立 Steamworks 会话验证通过；未执行工坊条目查询或上传" };

            var query = Bind<CreateDetailsQuery>(library, "SteamAPI_ISteamUGC_CreateQueryUGCDetailsRequest")(ugc, [publishedFileId.Value], 1);
            if (query is 0 or ulong.MaxValue)
                return result with { ErrorCode = "WORKSHOP_QUERY_INVALID", Message = "Steam 未能创建 UGC 查询句柄" };
            var release = Bind<ReleaseQuery>(library, "SteamAPI_ISteamUGC_ReleaseQueryUGCRequest");
            try
            {
                var call = Bind<SendQuery>(library, "SteamAPI_ISteamUGC_SendQueryUGCRequest")(ugc, query);
                if (call == 0) return result with { ErrorCode = "WORKSHOP_QUERY_INVALID", Message = "Steam 未能提交只读 UGC 查询" };
                var completed = Bind<IsCallCompleted>(library, "SteamAPI_ISteamUtils_IsAPICallCompleted");
                var getResult = Bind<GetCallResult>(library, "SteamAPI_ISteamUtils_GetAPICallResult");
                // Windows x64 Steamworks.NET 20.2.0 的 SteamUGCQueryCompleted_t，callback 3401。
                while (elapsed.Elapsed < timeout)
                {
                    callbacks();
                    if (completed(utils, call, out var failed))
                    {
                        if (failed || !getResult(utils, call, out var response, Marshal.SizeOf<QueryCompleted>(), 3401, out var ioFailure) || ioFailure)
                            return result with { ErrorCode = "WORKSHOP_QUERY_IO_FAILED", Message = "Steam UGC 查询异步结果读取失败" };
                        result = result with { QueryResult = response.Result, ReturnedResults = response.ReturnedResults };
                        if (response.Handle != query || response.Result != 1 || response.ReturnedResults != 1)
                            return result with { ErrorCode = "WORKSHOP_QUERY_FAILED", Message = "Steam UGC 查询未返回预期条目；参见 queryResult 与 returnedResults" };
                        return result with { Message = "独立会话与 UGC 异步查询通过；未验证作者权限，未执行上传" };
                    }
                    Thread.Sleep(100);
                }
                return result with { ErrorCode = "WORKSHOP_QUERY_TIMEOUT", Message = "等待 Steam UGC 查询回调超时" };
            }
            finally { release(ugc, query); }
        }
        catch (EntryPointNotFoundException ex) { return result with { ErrorCode = "STEAMWORKS_API_UNSUPPORTED", Message = ex.Message }; }
        catch (Exception ex) { return result with { ErrorCode = "STEAMWORKS_PROBE_FAILED", Message = ex.Message }; }
        finally
        {
            try { if (result.Initialized) shutdown?.Invoke(); }
            finally { if (library != IntPtr.Zero) NativeLibrary.Free(library); }
        }
    }

    private static T Bind<T>(IntPtr library, string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct QueryCompleted
    {
        public ulong Handle;
        public int Result;
        public uint ReturnedResults;
        public uint TotalResults;
        public byte CachedData;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public byte[] NextCursor;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)] private delegate bool NativeBool();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void NativeVoid();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetInterface();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)] private delegate bool UserLoggedOn(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong GetSteamId(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint GetAppId(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong CreateDetailsQuery(IntPtr self, [In] ulong[] ids, uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong SendQuery(IntPtr self, ulong query);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)] private delegate bool ReleaseQuery(IntPtr self, ulong query);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)] private delegate bool IsCallCompleted(IntPtr self, ulong call, [MarshalAs(UnmanagedType.I1)] out bool failed);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)] private delegate bool GetCallResult(IntPtr self, ulong call, out QueryCompleted response, int size, int callback, [MarshalAs(UnmanagedType.I1)] out bool failed);
}
