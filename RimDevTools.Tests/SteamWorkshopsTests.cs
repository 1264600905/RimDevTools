using System.Text.Json;
using RimDevTools;

internal static class SteamWorkshopsTests
{
    internal static async Task<int> RunAsync(string fixtureRoot)
    {
        var passed = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("FAIL Workshop: " + message);
            passed++;
        }
        void Error(Action action, string code)
        {
            try { action(); throw new Exception("Expected " + code); }
            catch (WorkshopSearchException ex) { Check(ex.Code == code, code); }
        }
        Check(!OfficialAssetBakeAdapter.RequiresBaking("1.6") && OfficialAssetBakeAdapter.RequiresBaking("1.7"), "only 1.7 invokes official resource baking");
        Check(WorkshopSubscriptionService.Matches(new(294100, 123, 1), 123)
            && !WorkshopSubscriptionService.Matches(new(999, 123, 1), 123)
            && !WorkshopSubscriptionService.Matches(new(294100, 456, 1), 123), "download callbacks must match both app and exact item");
        var progressOnly = new SteamworksResponse { SteamRunning = true, Subscription = null, IsFinal = false };
        Check(SteamworksSessionProbe.ValidateHelperResponse(progressOnly, 0, "", true).ErrorCode == "STEAMWORKS_HELPER_FAILED"
            && SteamworksSessionProbe.ValidateHelperResponse(progressOnly with { IsFinal = true }, 99, "", true).ErrorCode == "STEAMWORKS_HELPER_FAILED",
            "partial progress or abnormal helper exit cannot become success");
        Check(SteamworksSessionProbe.ValidateHelperResponse(progressOnly with { IsFinal = true }, 0, "", true).ErrorCode is null, "only clean final response can succeed");
        var skipped = await OfficialAssetBakeAdapter.BakeAsync(new ProjectContext("missing", "missing", new ProjectConfig { GameVersion = "1.6" }), "missing", CancellationToken.None);
        Check(skipped.Succeeded && skipped.State == "not_required" && skipped.Libraries.Length == 0, "1.6 skips before game/library/staging discovery");
        foreach (var name in new[] { "upload", "edit", "search", "subscribe", "status" })
        {
            var command = "steamworkshops " + name;
            var args = name is "search" or "subscribe" ? new[] { "steamworkshops", name, "123" } : ["steamworkshops", name];
            Check(CliApplication.CommandPath(args) == command && CliArgs.Validate(command, args) is null, "command contracts: " + name);
            var previous = Console.Out;
            using var help = new StringWriter();
            try { Console.SetOut(help); Check(CliHelp.TryWrite(command), "help: " + name); }
            finally { Console.SetOut(previous); }
            Check(CliArgs.OptionNames(command).All(option => help.ToString().Contains(option)), "all options documented: " + name);
        }
        Check(CliArgs.Validate("steamworkshops status", ["steamworkshops", "status", "1", "2"]) is not null, "optional status positional limit");
        Check(CliArgs.Positionals("steamworkshops search", ["steamworkshops", "search", "--project", "x", "Harmony", "--page", "2"]).SequenceEqual(["Harmony"]), "options can precede positionals");
        Check(SteamWorkshopsCommand.ParseId("3833398574") == 3833398574 && SteamWorkshopsCommand.ParseId("https://steamcommunity.com/sharedfiles/filedetails/?id=3833398574") == 3833398574, "exact identifiers");
        Check(SteamWorkshopsCommand.ParseId("0") is null && SteamWorkshopsCommand.ParseId("https://evil.example/sharedfiles/filedetails/?id=3833398574") is null, "reject invalid ids and hosts");
        Check(WorkshopSearchPages.Map(false, 1) == (25, 3, 1, 0)
            && WorkshopSearchPages.Map(false, 2) == (25, 3, 1, 25)
            && WorkshopSearchPages.Map(false, 3) == (25, 3, 2, 0)
            && WorkshopSearchPages.Map(true, 5) == (10, 5, 1, 40), "native/user pagination mapping");
        Error(() => WorkshopSearchPages.Map(false, 4), "SEARCH_PAGE_LIMIT_EXCEEDED");
        Error(() => WorkshopSearchPages.Map(true, 6), "SEARCH_PAGE_LIMIT_EXCEEDED");
        var now = DateTimeOffset.UtcNow;
        var items = Enumerable.Range(1, 50).Select(n => new WorkshopItemInfo(n.ToString(), "1", 294100, "Item " + n, "official summary", "official_summary_255_bytes", "public", 0, null, 1, "", "", 0, null, "Mod")).ToArray();
        var native = new WorkshopQueryPage(1, 100, false, now, items);
        var root = Path.Combine(fixtureRoot, "WorkshopInputs"); Directory.CreateDirectory(root);
        var context = new ProjectContext(root, Path.Combine(root, "mod_config.jsonc"), new ProjectConfig { SyncItems = ["Resources"], CsprojPaths = [] });
        var cache = new WorkshopSearchCache(1, root, 294100, "Harmony", false, 1, 25, 3, 100, now, now.AddMinutes(10), new() { [1] = native });
        var p1 = SteamWorkshopsCommand.Json(WorkshopSearchPages.Output(cache, 1, native, "online"));
        var p2 = SteamWorkshopsCommand.Json(WorkshopSearchPages.Output(cache, 2, native, "cache"));
        Check(p1.GetProperty("items").GetArrayLength() == 25 && p2.GetProperty("items")[0].GetProperty("publishedFileId").GetString() == "26", "slices don't omit or repeat intervening items");
        Check(p1.GetProperty("items")[0].EnumerateObject().Count() == 3 && p1.GetProperty("items")[0].GetProperty("subscriptionCount").GetUInt64() == 0, "simple fields and real zero statistic");
        var detailed = cache with { Details = true, PageSize = 10, MaxPages = 5 };
        var p5 = SteamWorkshopsCommand.Json(WorkshopSearchPages.Output(detailed, 5, native, "cache"));
        Check(p5.GetProperty("items").GetArrayLength() == 10 && p5.GetProperty("limitReached").GetBoolean() && !p5.GetProperty("hasNextPage").GetBoolean(), "detailed last-page cap with real total");
        Check(p5.GetProperty("items")[0].GetProperty("description").GetString() == "official summary", "official summary unchanged");
        var shortPage = SteamWorkshopsCommand.Json(WorkshopSearchPages.Output(cache with { TotalMatchingResults = 2 }, 2, native with { Items = items.Take(2).ToArray() }, "online"));
        Check(shortPage.GetProperty("items").GetArrayLength() == 0 && !shortPage.GetProperty("limitReached").GetBoolean(), "short/empty pages are not cap errors");
        var unknown = SteamWorkshopsCommand.Json(WorkshopSearchPages.Output(cache, 1, native with { Items = [items[0] with { SubscriptionCount = null }] }, "online"));
        Check(unknown.GetProperty("items")[0].GetProperty("subscriptionCount").ValueKind == JsonValueKind.Null, "failed statistic is null");
        Error(() => WorkshopSearchPages.Read(null, now), "SEARCH_CONTEXT_NOT_FOUND");
        Error(() => WorkshopSearchPages.Read(context, now), "SEARCH_CONTEXT_NOT_FOUND");
        WorkshopSearchPages.Save(context, cache);
        Check(WorkshopSearchPages.Read(context, now).CurrentPage == 1, "context persists across reads");
        Error(() => WorkshopSearchPages.Read(context, now.AddMinutes(11)), "SEARCH_CONTEXT_EXPIRED");
        WorkshopSearchPages.Save(context, cache with { ProjectRoot = root + "Other" });
        Error(() => WorkshopSearchPages.Read(context, now), "SEARCH_CONTEXT_INVALID");
        var cachePath = ProjectDataPaths.File(root, "steamworkshops-search-cache.json");
        File.WriteAllText(cachePath, "{broken");
        Error(() => WorkshopSearchPages.Read(context, now), "SEARCH_CONTEXT_INVALID");
        WorkshopSearchPages.Save(context, cache);
        using (var locked = File.Open(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { WorkshopSearchPages.Save(context, cache with { CurrentPage = 2 }); throw new Exception("Expected atomic save failure"); }
            catch (IOException) { Check(WorkshopSearchPages.Read(context, now).CurrentPage == 1, "failed save does not advance cursor"); }
        }
        var owner = SteamWorkshopsCommand.Json(WorkshopPermissionEvaluator.Evaluate("1", "1"));
        var other = SteamWorkshopsCommand.Json(WorkshopPermissionEvaluator.Evaluate("1", "2"));
        var unavailable = SteamWorkshopsCommand.Json(WorkshopPermissionEvaluator.Evaluate(null, "2"));
        Check(owner.GetProperty("canUpdateContent").GetBoolean() && !other.GetProperty("canUpdateContent").GetBoolean()
            && unavailable.GetProperty("canUpdateContent").ValueKind == JsonValueKind.Null, "owner/nonowner/unknown gates");
        foreach (uint flags in new uint[] { 4 | 8, 4 | 16, 4 | 32, 0, 1 })
            Check(!SteamworksClient.EvaluateDownload(flags, true, root, null, null).DownloadComplete, "download flags: " + flags);
        Check(SteamworksClient.EvaluateDownload(1 | 4, true, root, null, null).Ready
            && !SteamworksClient.EvaluateDownload(1 | 4, false, root, null, null).Ready
            && !SteamworksClient.EvaluateDownload(1 | 4 | 2, true, root, null, null).Ready, "installed is insufficient without valid directory; legacy file not folder");
        var resources = Path.Combine(root, "Resources"); Directory.CreateDirectory(resources);
        var input = Path.Combine(resources, "a.txt"); File.WriteAllText(input, "abcd");
        var first = await DeploymentReceiptStore.CaptureInputsAsync(context, false);
        File.WriteAllText(input, "abce"); File.WriteAllText(Path.Combine(resources, "new.txt"), "new");
        var changes = DeploymentReceiptStore.Compare("project", first, await DeploymentReceiptStore.CaptureInputsAsync(context, false));
        Check(changes.Any(x => x.Kind == "modified") && changes.Any(x => x.Kind == "added"), "same-length and newly added inputs");
        File.Delete(input);
        Check(DeploymentReceiptStore.Compare("project", first, await DeploymentReceiptStore.CaptureInputsAsync(context, false)).Any(x => x.Kind == "missing"), "missing inputs");
        var receiptSentinel = ProjectDataPaths.File(root, "deployment-receipt.json"); File.WriteAllText(receiptSentinel, "runtime metadata");
        context.Config.SyncItems = ["."];
        Check(!(await DeploymentReceiptStore.CaptureInputsAsync(context, false)).Any(x => x.Path.Contains("ProjectData", StringComparison.OrdinalIgnoreCase)), "root sync cannot track tool runtime data as input");
        Check(DeploymentReceiptStore.ScanTarget(root).Any(x => x.Path.EndsWith("deployment-receipt.json")), "target enumeration detects unexpected runtime data too");
        File.Delete(cachePath);
        Check(File.Exists(receiptSentinel), "cache cleanup does not remove receipts");
        var buildRoot = Path.Combine(fixtureRoot, "WorkshopBuildInputs"); Directory.CreateDirectory(buildRoot);
        File.WriteAllText(Path.Combine(buildRoot, "Input.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><Import Project=\"custom.props\" /></Project>");
        File.WriteAllText(Path.Combine(buildRoot, "custom.props"), "<Project><PropertyGroup><ExampleValue>1</ExampleValue></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(buildRoot, "First.cs"), "class First {} ");
        var buildContext = new ProjectContext(buildRoot, Path.Combine(buildRoot, "mod_config.jsonc"), new ProjectConfig { CsprojPaths = ["Input.csproj"] });
        var buildBefore = await DeploymentReceiptStore.CaptureInputsAsync(buildContext, false);
        Check(buildBefore.Any(x => x.Path.EndsWith("custom.props")) && buildBefore.Any(x => x.Path.EndsWith("Microsoft.NET.Sdk.props")), "actual custom and SDK imports tracked");
        File.WriteAllText(Path.Combine(buildRoot, "Second.cs"), "class Second {} ");
        File.WriteAllText(Path.Combine(buildRoot, "custom.props"), "<Project><PropertyGroup><ExampleValue>2</ExampleValue></PropertyGroup></Project>");
        var buildDifferences = DeploymentReceiptStore.Compare("project", buildBefore, await DeploymentReceiptStore.CaptureInputsAsync(buildContext, false));
        Check(buildDifferences.Any(x => x.Kind == "added" && x.Path == "Second.cs") && buildDifferences.Any(x => x.Kind == "modified" && x.Path == "custom.props"), "new Compile glob and changed imported props require deploy");
        var replacement = Path.Combine(root, "new-pack.bin"); var destination = Path.Combine(root, "pack.bin");
        File.WriteAllText(replacement, "new"); File.WriteAllText(destination, "old");
        DeploymentFileTransaction.ReplacePayload(replacement, destination, (_, _) => throw new IOException("injected ReplaceFile 1175", unchecked((int)0x80070497)));
        Check(File.ReadAllText(destination) == "new" && !File.Exists(replacement), "1175 falls back to atomic same-volume replacement");
        File.WriteAllText(replacement, "next");
        try { DeploymentFileTransaction.ReplacePayload(replacement, destination, (_, _) => throw new IOException("sharing violation", unchecked((int)0x80070020))); throw new Exception("Expected sharing violation"); }
        catch (IOException) { Check(File.ReadAllText(destination) == "new" && File.ReadAllText(replacement) == "next", "real sharing violations are not bypassed"); }
        return passed;
    }
}
