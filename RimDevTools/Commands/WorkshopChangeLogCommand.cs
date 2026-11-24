using System.Diagnostics;
using System.Globalization;

namespace RimDevTools;

internal static class WorkshopChangeLogCommand
{
    internal static async Task<int> RunAsync(string[] args, ProjectContext? context, CancellationToken token)
    {
        const string command = "steamworkshops changelog";
        var rawId = CliArgs.Positionals(command, args).SingleOrDefault();
        var id = rawId is not null ? SteamWorkshopsCommand.ParseId(rawId)
            ?? throw new ArgumentException("无效的工坊 ID/链接")
            : context is not null ? WorkshopUploadCommand.ReadId(Path.Combine(context.Root, "About", "PublishedFileId.txt")) : null;
        if (id is null) return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "WORKSHOP_ID_REQUIRED", "请指定条目 ID 或进入已发布项目", context?.Root);
        var language = WorkshopChangeLog.Language(args);
        if (!CliArgs.TryPositiveInt(args, "--page", 1, out var pageNumber, out var pageError) || pageNumber > 100)
            throw new ArgumentException(pageError ?? "页码不能超过 100");
        if (!CliArgs.TryPositiveInt(args, "--timeout-seconds", 60, out var seconds, out var timeoutError) || seconds > 600)
            throw new ArgumentException(timeoutError ?? "复查等待不能超过 600 秒");
        var verify = CliArgs.Has(args, "--verify");
        var supplied = CliArgs.Has(args, "--change-note") || CliArgs.Has(args, "--change-note-file");
        if (!verify && (supplied || CliArgs.Has(args, "--wait") || CliArgs.Has(args, "--timeout-seconds")))
            throw new ArgumentException("日志预期文本、--wait 和 --timeout-seconds 需要 --verify");
        var record = CliArgs.Value(args, "--record");
        if (record is not null && (!ulong.TryParse(record, NumberStyles.None, CultureInfo.InvariantCulture, out var key) || key == 0))
            throw new ArgumentException("--record 需要日志查询返回的数字 recordId");
        WorkshopChangeLogBaseline? baseline = null;
        string? expected = null;
        if (verify)
        {
            if (supplied) expected = WorkshopMetadata.ChangeNote(args);
            else
            {
                baseline = context is null ? null : WorkshopChangeLog.ReadBaseline(context);
                if (baseline is null) return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "WORKSHOP_CHANGELOG_BASELINE_REQUIRED",
                    "没有本项目最近一次提交的日志基线；请显式提供 --change-note/--change-note-file，或先执行一次上传", context?.Root);
                if (baseline.PublishedFileId != id.Value.ToString(CultureInfo.InvariantCulture))
                    return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "WORKSHOP_CHANGELOG_BASELINE_MISMATCH", "复查基线不属于目标条目，不能交叉使用", context?.Root);
                expected = baseline.Text;
            }
        }
        WorkshopChangeLogPage? page = null;
        WorkshopChangeLogVerification? verification = null;
        var stopwatch = Stopwatch.StartNew();
        var delaySeconds = 2;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(seconds));
        try
        {
            do
            {
                if (!verify || !string.IsNullOrWhiteSpace(expected))
                    page = await WorkshopChangeLog.ReadAsync(id.Value.ToString(CultureInfo.InvariantCulture), language, pageNumber, budget.Token);
                if (!verify) break;
                verification = WorkshopChangeLog.Verify(page, expected, baseline, record);
                if (!CliArgs.Has(args, "--wait") || verification.Status is not ("pending" or "mismatch")) break;
                if (stopwatch.Elapsed + TimeSpan.FromSeconds(delaySeconds) >= TimeSpan.FromSeconds(seconds)) break;
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), budget.Token);
                delaySeconds = Math.Min(delaySeconds * 2, 15);
            } while (!budget.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            verification = verification is { Status: "pending" or "mismatch" } ? verification
                : new("unknown", "rendered_text", expected, null, "复查等待已到上限；未重发上传");
        }
        var result = SteamWorkshopsCommand.Json(new
        {
            publishedFileId = id.Value.ToString(CultureInfo.InvariantCulture), language, page = pageNumber,
            source = "steamcommunity_live_http", cachedData = false, checkedAt = page?.CheckedAt ?? DateTimeOffset.UtcNow,
            url = WorkshopChangeLog.Url(id.Value.ToString(CultureInfo.InvariantCulture), language, pageNumber),
            status = page?.Status ?? verification?.Status ?? "unknown", page?.HttpStatus, page?.Reason,
            entries = record is null ? page?.Entries : page?.Entries.Where(x => x.RecordId == record).ToArray(),
            verification, operationId = baseline?.OperationId, remoteOutcome = baseline?.RemoteOutcome,
            historicalEditing = "unsupported_by_existing_game_and_steamworks_interfaces",
            artifacts = baseline is not null && context is not null ? new[] { WorkshopChangeLog.PathFor(context) } : Array.Empty<string>()
        });
        if (verify && verification?.Status is "passed" or "skipped_empty" || !verify && page?.Status == "available")
            return CliResult.WriteSuccess(command, context?.Root, result);
        var code = verify && verification?.Status is "mismatch" ? "WORKSHOP_CHANGELOG_MISMATCH"
            : verify && verification?.Status is "ambiguous" ? "WORKSHOP_CHANGELOG_AMBIGUOUS"
            : page?.Status == "rate_limited" ? "WORKSHOP_CHANGELOG_RATE_LIMITED"
            : verification?.Status == "pending" ? "WORKSHOP_CHANGELOG_PENDING" : "WORKSHOP_CHANGELOG_UNAVAILABLE";
        return CliResult.WriteFailure(command, verification?.Status == "pending" && CliArgs.Has(args, "--wait") ? ExitCodes.Timeout : ExitCodes.TaskFailed,
            code, verification?.Reason ?? page?.Reason ?? "无法复查改动日志", context?.Root, result);
    }
}
