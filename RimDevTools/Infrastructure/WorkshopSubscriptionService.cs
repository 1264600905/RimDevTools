using System.Runtime.InteropServices;

namespace RimDevTools;

internal sealed record WorkshopDownloadCallback(uint AppId, ulong PublishedFileId, int Result);
internal sealed record WorkshopSubscriptionResult(string PublishedFileId, bool ObserveOnly, bool AlreadySubscribed,
    bool SubscribeConfirmed, int? SubscribeResult, bool DownloadRequested, bool DownloadCallbackMatched,
    int? DownloadCallbackResult, bool TimedOut, WorkshopDownloadState Current, uint[] ObservedFlags, string? Reason, string? ErrorCode)
{
    public WorkshopDownloadState? Initial { get; init; }
}

internal static class WorkshopSubscriptionService
{
    internal static bool Matches(WorkshopDownloadCallback callback, ulong id)
        => callback.AppId == SteamworksClient.AppId && callback.PublishedFileId == id;

    internal static WorkshopSubscriptionResult Run(SteamworksClient client, ulong id, bool subscribe, bool wait,
        Action<WorkshopSubscriptionResult> progress, bool forceDownload = false)
    {
        var state = client.DownloadState(id);
        var observed = new HashSet<uint> { state.Flags };
        var result = new WorkshopSubscriptionResult(id.ToString(System.Globalization.CultureInfo.InvariantCulture), !subscribe,
            state.Subscribed, state.Subscribed, null, false, false, null, false, state, observed.ToArray(), null, null) { Initial = state };
        try
        {
            progress(result);
            if (subscribe)
            {
                if (!client.InLibrary) throw new IOException("当前账号库中不可用 RimWorld");
                if (!state.Subscribed)
                {
                    var subscribed = client.Wait<SubscribeCompleted>(client.Bind<SubscribeCall>("SteamAPI_ISteamUGC_SubscribeItem")(client.Ugc, id), 1313);
                    result = result with { SubscribeResult = subscribed.Result, SubscribeConfirmed = subscribed.Result == 1 && subscribed.Id == id };
                    if (!result.SubscribeConfirmed) throw new IOException("SubscribeItem 回调失败或目标不匹配: EResult=" + subscribed.Result);
                    progress(result);
                }
                client.Pump(); state = client.DownloadState(id);
                if (!state.DownloadComplete || forceDownload)
                {
                    client.EnableDownloadCallbacks();
                    var requested = client.Bind<DownloadCall>("SteamAPI_ISteamUGC_DownloadItem")(client.Ugc, id, false);
                    result = result with { DownloadRequested = requested };
                    if (!requested) throw new IOException("DownloadItem 未接受下载请求；订阅结果保留");
                    progress(result);
                }
            }
            do
            {
                client.Pump();
                foreach (var callback in client.DrainDownloadCallbacks().Where(callback => Matches(callback, id)))
                {
                    result = result with { DownloadCallbackMatched = true, DownloadCallbackResult = callback.Result };
                    if (callback.Result != 1) throw new IOException("DownloadItemResult_t 失败: EResult=" + callback.Result);
                }
                // Never access installed content after our DownloadItem until its own matching callback.
                state = client.DownloadState(id, !result.DownloadRequested || result.DownloadCallbackMatched);
                observed.Add(state.Flags);
                result = result with { Current = state, ObservedFlags = observed.ToArray() };
                if (state.Ready && (!result.DownloadRequested || result.DownloadCallbackMatched)) return result;
                if (!wait) return result with { Reason = "已返回当前状态；使用 status 或 subscribe --wait 继续观察" };
                if (!subscribe && !state.Downloading && !state.DownloadPending)
                    return result with { Reason = "当前没有可等待的下载任务；没有发起订阅或下载" };
                progress(result);
                Thread.Sleep(100);
            } while (client.HasTimeRemaining);
            return result with { TimedOut = true, ErrorCode = "WORKSHOP_DOWNLOAD_TIMEOUT", Reason = "等待最新内容下载超时，已成功的订阅保留；观察到的回调 id:size=" + client.CallbackDiagnostics };
        }
        catch (Exception ex)
        {
            state = client.DownloadState(id, !result.DownloadRequested || result.DownloadCallbackMatched);
            observed.Add(state.Flags);
            return result with { Current = state, ObservedFlags = observed.ToArray(), TimedOut = ex is TimeoutException,
                ErrorCode = ex is TimeoutException ? "WORKSHOP_DOWNLOAD_TIMEOUT" : "WORKSHOP_SUBSCRIPTION_FAILED", Reason = ex.Message };
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)] private struct SubscribeCompleted { public int Result; public ulong Id; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong SubscribeCall(IntPtr self, ulong id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool DownloadCall(IntPtr self, ulong id, [MarshalAs(UnmanagedType.I1)] bool highPriority);
}
