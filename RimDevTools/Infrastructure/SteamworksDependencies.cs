using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RimDevTools;

internal sealed record WorkshopModDependency(string PublishedFileId, string? Name, WorkshopDownloadState? Download, string? Reason);
internal sealed record WorkshopDlcDependency(uint AppId, string? Name, string? Reason);
internal sealed record WorkshopDependencies(string Status, WorkshopModDependency[]? ModDependencies, WorkshopDlcDependency[]? Dlc, string? Reason)
{
    public string Source => "steam_declared_direct";
}
internal sealed record WorkshopAuthor(string SteamId, string? Name, string? Reason);

internal sealed partial class SteamworksClient
{
    internal WorkshopAuthor Author(string id)
    {
        try
        {
            var friends = Bind<InterfaceCall>("SteamAPI_SteamFriends_v017")();
            if (friends == IntPtr.Zero) throw new IOException("SteamFriends 不可用");
            var owner = ulong.Parse(id, System.Globalization.CultureInfo.InvariantCulture);
            _ = Bind<RequestUser>("SteamAPI_ISteamFriends_RequestUserInformation")(friends, owner, true);
            var watch = Stopwatch.StartNew();
            do
            {
                Pump();
                var name = Marshal.PtrToStringUTF8(Bind<PersonaName>("SteamAPI_ISteamFriends_GetFriendPersonaName")(friends, owner));
                if (!string.IsNullOrWhiteSpace(name) && name != "[unknown]") return new(id, name, null);
                Thread.Sleep(50);
            } while (HasTimeRemaining && watch.Elapsed < TimeSpan.FromSeconds(2));
            return new(id, null, "Steam 尚未返回发布者昵称");
        }
        catch (Exception ex) { return new(id, null, ex.Message); }
    }

    internal WorkshopDependencies Dependencies(WorkshopItemInfo item)
        => ResolveDependencies(item, ids => Query(ids: ids).Items, id => DownloadState(id), () =>
        {
            var ids = AppDependencies(ulong.Parse(item.PublishedFileId), out var reason);
            return (ids, reason);
        }, DlcNames);

    internal static WorkshopDependencies ResolveDependencies(WorkshopItemInfo item, Func<ulong[], WorkshopItemInfo[]> queryNames,
        Func<ulong, WorkshopDownloadState> downloadState, Func<(uint[] Ids, string? Reason)> appDependencies, Func<IReadOnlyDictionary<uint, string>> getDlcNames)
    {
        // PublishedFileType Collection=2: children are collection members, not required mods.
        if (item.FileType == 2) return new("not_applicable", [], [], "集合成员不解释为模组前置");
        var reasons = new List<string>();
        var ids = item.Children;
        if (ids is null) reasons.Add(item.ChildrenReason ?? "未取得工坊前置 ID");
        WorkshopItemInfo[] names = [];
        if (ids?.Length > 0)
        {
            try { names = queryNames(ids); }
            catch (Exception ex) { reasons.Add("前置名称查询失败: " + ex.Message); }
        }
        var mods = ids?.Distinct().Select(id =>
        {
            var name = names.FirstOrDefault(x => x.PublishedFileId == id.ToString())?.Title;
            string? reason = name is null ? "Steam 未返回前置名称" : null;
            WorkshopDownloadState? state = null;
            try { state = downloadState(id); }
            catch (Exception ex) { reason = (reason is null ? "" : reason + "; ") + "安装状态未知: " + ex.Message; }
            return new WorkshopModDependency(id.ToString(System.Globalization.CultureInfo.InvariantCulture), name, state, reason);
        }).ToArray();
        WorkshopDlcDependency[]? dlc = null;
        try
        {
            var (appIds, appReason) = appDependencies();
            if (appReason is not null) reasons.Add(appReason);
            IReadOnlyDictionary<uint, string> dlcNames = new Dictionary<uint, string>();
            try { dlcNames = getDlcNames(); }
            catch (Exception ex) { reasons.Add("DLC 名称查询失败，保留已知 App ID: " + ex.Message); }
            dlc = appIds.Select(id => dlcNames.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name)
                ? new WorkshopDlcDependency(id, name, null) : new(id, null, "Steam DLC 清单未返回此 App 的名称")).ToArray();
        }
        catch (Exception ex) { reasons.Add("DLC 依赖查询失败: " + ex.Message); }
        var partial = reasons.Count > 0 || mods?.Any(x => x.Reason is not null) == true || dlc?.Any(x => x.Reason is not null) == true;
        return new(partial ? "partial" : "complete", mods, dlc, reasons.Count > 0 ? string.Join("; ", reasons) : null);
    }

    private IReadOnlyDictionary<uint, string> DlcNames()
    {
        var apps = Bind<InterfaceCall>("SteamAPI_SteamApps_v008")();
        var result = new Dictionary<uint, string> { [AppId] = "RimWorld" };
        var count = Bind<DlcCount>("SteamAPI_ISteamApps_GetDLCCount")(apps);
        for (var i = 0; i < count; i++)
        {
            var buffer = new byte[4096];
            if (Bind<DlcInfo>("SteamAPI_ISteamApps_BGetDLCDataByIndex")(apps, i, out var appId, out _, buffer, buffer.Length))
                result[appId] = Utf8(buffer);
        }
        return result;
    }

    private uint[] AppDependencies(ulong id, out string? reason)
    {
        var first = Wait<AppDependenciesResult>(Bind<AppDependenciesCall>("SteamAPI_ISteamUGC_GetAppDependencies")(Ugc, id), 3416);
        if (first.Result != 1 || first.Id != id || first.Count > 32) throw new IOException("GetAppDependencies 未成功: EResult=" + first.Result);
        var ids = new HashSet<uint>(first.AppIds.Take((int)first.Count));
        var watch = Stopwatch.StartNew();
        while (ids.Count < first.Total && HasTimeRemaining && watch.Elapsed < TimeSpan.FromSeconds(2))
        {
            Pump();
            foreach (var packet in appDependencyCallbacks.Where(x => x.Id == id && x.Result == 1 && x.Count <= 32))
                ids.UnionWith(packet.AppIds.Take((int)packet.Count));
            if (ids.Count < first.Total) Thread.Sleep(50);
        }
        reason = ids.Count < first.Total ? $"Steam 仅返回 {ids.Count}/{first.Total} 个 App 依赖，保留已知 ID" : null;
        return ids.ToArray();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)] internal struct AppDependenciesResult
    {
        public int Result; public ulong Id;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public uint[] AppIds;
        public uint Count; public uint Total;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong AppDependenciesCall(IntPtr self, ulong id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DlcCount(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool DlcInfo(IntPtr self, int index, out uint id, [MarshalAs(UnmanagedType.I1)] out bool available, [Out] byte[] name, int capacity);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool RequestUser(IntPtr self, ulong id, [MarshalAs(UnmanagedType.I1)] bool nameOnly);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr PersonaName(IntPtr self, ulong id);
}
