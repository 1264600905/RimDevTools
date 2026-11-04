using System.Text.Json;
using System.Text.Json.Nodes;

namespace RimDevTools;

internal static class WorkshopStatusView
{
    internal static string DownloadState(WorkshopDownloadState? state) => state switch
    {
        null => "unknown",
        { Ready: true } => "ready",
        { Downloading: true } => "downloading",
        { DownloadPending: true } => "pending",
        { NeedsUpdate: true } => "needs_update",
        { Installed: false } => "not_installed",
        { DownloadComplete: true, Subscribed: false } => "installed_unsubscribed",
        _ => "unknown"
    };

    internal static object Download(WorkshopDownloadState? value, string? reason = null)
    {
        var state = DownloadState(value);
        var result = new JsonObject { ["subscribed"] = value?.Subscribed, ["state"] = state, ["ready"] = value?.Ready };
        if (value?.Ready == true) result["installPath"] = value.InstallPath;
        if (state is "downloading" or "pending" && value?.Progress is not null) result["progress"] = value.Progress;
        if (state == "unknown") result["reason"] = reason ?? (value is null ? "未取得下载状态" : "安装信息无效、旧式条目或目录不可用，不能判为就绪");
        return result;
    }

    internal static object Dependencies(WorkshopDependencies? value, bool verbose)
    {
        if (value is null) return new { status = "unknown", reason = "未取得工坊声明的直接依赖", modDependencies = (object?)null, dlc = (object?)null };
        if (verbose) return value;
        return new JsonObject
        {
            ["source"] = value.Source, ["status"] = value.Status,
            ["modDependencies"] = value.ModDependencies is null ? null : new JsonArray(value.ModDependencies.Select(mod =>
            {
                var entry = JsonSerializer.SerializeToNode(Download(mod.Download, mod.Reason), JsonDefaults.Storage)!.AsObject();
                entry.Remove("installPath"); entry.Remove("ready");
                entry["publishedFileId"] = mod.PublishedFileId; entry["name"] = mod.Name;
                if (mod.Reason is not null) entry["reason"] = mod.Reason;
                return (JsonNode)entry;
            }).ToArray()),
            ["dlc"] = JsonSerializer.SerializeToNode(value.Dlc, JsonDefaults.Storage),
            ["reason"] = value.Reason
        }.WithoutEmptyReasons();
    }

    internal static JsonElement Format(JsonElement raw, WorkshopDownloadState? download, WorkshopDependencies? dependencies, bool verbose)
    {
        var value = JsonNode.Parse(raw.GetRawText())!.AsObject();
        if (verbose) return raw;
        var compact = new JsonObject();
        foreach (var key in new[] { "publishedFileId", "publicationState", "checkedAt", "title", "author", "description", "visibility", "subscriptionCount", "fileBytes", "tags", "partial" })
            compact[key] = value[key]?.DeepClone();
        compact["lastUpdatedAt"] = value["lastUpdatedAtLocal"]?.DeepClone();
        if (compact["author"] is JsonObject author && author["reason"] is null) author.Remove("reason");
        var permissions = value["permissions"]!.AsObject();
        compact["permissions"] = new JsonObject
        {
            ["userRole"] = permissions["userRole"]?.DeepClone(), ["canUpdateContent"] = permissions["canUpdateContent"]?.DeepClone()
        };
        if (permissions["canUpdateContent"] is null) compact["permissions"]!["reason"] = permissions["permissionEvidence"]?.DeepClone();
        compact["download"] = JsonSerializer.SerializeToNode(Download(download, value["queryReason"]?.GetValue<string>() ?? value["environmentReason"]?.GetValue<string>()), JsonDefaults.Storage);
        compact["dependencies"] = JsonSerializer.SerializeToNode(Dependencies(dependencies, false), JsonDefaults.Storage);
        if (value["idSource"]?.GetValue<string>() != "explicit")
        {
            compact["modName"] = value["modName"]?.DeepClone(); compact["packageId"] = value["packageId"]?.DeepClone();
            compact["bindingConsistent"] = value["bindingConsistent"]?.DeepClone();
        }
        if (value["bindingConsistent"]?.GetValue<bool>() == false) compact["bindingSources"] = value["bindingSources"]?.DeepClone();
        foreach (var key in new[] { "environmentReason", "queryReason", "subscriptionCountReason", "lastUpdatedAtReason" })
            if (value[key] is not null) compact[key] = value[key]!.DeepClone();
        if (value["publicationState"]?.GetValue<string>() == "unpublished")
        {
            foreach (var key in new[] { "author", "title", "description", "visibility", "subscriptionCount", "fileBytes", "tags", "lastUpdatedAt", "download", "dependencies", "subscriptionCountReason", "lastUpdatedAtReason" }) compact.Remove(key);
        }
        return JsonSerializer.SerializeToElement(compact, JsonDefaults.Storage);
    }

    private static JsonObject WithoutEmptyReasons(this JsonObject value)
    {
        if (value["reason"] is null) value.Remove("reason");
        if (value["dlc"] is JsonArray dlc) foreach (var entry in dlc.OfType<JsonObject>()) if (entry["reason"] is null) entry.Remove("reason");
        return value;
    }
}
