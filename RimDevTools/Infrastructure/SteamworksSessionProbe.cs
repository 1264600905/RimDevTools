using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace RimDevTools;

internal sealed record SteamworksRequest(string NativePath, string Operation, int TimeoutSeconds = 15,
    string? Keyword = null, int Page = 1, ulong? Id = null);
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
    public string? ErrorCode { get; init; }
    public string? Reason { get; init; }
}

internal static class SteamworksSessionProbe
{
    internal static Func<ProjectContext?, Task<SteamworksResponse>>? ProbeOverride { get; set; }
    private const string Prefix = "RIMDEVTOOLS_STEAMWORKSHOPS=";
    internal static string? GameDirectory(ProjectContext? context)
        => SteamInstallationResolver.Resolve(context?.Config.RimWorldDir, context?.Config.WorkshopPaths ?? [], expectedGameVersion: context?.Config.GameVersion).RimWorldDirectory;

    internal static async Task<SteamworksResponse> RunAsync(ProjectContext? context, string operation = "probe", string? keyword = null,
        int page = 1, ulong? id = null, int seconds = 15, CancellationToken cancellationToken = default)
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
            await helper.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new SteamworksRequest(native, operation, seconds, keyword, page, id), JsonDefaults.Storage));
            helper.StandardInput.Close();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
            try { await helper.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                if (!helper.HasExited) helper.Kill(true);
                await helper.WaitForExitAsync(CancellationToken.None);
                await stdout; await stderr;
                cancellationToken.ThrowIfCancellationRequested();
                return Failed("STEAMWORKS_TIMEOUT", "Steamworks helper 超时并已退出");
            }
            var output = await stdout;
            var diagnostic = await stderr;
            var line = output.Split('\n').LastOrDefault(x => x.StartsWith(Prefix, StringComparison.Ordinal));
            return line is null ? Failed("STEAMWORKS_HELPER_FAILED", $"helper exit={helper.ExitCode}: {diagnostic.Trim()}")
                : JsonSerializer.Deserialize<SteamworksResponse>(line[Prefix.Length..], JsonDefaults.Storage) ?? Failed("STEAMWORKS_HELPER_FAILED", "helper 返回空响应");
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
                request.Operation == "detail" ? request.Id : null);
            response = response with { WorkshopAvailability = "available", Source = "native_live_query", Page = request.Operation == "probe" ? null : page,
                Download = request.Operation == "detail" && request.Id.HasValue ? client.DownloadState(request.Id.Value) : null };
        }
        catch (Exception ex)
        {
            response = response with { ErrorCode = ex is TimeoutException ? "STEAMWORKS_TIMEOUT" : "STEAMWORKS_QUERY_FAILED", Reason = ex.Message };
        }
        Console.WriteLine(Prefix + JsonSerializer.Serialize(response with { CheckedAt = DateTimeOffset.UtcNow }, JsonDefaults.Storage));
        return 0;
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
