using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace RimDevTools;

internal static class WorkshopPermissionEvaluator
{
    internal static object Evaluate(string? owner, string? current)
    {
        bool? permitted = owner is null || current is null || owner == "0" || current == "0" ? null : owner == current;
        return new
        {
            ownerSteamId = owner, currentUserSteamId = current,
            userRole = permitted == true ? "owner" : permitted == false ? "not_owner" : "unknown",
            canUpdateContent = permitted, canEditDescription = permitted, canEditPreview = permitted, canChangeVisibility = permitted,
            permissionEvidence = permitted is null ? "未取得可核验的账号及远端所有者" : "Steam 远端 ownerSteamId 与当前 SteamID 比较；最终权限仍以提交结果为准",
            contributorCheck = "not_performed/deferred"
        };
    }
}

public static class SteamWorkshopsCommand
{
    // Preserve meaningful null (unknown) fields within Workshop results despite the legacy CLI null policy.
    internal static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, JsonDefaults.Storage);
    internal static ProjectContext? FindProject(string[] args, out CliError? error)
        => ProjectDiscovery.Find(CliArgs.Value(args, "--project") ?? Environment.CurrentDirectory, out error);

    internal static ulong? ParseId(string value)
    {
        if (ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id is not (0 or ulong.MaxValue)) return id;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"
            && uri.Host.Equals("steamcommunity.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.TrimEnd('/').Equals("/sharedfiles/filedetails", StringComparison.OrdinalIgnoreCase))
        {
            var pair = uri.Query.TrimStart('?').Split('&').FirstOrDefault(x => x.StartsWith("id=", StringComparison.OrdinalIgnoreCase));
            if (pair is not null && ulong.TryParse(pair[3..], NumberStyles.None, CultureInfo.InvariantCulture, out id) && id is not (0 or ulong.MaxValue)) return id;
        }
        return null;
    }

    public static async Task<int> RunAsync(string[] args, CancellationToken token)
    {
        var command = CliApplication.CommandPath(args);
        var context = FindProject(args, out var discovery);
        if (discovery?.Code == "CONFIG_INVALID") return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, discovery.Code, discovery.Message);
        if (CliArgs.Has(args, "--project") && context is null)
            return CliResult.WriteFailure(command, ExitCodes.MissingDependency, discovery!.Code, discovery.Message);
        try
        {
            return args[1] switch
            {
                "search" => await SearchAsync(args, context, token),
                "status" => await StatusAsync(args, context, token),
                "upload" => context is null ? CliResult.WriteFailure(command, ExitCodes.MissingDependency, "PROJECT_NOT_FOUND", "上传需要项目上下文")
                    : WorkshopPublishingService.Write(command, context, await WorkshopPublishingService.UploadAsync(context, args, token)),
                "edit" => WorkshopPublishingService.Write(command, context, await WorkshopPublishingService.EditAsync(context, args, token)),
                _ => CliResult.WriteFailure(command, ExitCodes.TaskFailed, "WORKSHOP_FEATURE_NOT_IMPLEMENTED", "此写入功能将在后续批次交付；未执行远端操作", context?.Root)
            };
        }
        catch (WorkshopSearchException ex) { return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, ex.Code, ex.Message, context?.Root); }
        catch (InvalidDataException ex) { return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "WORKSHOP_BINDING_INVALID", ex.Message, context?.Root); }
        catch (JsonException ex) { return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "WORKSHOP_BINDING_INVALID", "项目运行元数据无效: " + ex.Message, context?.Root); }
    }

    private static async Task<int> SearchAsync(string[] args, ProjectContext? context, CancellationToken token)
    {
        const string command = "steamworkshops search";
        var value = CliArgs.Positionals(command, args).Single();
        if (ParseId(value) is { } id)
        {
            if (CliArgs.Has(args, "--page") || CliArgs.Has(args, "--details")) throw new ArgumentException("精确 ID/链接查询不接受分页或 --details");
            return await StatusAsync(args, context, token, id, command);
        }
        if (value.All(char.IsDigit) || value.Contains("://", StringComparison.Ordinal)) throw new ArgumentException("无效的工坊 ID/链接");
        var next = value.Equals("next", StringComparison.OrdinalIgnoreCase);
        if (next && (CliArgs.Has(args, "--page") || CliArgs.Has(args, "--details"))) throw new ArgumentException("search next 不接受 --page 或 --details");
        var now = DateTimeOffset.UtcNow;
        WorkshopSearchCache cache;
        int page;
        if (next)
        {
            cache = WorkshopSearchPages.Read(context, now);
            // Actual exhaustion precedes the artificial cap.
            if ((long)cache.CurrentPage * cache.PageSize >= cache.TotalMatchingResults)
                return CliResult.WriteSuccess(command, context?.Root, Json(WorkshopSearchPages.Output(cache, cache.CurrentPage,
                    cache.Pages.Values.First(), "cache", end: true)));
            page = cache.CurrentPage + 1;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("搜索关键词不能为空");
            if (!CliArgs.TryPositiveInt(args, "--page", 1, out page, out var error)) throw new ArgumentException(error);
            var map = WorkshopSearchPages.Map(CliArgs.Has(args, "--details"), page);
            cache = new(1, context?.Root ?? "", SteamworksClient.AppId, value, CliArgs.Has(args, "--details"), page, map.Size, map.Max,
                0, now, now.AddMinutes(10), []);
        }
        var mapping = WorkshopSearchPages.Map(cache.Details, page);
        var source = "cache";
        if (!cache.Pages.TryGetValue(mapping.Native, out var native))
        {
            var response = await SteamworksSessionProbe.RunAsync(context, "search", cache.Keyword, mapping.Native, cancellationToken: token);
            if (response.ErrorCode is not null) return QueryFailure(command, context, response);
            native = response.Page!;
            cache.Pages[mapping.Native] = native;
            cache = cache with { TotalMatchingResults = native.TotalMatchingResults };
            source = "online";
        }
        cache = cache with { CurrentPage = page };
        if (context is not null) WorkshopSearchPages.Save(context, cache);
        return CliResult.WriteSuccess(command, context?.Root, Json(WorkshopSearchPages.Output(cache, page, native, source)));
    }

    private static async Task<int> StatusAsync(string[] args, ProjectContext? context, CancellationToken token, ulong? explicitId = null, string command = "steamworkshops status")
    {
        var positional = command == "steamworkshops status" ? CliArgs.Positionals(command, args).SingleOrDefault() : null;
        if (explicitId is null && positional is not null) explicitId = ParseId(positional) ?? throw new ArgumentException("无效的工坊 ID/链接");
        if (context is null && explicitId is null) return CliResult.WriteFailure(command, ExitCodes.MissingDependency, "PROJECT_NOT_FOUND", "请进入项目、使用 --project 或指定条目 ID");
        var health = await SteamworksSessionProbe.RunAsync(context, seconds: 10, cancellationToken: token);
        SteamworksSessionProbe.WriteSummary(health);
        string? localId = null, deployedId = null, deployedPackageId = null, known = null, receiptId = null, package = null, modName = null;
        var consistent = true;
        var idSource = explicitId.HasValue ? "explicit" : "unpublished";
        if (context is not null && !explicitId.HasValue)
        {
            var about = XDocument.Load(Path.Combine(context.Root, "About", "About.xml")).Root!;
            package = about.Element("packageId")?.Value.Trim(); modName = about.Element("name")?.Value.Trim();
            localId = WorkshopUploadCommand.ReadId(Path.Combine(context.Root, "About", "PublishedFileId.txt"))?.ToString();
            var game = SteamworksSessionProbe.GameDirectory(context);
            if (game is not null)
            {
                var target = Path.GetFullPath(Path.Combine(game, "Mods", context.Config.ModName ?? ""));
                if (PathSafety.IsWithin(target, Path.Combine(game, "Mods")))
                {
                    var deployedAbout = Path.Combine(target, "About", "About.xml");
                    if (File.Exists(deployedAbout))
                    {
                        deployedPackageId = XDocument.Load(deployedAbout).Root?.Element("packageId")?.Value.Trim();
                        deployedId = WorkshopUploadCommand.ReadId(Path.Combine(target, "About", "PublishedFileId.txt"))?.ToString();
                    }
                }
            }
            known = DeploymentReceiptStore.KnownBinding(context);
            var receipt = DeploymentReceiptStore.Read(context);
            if (receipt is not null && receipt.PackageId == package) receiptId = receipt.PublishedFileId;
            consistent = new[] { localId, deployedId, known, receiptId }.Where(x => x is not null).Distinct().Count() <= 1
                && (localId is not null || known is null && deployedId is null && receiptId is null)
                && (deployedPackageId is null || deployedPackageId == package);
        }
        var selected = explicitId?.ToString() ?? localId ?? known ?? receiptId ?? (deployedPackageId == package ? deployedId : null);
        if (!explicitId.HasValue && selected is not null) idSource = localId is not null ? "project_About/PublishedFileId.txt" : known is not null ? "known_binding_missing_project_file" : receiptId is not null ? "receipt_missing_project_file" : "deployed_binding_missing_project_file";
        SteamworksResponse? detail = null;
        if (selected is not null && health.WorkshopAvailability == "available")
            detail = await SteamworksSessionProbe.RunAsync(context, "detail", id: ulong.Parse(selected, CultureInfo.InvariantCulture), cancellationToken: token);
        var item = detail?.Page?.Items.SingleOrDefault();
        var result = Json(new
        {
            health.SteamRunning, health.RimWorldInLibrary, health.WorkshopAvailability, health.CheckedAt, health.Source,
            environmentReason = health.Reason, modName, packageId = package, publishedFileId = selected, idSource,
            bindingConsistent = consistent, bindingSources = new { project = localId, deployed = deployedId, deployedPackageId, known, receipt = receiptId },
            publicationState = selected is null ? "unpublished" : item is null ? "unknown" : "published",
            title = item?.Title, description = item?.Description, descriptionMode = "full", visibility = item?.Visibility,
            subscriptionCount = item?.SubscriptionCount, subscriptionCountReason = item?.SubscriptionCountReason ?? (item is null ? "条目不可得" : null),
            lastUpdatedAtUnix = item?.LastUpdatedAtUnix, lastUpdatedAt = item?.LastUpdatedAt, lastUpdatedAtLocal = item?.LastUpdatedAtLocal,
            lastUpdatedAtReason = item?.LastUpdatedAtUnix is null ? detail?.Reason ?? "未发布或 Steam 未提供有效时间" : null,
            permissions = WorkshopPermissionEvaluator.Evaluate(item?.OwnerSteamId, detail?.CurrentUserSteamId ?? health.CurrentUserSteamId),
            download = detail?.Download, item?.FileBytes, item?.PreviewUrl, item?.Tags,
            partial = selected is not null && (item is null || item.SubscriptionCount is null || item.LastUpdatedAtUnix is null),
            queryReason = detail?.Reason
        });
        if (health.ErrorCode is not null || detail?.ErrorCode is not null)
            return CliResult.WriteFailure(command, ExitCodes.TaskFailed, detail?.ErrorCode ?? health.ErrorCode!, detail?.Reason ?? health.Reason!, context?.Root, result);
        if (CliArgs.Has(args, "--wait")) return CliResult.WriteFailure(command, ExitCodes.TaskFailed, "WORKSHOP_FEATURE_NOT_IMPLEMENTED", "下载等待将在第三批交付；当前状态已查询，未触发订阅或下载", context?.Root, result);
        return CliResult.WriteSuccess(command, context?.Root, result);
    }

    internal static int QueryFailure(string command, ProjectContext? context, SteamworksResponse response)
        => CliResult.WriteFailure(command, response.ErrorCode == "STEAMWORKS_TIMEOUT" ? ExitCodes.Timeout : ExitCodes.TaskFailed,
            response.ErrorCode!, response.Reason ?? "Steamworks 查询失败", context?.Root, Json(response));

}
