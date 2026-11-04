using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace RimDevTools;

internal sealed record SteamworksRequest(string NativePath, string Operation, int TimeoutSeconds = 15,
    string? Keyword = null, int Page = 1, ulong? Id = null, bool Wait = false, string? Language = null);
internal sealed record SteamworksResponse
{
    public bool SteamRunning { get; init; }
    public bool? RimWorldInLibrary { get; init; }
    public string WorkshopAvailability { get; init; } = "unknown";
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;
    public string Source { get; init; } = "not_checked";
    public string? CurrentUserSteamId { get; init; }
    public WorkshopQueryPage? Page { get; init; }
    public WorkshopDownloadState? Download { get; init; }
    public WorkshopSubscriptionResult? Subscription { get; init; }
    public string? ErrorCode { get; init; }
    public string? Reason { get; init; }
    public bool IsFinal { get; init; }
}

internal static class SteamworksSessionProbe
{
    internal static Func<ProjectContext?, Task<SteamworksResponse>>? ProbeOverride { get; set; }
    private const string Prefix = "RIMDEVTOOLS_STEAMWORKSHOPS=";
    internal static string? GameDirectory(ProjectContext? context)
        => SteamInstallationResolver.Resolve(context?.Config.RimWorldDir, context?.Config.WorkshopPaths ?? [], expectedGameVersion: context?.Config.GameVersion).RimWorldDirectory;

    internal static async Task<SteamworksResponse> RunAsync(ProjectContext? context, string operation = "probe", string? keyword = null,
        int page = 1, ulong? id = null, int seconds = 15, CancellationToken cancellationToken = default, bool wait = false)
    {
        if (ProbeOverride is not null) return await ProbeOverride(context);
        var running = SteamSessionGuard.CheckForLaunch().IsRunning;
        if (!running) return new() { ErrorCode = "STEAM_NOT_RUNNING", Reason = "Steam 未启动", WorkshopAvailability = "unavailable" };
        try
        {
            var game = GameDirectory(context);
            if (game is null) return Failed("RIMWORLD_DIR_NOT_FOUND", "未找到对应游戏目录");
            var native = new[] { Path.Combine(game, "steam_api64.dll"), Path.Combine(game, "RimWorldWin64_Data", "Plugins", "x86_64", "steam_api64.dll") }.FirstOrDefault(File.Exists);
            if (native is null) return Failed("STEAMWORKS_LIBRARY_NOT_FOUND", "未找到 steam_api64.dll");
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            {
                WorkingDirectory = game, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8
            };
            if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(typeof(CliApplication).Assembly.Location);
            start.ArgumentList.Add("--steamworkshops-helper");
            start.Environment["SteamAppId"] = "294100";
            start.Environment["SteamGameId"] = "294100";
            using var helper = Process.Start(start) ?? throw new IOException("无法启动 Steamworks helper");
            var stdout = helper.StandardOutput.ReadToEndAsync();
            var stderr = helper.StandardError.ReadToEndAsync();
            await helper.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new SteamworksRequest(native, operation, Math.Max(1, seconds - 1), keyword, page, id, wait), JsonDefaults.Storage));
            helper.StandardInput.Close();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
            try { await helper.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                if (!helper.HasExited) helper.Kill(true);
                await helper.WaitForExitAsync(CancellationToken.None);
                var partial = await stdout; await stderr;
                var last = partial.Split('\n').LastOrDefault(x => x.StartsWith(Prefix, StringComparison.Ordinal));
                if (last is not null && JsonSerializer.Deserialize<SteamworksResponse>(last[Prefix.Length..], JsonDefaults.Storage) is { } latest)
                    return latest with { ErrorCode = cancellationToken.IsCancellationRequested ? "CANCELLED" : "STEAMWORKS_TIMEOUT", Reason = "本地等待已停止；保留最后观察到的状态，订阅未撤销", CheckedAt = DateTimeOffset.UtcNow };
                if (cancellationToken.IsCancellationRequested) return Failed("CANCELLED", "本地等待已取消；远端动作可能已经发出，请使用 status 复核");
                return Failed("STEAMWORKS_TIMEOUT", "Steamworks helper 超时并已退出");
            }
            var output = await stdout;
            var diagnostic = await stderr;
            var line = output.Split('\n').LastOrDefault(x => x.StartsWith(Prefix, StringComparison.Ordinal));
            var response = line is null ? null : JsonSerializer.Deserialize<SteamworksResponse>(line[Prefix.Length..], JsonDefaults.Storage);
            return ValidateHelperResponse(response, helper.ExitCode, diagnostic, running);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Failed("STEAMWORKS_PROBE_FAILED", ex.Message); }
        SteamworksResponse Failed(string code, string reason) => new() { SteamRunning = running, ErrorCode = code, Reason = reason };
    }

    internal static int RunHelper()
    {
        Console.InputEncoding = Encoding.UTF8;
        var request = JsonSerializer.Deserialize<SteamworksRequest>(Console.ReadLine() ?? "", JsonDefaults.Storage)
            ?? throw new InvalidDataException("缺少 Steamworks 请求");
        var response = new SteamworksResponse { SteamRunning = true };
        try
        {
            using var client = new SteamworksClient(request.NativePath, request.TimeoutSeconds);
            response = response with { RimWorldInLibrary = client.InLibrary, CurrentUserSteamId = client.SteamId, Source = "native_session" };
            // Probe independently of the selected (possibly private or deleted) item.
            var page = client.Query(request.Operation == "search" ? request.Keyword : null, request.Page,
                request.Operation is "detail" or "subscribe" or "observe" or "subscribe-verify-download" ? request.Id : null, request.Language);
            response = response with { WorkshopAvailability = "available", Source = "native_live_query", Page = request.Operation == "probe" ? null : page,
                Download = request.Operation == "detail" && request.Id.HasValue ? client.DownloadState(request.Id.Value) : null };
            if (request.Operation is "subscribe" or "observe" or "subscribe-verify-download" && request.Id.HasValue)
            {
                (uint, bool, bool, bool)? previous = null;
                var subscription = WorkshopSubscriptionService.Run(client, request.Id.Value, request.Operation != "observe", request.Wait, value =>
                {
                    var state = (value.Current.Flags, value.SubscribeConfirmed, value.DownloadRequested, value.DownloadCallbackMatched);
                    if (previous == state) return;
                    previous = state;
                    Console.WriteLine(Prefix + JsonSerializer.Serialize(response with { Subscription = value, Download = value.Current, CheckedAt = DateTimeOffset.UtcNow }, JsonDefaults.Storage));
                }, forceDownload: request.Operation == "subscribe-verify-download");
                response = response with { Subscription = subscription, Download = subscription.Current, ErrorCode = subscription.ErrorCode, Reason = subscription.Reason };
            }
        }
        catch (Exception ex)
        {
            response = response with { ErrorCode = ex is TimeoutException ? "STEAMWORKS_TIMEOUT" : "STEAMWORKS_QUERY_FAILED", Reason = ex.Message };
        }
        Console.WriteLine(Prefix + JsonSerializer.Serialize(response with { CheckedAt = DateTimeOffset.UtcNow, IsFinal = true }, JsonDefaults.Storage));
        return 0;
    }

    internal static SteamworksResponse ValidateHelperResponse(SteamworksResponse? response, int exitCode, string diagnostic, bool running)
    {
        if (response is null) return new() { SteamRunning = running, ErrorCode = "STEAMWORKS_HELPER_FAILED", Reason = $"helper exit={exitCode}，没有最终响应: {diagnostic.Trim()}" };
        return exitCode == 0 && response.IsFinal ? response : response with { ErrorCode = "STEAMWORKS_HELPER_FAILED", Reason = $"helper exit={exitCode} 或缺少最终响应；保留已观察状态，不把进度当成成功" };
    }

    internal static void WriteSummary(SteamworksResponse value)
        => Console.Error.WriteLine($"Steam：{(value.SteamRunning ? "已启动" : "未启动")} | RimWorld：{(value.RimWorldInLibrary == true ? "库中可用" : value.RimWorldInLibrary == false ? "库中不可用" : "未确认")} | Workshop：{(value.WorkshopAvailability == "available" ? "在线可查询" : "未确认（" + value.Reason + "）")}");

    internal static DoctorCheck[] Checks(SteamworksResponse value) =>
    [
        new("steam.runtime", value.SteamRunning ? "available" : "warning", false, null, value.SteamRunning ? null : "Steam 未启动", new { value.CheckedAt, source = "process" }),
        new("steam.rimworld_library", value.RimWorldInLibrary == true ? "available" : "warning", false, null, value.RimWorldInLibrary == true ? null : value.Reason ?? "库中不可用", new { value.CheckedAt, source = "BIsSubscribed", value.RimWorldInLibrary }),
        new("steam.workshop_online", value.WorkshopAvailability == "available" ? "available" : "warning", false, null, value.Reason, new { value.CheckedAt, value.Source, value.WorkshopAvailability })
    ];
}
