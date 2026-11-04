using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using RimDevTools;

internal static class WorkshopQueryPresentationTests
{
    internal static async Task<int> RunAsync(string fixtureRoot)
    {
        var passed = 0;
        void Check(bool value, string message) { if (!value) throw new Exception("FAIL Workshop display: " + message); passed++; }
        foreach (var (input, expected) in new[]
        {
            ("[h1]标题[/h1]\n[b]正文[/b] [新增] [NEW]", "标题\n\n正文 [新增] [NEW]"),
            ("[url=https://example.test]依赖名称[/url]", "依赖名称"),
            ("[url=\"https://example.test\"][b]名称[/b][/url]", "名称"),
            ("[url=https://example.test][img]https://example.test/a.png[/img][/url]", ""),
            ("[img]https://example.test/a.png[/img]", ""),
            ("[url]https://example.test[/url]", ""),
            ("https://example.test/a.png", ""),
            ("开头https://example.test/a，中文正文。https://example.test/b。结尾", "开头，中文正文。。结尾"),
            ("[list][*]甲[*][b]乙[/b][/list]", "- 甲\n- 乙"),
            ("[table][tr][td]甲[/td][td]乙[/td][/tr][/table]", "甲 乙"),
            ("[h2]问答[/h2]\nQ: 问题？\nA: 答案。", "问答\n\nQ: 问题？\nA: 答案。"),
            ("正文 [url=https://exa", "正文"),
            ("正文 [b", "正文"),
            ("正文 [img]https://ex", "正文"),
            ("正文 https://exa", "正文"),
            ("正文 https://", "正文"),
            ("  甲\r\n\r\n\r\n\t乙  &amp; 丙 ", "甲\n\n乙 & 丙"),
            ("[previewyoutube=abc;full][/previewyoutube]正文", "正文")
        }) Check(WorkshopText.Plain(input) == expected, "plain text: " + input + "; actual=" + WorkshopText.Plain(input));
        Check(WorkshopText.Plain(null) is null && WorkshopText.Plain("") == "", "unknown and empty descriptions remain distinct");
        var longText = new string('中', 7000);
        Check(WorkshopText.Plain("[b]" + longText + "[/b]") == longText, "full text is not truncated");
        Check(Marshal.SizeOf<SteamworksClient.AppDependenciesResult>() == 152, "official Windows x64 DLC callback layout");
        var root = Path.Combine(fixtureRoot, "WorkshopPresentation"); Directory.CreateDirectory(root);
        var ready = SteamworksClient.EvaluateDownload(5, true, root, null, null);
        foreach (var (flags, valid, expected) in new (uint, bool, string)[]
        {
            (0, false, "not_installed"), (1, false, "not_installed"), (5, true, "ready"), (13, false, "needs_update"),
            (21, false, "downloading"), (37, false, "pending"), (5, false, "unknown"), (4, true, "installed_unsubscribed")
        }) Check(WorkshopStatusView.DownloadState(SteamworksClient.EvaluateDownload(flags, valid, valid ? root : null, null, null)) == expected,
            "installation state: " + expected);
        var unknown = SteamWorkshopsCommand.Json(WorkshopStatusView.Download(null));
        Check(unknown.GetProperty("subscribed").ValueKind == JsonValueKind.Null && unknown.GetProperty("ready").ValueKind == JsonValueKind.Null
            && unknown.GetProperty("reason").GetString()!.Length > 0, "missing installation data is explained, never false");
        var progress = SteamworksClient.EvaluateDownload(21, false, null, 1, 4);
        Check(SteamWorkshopsCommand.Json(WorkshopStatusView.Download(progress)).GetProperty("progress").GetDouble() == .25
            && !SteamWorkshopsCommand.Json(WorkshopStatusView.Download(ready)).TryGetProperty("progress", out _), "progress only appears while downloading");

        var item = new WorkshopItemInfo("123", "1", 294100, "Fixture", "[h1]标题[/h1]\n[url=https://example.test]前置[/url] [NEW]", "full", "private", 0, null,
            123, "1970-01-01T00:02:03+00:00", "1970-01-01T08:02:03+08:00", 42, "https://example.test/cover", "Mod") { Children = [456], AdditionalPreviews = [] };
        WorkshopDependencies Resolve(WorkshopItemInfo target, Func<ulong[], WorkshopItemInfo[]>? names = null, Func<ulong, WorkshopDownloadState>? states = null,
            Func<(uint[], string?)>? apps = null, Func<IReadOnlyDictionary<uint, string>>? dlcNames = null)
            => SteamworksClient.ResolveDependencies(target, names ?? (_ => [item with { PublishedFileId = "456", Title = "Required" }]), states ?? (_ => ready),
                apps ?? (() => (new uint[] { 3022790 }, null)), dlcNames ?? (() => new Dictionary<uint, string> { [3022790] = "RimWorld - Odyssey" }));
        var complete = Resolve(item);
        Check(complete.Status == "complete" && complete.Mods!.Single() is { PublishedFileId: "456", Name: "Required", Download.Ready: true }
            && complete.Dlc!.Single() is { AppId: 3022790, Name: "RimWorld - Odyssey" }, "direct dependencies resolve independently of subject installation");
        var partial = Resolve(item, names: _ => throw new IOException("offline"));
        Check(partial.Status == "partial" && partial.Mods!.Single() is { PublishedFileId: "456", Name: null, Download.Ready: true }
            && partial.Reason!.Contains("offline"), "failed name query retains IDs and independently known installation state");
        partial = Resolve(item, states: _ => throw new IOException("unavailable"));
        Check(partial.Mods!.Single() is { Name: "Required", Download: null, Reason: not null }, "unknown installation cannot become not installed");
        partial = Resolve(item, apps: () => throw new IOException("offline"));
        Check(partial.Status == "partial" && partial.Dlc is null && partial.Mods!.Length == 1, "failed DLC query is unknown, not empty");
        partial = Resolve(item, dlcNames: () => throw new IOException("unavailable"));
        Check(partial.Dlc!.Single() is { AppId: 3022790, Name: null, Reason: not null }, "known DLC ID survives name enumeration failure");
        partial = Resolve(item, apps: () => ([3022790], "partial packet"));
        Check(partial.Status == "partial" && partial.Dlc!.Length == 1 && partial.Reason!.Contains("partial packet"), "partial native packets retain known dependencies");
        partial = Resolve(item with { Children = null, ChildrenReason = "children failed" });
        Check(partial.Mods is null && partial.Dlc!.Length == 1 && partial.Reason!.Contains("children failed"), "failed children query cannot claim no required mods");
        var empty = Resolve(item with { Children = [] }, apps: () => ([], null));
        Check(empty.Status == "complete" && empty.Mods!.Length == 0 && empty.Dlc!.Length == 0, "genuine empty dependency response");
        var collection = Resolve(item with { FileType = 2 }, names: _ => throw new Exception("must not query"), states: _ => throw new Exception("must not query"),
            apps: () => throw new Exception("must not query"));
        Check(collection.Status == "not_applicable" && collection.Mods!.Length == 0, "collections never become required mod lists");

        File.WriteAllText(Path.Combine(root, "mod_config.jsonc"), JsonSerializer.Serialize(new ProjectConfig { ConfigVersion = 2, ModName = "Fixture", CsprojPaths = [], SyncItems = [] }, JsonDefaults.Storage));
        var previous = SteamworksSessionProbe.ProbeOverride;
        var response = new SteamworksResponse { SteamRunning = true, RimWorldInLibrary = true, WorkshopAvailability = "available", CurrentUserSteamId = "1",
            Page = new(1, 1, false, DateTimeOffset.UtcNow, [item]), Download = ready, Dependencies = complete, Author = new("1", "Author", null) };
        try
        {
            SteamworksSessionProbe.ProbeOverride = _ => Task.FromResult(response);
            var compact = (await Capture(["steamworkshops", "status", "123", "--project", root])).GetProperty("result");
            var verbose = (await Capture(["steamworkshops", "status", "123", "--project", root, "--verbose"])).GetProperty("result");
            var exact = (await Capture(["steamworkshops", "search", "https://steamcommunity.com/sharedfiles/filedetails/?id=123", "--project", root])).GetProperty("result");
            Check(compact.GetProperty("description").GetString() == "标题\n\n前置 [NEW]" && exact.GetProperty("description").GetString() == compact.GetProperty("description").GetString()
                && verbose.GetProperty("description").GetString() == compact.GetProperty("description").GetString(), "status/exact search/verbose all show full clean text");
            Check(!compact.TryGetProperty("packageId", out _) && !compact.TryGetProperty("bindingSources", out _) && !compact.TryGetProperty("idSource", out _)
                && !compact.TryGetProperty("previewUrl", out _) && !compact.TryGetProperty("lastUpdatedAtUnix", out _), "explicit status omits inapplicable project and diagnostic fields");
            Check(verbose.GetProperty("download").GetProperty("flags").GetUInt32() == 5 && verbose.TryGetProperty("bindingSources", out _)
                && verbose.TryGetProperty("lastUpdatedAtUnix", out _) && verbose.TryGetProperty("previewUrl", out _), "verbose retains diagnostic evidence");
            Check(compact.GetProperty("permissions").GetProperty("canUpdateContent").GetBoolean() == verbose.GetProperty("permissions").GetProperty("canUpdateContent").GetBoolean()
                && compact.GetProperty("download").GetProperty("ready").GetBoolean() == verbose.GetProperty("download").GetProperty("ready").GetBoolean(), "views preserve permission and readiness conclusions");
            Check(compact.GetProperty("subscriptionCount").GetUInt64() == 0 && !compact.GetProperty("author").TryGetProperty("reason", out _), "real zero retained; empty normal reasons removed");
            var altered = JsonNode.Parse(verbose.GetRawText())!.AsObject();
            altered["idSource"] = "project"; altered["bindingConsistent"] = false;
            var conflict = WorkshopStatusView.Format(SteamWorkshopsCommand.Json(altered), ready, complete, false);
            Check(conflict.TryGetProperty("bindingSources", out _) && !conflict.GetProperty("bindingConsistent").GetBoolean(), "binding conflict sources stay visible without verbose");
            altered["publicationState"] = "unpublished"; altered["publishedFileId"] = null;
            var unpublished = WorkshopStatusView.Format(SteamWorkshopsCommand.Json(altered), null, null, false);
            Check(!unpublished.TryGetProperty("description", out _) && !unpublished.TryGetProperty("download", out _), "unpublished project omits inapplicable remote fields");
            response = response with { ErrorCode = "STEAM_NOT_RUNNING", Reason = "offline", Page = null, Download = null, Dependencies = null, Author = null, WorkshopAvailability = "unknown" };
            var failed = await Capture(["steamworkshops", "status", "123", "--project", root]);
            var details = failed.GetProperty("error").GetProperty("details");
            Check(!failed.GetProperty("ok").GetBoolean() && details.GetProperty("subscriptionCount").ValueKind == JsonValueKind.Null
                && details.GetProperty("environmentReason").GetString() == "offline" && details.GetProperty("download").GetProperty("state").GetString() == "unknown", "offline/unknown data has visible reasons without verbose");
        }
        finally { SteamworksSessionProbe.ProbeOverride = previous; }
        return passed;
    }

    private static async Task<JsonElement> Capture(string[] args)
    {
        var original = Console.Out; using var writer = new StringWriter();
        try { Console.SetOut(writer); await CliApplication.RunAsync(args); }
        finally { Console.SetOut(original); }
        return JsonDocument.Parse(writer.ToString()).RootElement.Clone();
    }
}
