using System.Runtime.InteropServices;

namespace RimDevTools;

internal sealed record SteamworksNativeDiagnostics(string NativeLibraryPath)
{
    public string? InitializationEntryPoint { get; set; }
    public int? NativeInitResult { get; set; }
    public string? NativeError { get; set; }
    public Dictionary<string, string> InterfaceExports { get; init; } = [];
    public List<string> AttemptedExports { get; init; } = [];
    public List<string> MissingExports { get; init; } = [];
    public int ItemDetailsSize { get; set; }
    public int QueryCompletedSize { get; set; }
}

internal sealed class SteamworksNativeException(string code, string message, SteamworksNativeDiagnostics diagnostics)
    : Exception($"{message}; DLL={diagnostics.NativeLibraryPath}; attempted=[{string.Join(", ", diagnostics.AttemptedExports)}]; missing=[{string.Join(", ", diagnostics.MissingExports)}]")
{
    internal string Code { get; } = code;
    internal SteamworksNativeDiagnostics Diagnostics { get; } = diagnostics;
}

// Only explicitly audited flat API ABIs are accepted. Never guess a version suffix:
// UGC021 adds a uint64 to SteamUGCDetails_t and a bool to SetItemTags.
internal sealed class SteamworksNativeApi(Func<string, IntPtr> resolve, string nativePath)
{
    internal SteamworksNativeDiagnostics Diagnostics { get; } = new(Path.GetFullPath(nativePath));
    internal bool ModernUgc => Diagnostics.InterfaceExports["UGC"] == "SteamAPI_SteamUGC_v021";

    internal T? TryBind<T>(string name) where T : Delegate
    {
        if (!Diagnostics.AttemptedExports.Contains(name)) Diagnostics.AttemptedExports.Add(name);
        var address = resolve(name);
        if (address != IntPtr.Zero) return Marshal.GetDelegateForFunctionPointer<T>(address);
        if (!Diagnostics.MissingExports.Contains(name)) Diagnostics.MissingExports.Add(name);
        return null;
    }

    internal T Bind<T>(string name) where T : Delegate => TryBind<T>(name)
        ?? throw Failure("STEAMWORKS_API_UNSUPPORTED", "缺少原生入口: " + name);

    internal void SelectInterfaces()
    {
        Select("User", "SteamAPI_SteamUser_v023", "SteamAPI_SteamUser_v021");
        Select("Utils", "SteamAPI_SteamUtils_v010");
        Select("UGC", "SteamAPI_SteamUGC_v021", "SteamAPI_SteamUGC_v016");
        Select("Apps", "SteamAPI_SteamApps_v009", "SteamAPI_SteamApps_v008");
        Select("Friends", "SteamAPI_SteamFriends_v018", "SteamAPI_SteamFriends_v017");
        Diagnostics.ItemDetailsSize = ModernUgc ? 9784 : 9776;
        Diagnostics.QueryCompletedSize = 280;
        if (Marshal.SizeOf<SteamworksClient.ItemDetails>() != 9776
            || Marshal.SizeOf<SteamworksClient.ModernItemDetails>() != 9784
            || Marshal.SizeOf<SteamworksClient.QueryCompleted>() != Diagnostics.QueryCompletedSize)
            throw Failure("STEAMWORKS_ABI_UNSUPPORTED", "Steamworks Windows x64 ABI 布局不匹配");
    }

    private void Select(string kind, params string[] candidates)
    {
        foreach (var name in candidates)
            if (TryBind<SteamworksClient.InterfaceCall>(name) is not null)
            {
                Diagnostics.InterfaceExports[kind] = name;
                return;
            }
        throw Failure("STEAMWORKS_API_UNSUPPORTED", $"没有受支持的 {kind} 接口，已尝试: {string.Join(", ", candidates)}");
    }

    internal IntPtr Interface(string kind)
    {
        var name = Diagnostics.InterfaceExports[kind];
        var pointer = Bind<SteamworksClient.InterfaceCall>(name)();
        if (pointer == IntPtr.Zero) throw Failure("STEAMWORKS_INTERFACE_UNAVAILABLE", name + " 返回空接口");
        return pointer;
    }

    internal void Initialize()
    {
        // Valve recommends InitFlat for dynamic bindings. ESteamAPIInitResult is int32;
        // SteamErrMsg is char[1024], encoded as UTF-8, and success is zero.
        if (TryBind<InitFlat>("SteamAPI_InitFlat") is { } flat)
        {
            Diagnostics.InitializationEntryPoint = "SteamAPI_InitFlat";
            var error = new byte[1024];
            var result = flat(error);
            Diagnostics.NativeInitResult = result;
            Diagnostics.NativeError = SteamworksClient.Utf8(error);
            if (result != 0) throw Failure("STEAMWORKS_INIT_FAILED", $"SteamAPI_InitFlat 失败: ESteamAPIInitResult={result}, {Diagnostics.NativeError}");
            return;
        }
        if (TryBind<SteamworksClient.NativeBool>("SteamAPI_Init") is { } legacy)
        {
            Diagnostics.InitializationEntryPoint = "SteamAPI_Init";
            if (!legacy()) throw Failure("STEAMWORKS_INIT_FAILED", "SteamAPI_Init 失败；请检查 Steam 登录、游戏许可与进程权限");
            return;
        }
        throw Failure("STEAMWORKS_API_UNSUPPORTED", "没有受支持的初始化入口，已尝试 SteamAPI_InitFlat、SteamAPI_Init");
    }

    internal SteamworksNativeException Failure(string code, string message) => new(code, message, Diagnostics);

    internal SteamworksClient.ItemDetails ReadDetails(IntPtr ugc, ulong query, uint index)
    {
        var read = Bind<SteamworksClient.ReadQueryBuffer>("SteamAPI_ISteamUGC_GetQueryUGCResult");
        var buffer = Marshal.AllocHGlobal(Diagnostics.ItemDetailsSize);
        try
        {
            SteamworksClient.Require(read(ugc, query, index, buffer), "GetQueryUGCResult");
            return Marshal.PtrToStructure<SteamworksClient.ItemDetails>(buffer);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    internal bool SetItemTags(IntPtr ugc, ulong update, ref SteamworksClient.TagArray tags)
        => ModernUgc
            ? Bind<SteamworksClient.ModernTagsCall>("SteamAPI_ISteamUGC_SetItemTags")(ugc, update, ref tags, false)
            : Bind<SteamworksClient.LegacyTagsCall>("SteamAPI_ISteamUGC_SetItemTags")(ugc, update, ref tags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int InitFlat([Out] byte[] error);
}
