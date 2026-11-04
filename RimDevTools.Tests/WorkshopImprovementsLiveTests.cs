using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RimDevTools;

// Opt-in real Steam acceptance. Functional text inputs use ProjectData; stdout is never archived.
internal static class WorkshopImprovementsLiveTests
{
    private const string Project = @"D:\RimworldProject3\奶牛枪";
    private const string Game = @"H:\SteamLibrary\steamapps\common\RimWorld";
    private const string Target = Game + @"\Mods\CowGun";
    private const ulong Id = 3833624388;
    private static string tool = "";
    private static int checks;

    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            tool = Path.GetFullPath(CliArgs.RequiredValue(args, "--tool"));
            Check(Environment.GetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE") is null && SteamworksSessionProbe.ProbeOverride is null
                && WorkshopPublishingService.PublishOverride is null, "Real Steam only");
            Check(WorkshopUploadCommand.ReadId(Path.Combine(Project, "About", "PublishedFileId.txt")) == Id
                && WorkshopUploadCommand.ReadId(Path.Combine(Target, "About", "PublishedFileId.txt")) == Id, "Current authorized CowGun identity");
            Check(ProjectDiscovery.Find(Project, out _)!.Config.CurrentPackageId(Project) == "trigger.cowgun", "CowGun package identity");
            var batch = CliArgs.RequiredValue(args, "--batch");
            Check(batch is "1" or "2", "Supported improvement batch");
            // Run web acceptance separately: repeated visibility propagation polling can hit Steam HTTP 429.
            if (batch == "2") await Queries();
            else if (args.Contains("--pages-only")) await Pages();
            else await Metadata(args.Contains("--metadata-only"));
            Console.WriteLine(JsonSerializer.Serialize(new { ok = true, batch = int.Parse(batch), mode = batch == "2" ? "queries" : args.Contains("--pages-only") ? "web-pages" : args.Contains("--metadata-only") ? "metadata-only" : "metadata-and-content", checks, publishedFileId = Id.ToString(), tool }));
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { ok = false, checks, error = ex.ToString() }));
            return 1;
        }
    }

    private static async Task Metadata(bool metadataOnly)
    {
        var original = await Inspect("english");
        Check(original.Visibility == "private", "Private test item");
        var receiptPath = Path.Combine(Project, ".tools", "ProjectData", "deployment-receipt.json");
        var receiptHash = Hash(receiptPath);
        var dll = Directory.GetFiles(Path.Combine(Target, "1.7", "Assemblies"), "CowGun.dll").Single();
        var dllHash = Hash(dll); var dllTime = File.GetLastWriteTimeUtc(dll);
        var descriptionFile = Path.Combine(Project, ".tools", "ProjectData", "workshop-edit-input.txt");
        var noteFile = Path.Combine(Project, ".tools", "ProjectData", "workshop-note-input.txt");
        var contentFile = Path.Combine(Project, "About", "About.xml");
        var originalContent = File.ReadAllBytes(contentFile);
        var previews = original.AdditionalPreviews ?? [];
        var sourceChanged = false;
        var wrote = false;
        try
        {
            wrote = true;
            var added = await Run(["steamworkshops", "edit", "--add-preview", Path.Combine(Project, "About", "Preview.png"),
                "--add-preview", @"C:\Users\liu\Pictures\553850_123.jpg"]);
            Check(!added.GetProperty("setItemContentCalled").GetBoolean() && added.GetProperty("submitCallCount").GetInt32() == 1, "Two images, one metadata-only submit");
            var afterAdd = await Inspect("english");
            Check(afterAdd.AdditionalPreviews!.Length == previews.Length + 2
                && afterAdd.AdditionalPreviews[^2].OriginalFileName == "Preview.png"
                && afterAdd.AdditionalPreviews[^1].OriginalFileName == "553850_123.jpg", "Two original filenames and order read back");
            Check(afterAdd.PreviewUrl == original.PreviewUrl && afterAdd.FileBytes == original.FileBytes, "Primary cover and content retained");
            await Run(["steamworkshops", "edit", "--update-preview", previews.Length + "=" + @"C:\Users\liu\Pictures\553850_123.jpg"]);
            var replaced = await Inspect("english");
            Check(replaced.AdditionalPreviews![previews.Length].OriginalFileName == "553850_123.jpg", "Replacement read back");
            await Run(["steamworkshops", "edit", "--remove-preview", previews.Length.ToString(), "--remove-preview", (previews.Length + 1).ToString()]);
            Check(WorkshopPublisher.PreviewsMatch(previews, (await Inspect("english")).AdditionalPreviews!), "Both test images removed and original gallery retained");

            await Run(["steamworkshops", "edit", "--title", "Cow Gun - title edit validation"]);
            var titleOnly = await Inspect("english");
            Check(titleOnly.Title == "Cow Gun - title edit validation" && titleOnly.Description == original.Description
                && titleOnly.PreviewUrl == original.PreviewUrl && titleOnly.Visibility == original.Visibility, "Title-only edit preserves omitted metadata");
            const string english = "[h1]Cow Gun[/h1]\nTransforms enemy humanlike targets into cows. Version 0.1.4; warmup 1.5 seconds.";
            const string chinese = "[h1]奶牛枪[/h1]\n命中敌方人形生物时将其变成奶牛。版本 0.1.4，预热 1.5 秒。";
            File.WriteAllText(descriptionFile, english, new UTF8Encoding(true));
            await Run(["steamworkshops", "edit", "--title", "Cow Gun", "--description-file", descriptionFile, "--text-language", "english"]);
            File.WriteAllText(descriptionFile, chinese, new UTF8Encoding(false));
            var localized = await Run(["steamworkshops", "edit", "--title", "奶牛枪", "--description-file", descriptionFile, "--text-language", "schinese"]);
            Check(localized.GetProperty("textLanguage").GetString() == "schinese" && !localized.GetProperty("setItemContentCalled").GetBoolean(), "Language reported and metadata only");
            var en = await Inspect("english"); var zh = await Inspect("schinese");
            Check(en.Title == "Cow Gun" && en.Description == english && zh.Title == "奶牛枪" && zh.Description == chinese, "English and Simplified Chinese stored independently");
            await Run(["steamworkshops", "edit", "--title", "奶牛枪·语言隔离验证", "--text-language", "schinese"]);
            var isolated = await Inspect("schinese"); var enAfter = await Inspect("english");
            Check(isolated.Description == chinese && enAfter.Title == en.Title && enAfter.Description == en.Description, "Changing one translated title preserves both descriptions and other language");
            Check(Hash(receiptPath) == receiptHash && Hash(dll) == dllHash && File.GetLastWriteTimeUtc(dll) == dllTime, "Metadata never rebuilds/deploys or rewrites receipt");
            await ExpectInvalid(["steamworkshops", "edit", "--title", new string('中', 43)]);
            await ExpectInvalid(["steamworkshops", "edit", "--title", " "]);
            await ExpectInvalid(["steamworkshops", "edit", "--text-language", "zh-CN"]);
            await ExpectInvalid(["steamworkshops", "edit", "--remove-preview", "999999"]);

            if (!metadataOnly)
            {
                const string note = "奶牛枪 0.1.4：验证部署并上传的自定义改动说明。\n预热保持 1.5 秒；本次仅增加 XML 末尾空行验证内容提交。";
                File.WriteAllText(noteFile, note, new UTF8Encoding(true));
                File.WriteAllBytes(contentFile, originalContent.Concat(Encoding.UTF8.GetBytes("\n")).ToArray()); sourceChanged = true;
                var upload = (await Run(["deploy", "--upload", "--change-note-file", noteFile])).GetProperty("upload");
                Check(upload.GetProperty("submitCallCount").GetInt32() == 1 && upload.GetProperty("setItemContentCalled").GetBoolean()
                    && upload.GetProperty("changeNote").GetString() == note && !upload.GetProperty("created").GetBoolean(), "One combined deploy/upload, actual content with exact Chinese multiline note");
            }
        }
        finally
        {
            // Restore local input bytes in memory; a normal deploy issues the matching fresh receipt.
            Exception? restoreError = null;
            if (sourceChanged)
            {
                try
                {
                    File.WriteAllBytes(contentFile, originalContent); await Run(["deploy"]);
                    var noNote = await Run(["steamworkshops", "upload", "--change-note", ""]);
                    Check(noNote.GetProperty("changeNote").ValueKind == JsonValueKind.Null && noNote.GetProperty("submitCallCount").GetInt32() == 1,
                        "Restored content uploaded with no note, without fixed fallback");
                }
                catch (Exception ex) { restoreError = ex; }
            }
            if (wrote)
            {
                var current = await Inspect("english");
                if (current.AdditionalPreviews!.Length > previews.Length)
                    await Run(new[] { "steamworkshops", "edit" }.Concat(Enumerable.Range(previews.Length, current.AdditionalPreviews.Length - previews.Length)
                        .SelectMany(index => new[] { "--remove-preview", index.ToString() })).ToArray());
                File.WriteAllText(descriptionFile, original.Description);
                await Run(["steamworkshops", "edit", "--title", original.Title, "--description-file", descriptionFile, "--text-language", "english", "--visibility", "private"]);
                await Run(["steamworkshops", "edit", "--title", "奶牛枪", "--text-language", "schinese"]);
                var restored = await Inspect("english");
                Check(restored.Title == original.Title && restored.Description == original.Description && restored.PreviewUrl == original.PreviewUrl
                    && restored.Visibility == "private" && WorkshopPublisher.PreviewsMatch(previews, restored.AdditionalPreviews!), "Original default page, cover, gallery and private visibility restored");
            }
            foreach (var path in new[] { descriptionFile, noteFile }) if (File.Exists(path)) File.Delete(path);
            if (restoreError is not null) throw new Exception("Content restoration failed", restoreError);
        }
        await Run(["steamworkshops", "upload", "--dry-run"]);
    }

    private static async Task Pages()
    {
        var original = await Inspect("english");
        try
        {
            await Run(["steamworkshops", "edit", "--title", "Cow Gun", "--text-language", "english", "--visibility", "public"]);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20), DefaultRequestVersion = System.Net.HttpVersion.Version11 };
            foreach (var (language, title) in new[] { ("english", "Cow Gun"), ("schinese", "奶牛枪") })
            {
                await ReadPage("https://steamcommunity.com/sharedfiles/filedetails/?id=" + Id + "&l=" + language, html =>
                {
                    var match = System.Text.RegularExpressions.Regex.Match(html, "<div class=\"workshopItemTitle\">(.*?)</div>");
                    return match.Success && System.Net.WebUtility.HtmlDecode(match.Groups[1].Value) == title;
                }, "Workshop page language: " + language);
            }
            await ReadPage("https://steamcommunity.com/sharedfiles/filedetails/changelog/" + Id + "?l=schinese", html =>
                System.Net.WebUtility.HtmlDecode(html).Contains("奶牛枪 0.1.4：验证部署并上传的自定义改动说明。"), "Chinese content note visible in Workshop change history");

            async Task ReadPage(string url, Func<string, bool> matches, string message)
            {
                string observed = "";
                for (var attempt = 0; attempt < 10; attempt++)
                {
                    var html = await http.GetStringAsync(url + "&t=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    if (matches(html)) { Check(true, message); return; }
                    observed = System.Text.RegularExpressions.Regex.Match(html, "<div class=\"workshopItemTitle\">(.*?)</div>").Groups[1].Value;
                    if (observed.Length == 0) observed = System.Text.RegularExpressions.Regex.Match(html, "<title>(.*?)</title>").Groups[1].Value;
                    Console.Error.WriteLine("PAGE observation: " + message + ": " + observed);
                    if (attempt < 9) await Task.Delay(2000);
                }
                Check(false, message + "; observed=" + observed);
            }
        }
        finally
        {
            await Run(["steamworkshops", "edit", "--title", original.Title, "--text-language", "english", "--visibility", "private"]);
            var restored = await Inspect("english");
            Check(restored.Title == original.Title && restored.Description == original.Description && restored.Visibility == "private", "Page verification restored private default page");
        }
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static async Task Queries()
    {
        var receipt = Path.Combine(Project, ".tools", "ProjectData", "deployment-receipt.json");
        var receiptHash = Hash(receipt);
        var modsConfig = GameEnvironment.ModsConfigPath(ProjectDiscovery.Find(Project, out _)!);
        var modsHash = Hash(modsConfig!);
        var raw = await Inspect(null); // Preserve Steam's existing client-language query behavior.
        var own = await Run(["steamworkshops", "status"]);
        var verbose = await Run(["steamworkshops", "status", "--verbose"]);
        Check(own.GetProperty("description").GetString() == WorkshopText.Plain(raw.Description) && own.GetProperty("visibility").GetString() == "private"
            && own.GetProperty("bindingConsistent").GetBoolean(), "CowGun full clean description, private identity and binding");
        Check(own.GetProperty("permissions").GetProperty("userRole").GetString() == "owner"
            && verbose.GetProperty("permissions").GetProperty("userRole").GetString() == "owner", "Concise/verbose owner conclusions agree");
        Check(!own.TryGetProperty("lastUpdatedAtUnix", out _) && verbose.TryGetProperty("lastUpdatedAtUnix", out _)
            && !own.TryGetProperty("previewUrl", out _) && verbose.TryGetProperty("previewUrl", out _), "Default hides duplicate times/diagnostics, verbose retains them");
        Check(own.GetProperty("dependencies").GetProperty("status").GetString() == "complete"
            && own.GetProperty("dependencies").GetProperty("mods").GetArrayLength() == 0 && own.GetProperty("dependencies").GetProperty("dlc").GetArrayLength() == 0,
            "CowGun has genuinely empty registered dependencies");
        var direct = await Run(["steamworkshops", "status", "3819799478"]);
        var exact = await Run(["steamworkshops", "search", "https://steamcommunity.com/sharedfiles/filedetails/?id=3819799478"]);
        Check(direct.GetProperty("description").GetString() == exact.GetProperty("description").GetString()
            && direct.GetProperty("description").GetString()!.Contains("常见问题") && direct.GetProperty("description").GetString()!.Contains("[新增]")
            && !direct.GetProperty("description").GetString()!.Contains("https://") && !direct.GetProperty("description").GetString()!.Contains("[url"), "DMS2 status and exact search preserve full Chinese text/FAQ and normal brackets");
        Check(direct.GetProperty("author").GetProperty("steamId").GetString() == "76561198208644840"
            && !string.IsNullOrWhiteSpace(direct.GetProperty("author").GetProperty("name").GetString()), "Publisher nickname returned from native Steam");
        Check(!direct.TryGetProperty("packageId", out _) && !direct.TryGetProperty("bindingSources", out _) && direct.GetProperty("download").GetProperty("ready").GetBoolean(),
            "Foreign item omits project identity; DMS2 remains ready");
        foreach (var (id, expectedIds) in new[] { ("3609835606", new[] { "2023507013" }), ("3799737423", new[] { "2023507013", "3609835606" }) })
        {
            var status = await Run(["steamworkshops", "status", id]);
            var deps = status.GetProperty("dependencies");
            var required = deps.GetProperty("mods").EnumerateArray().ToArray();
            Check(deps.GetProperty("status").GetString() == "complete" && required.Select(x => x.GetProperty("publishedFileId").GetString()).Order().SequenceEqual(expectedIds.Order()),
                "Chapter required IDs: " + id);
            Check(deps.GetProperty("dlc").EnumerateArray().Any(x => x.GetProperty("appId").GetUInt32() == 3022790 && x.GetProperty("name").GetString()!.Contains("Odyssey")), "Odyssey DLC name/App ID: " + id);
            foreach (var mod in required)
            {
                var installed = await Run(["steamworkshops", "status", mod.GetProperty("publishedFileId").GetString()!]);
                Check(mod.GetProperty("state").GetString() == installed.GetProperty("download").GetProperty("state").GetString()
                    && mod.GetProperty("subscribed").GetBoolean() == installed.GetProperty("download").GetProperty("subscribed").GetBoolean(), "Dependency install state matches direct status");
            }
        }
        var candidates = await Run(["steamworkshops", "search", "失能机关2"]);
        var foundNotInstalled = false;
        foreach (var candidate in candidates.GetProperty("items").EnumerateArray().Take(10))
        {
            var missing = await Run(["steamworkshops", "status", candidate.GetProperty("publishedFileId").GetString()!]);
            if (missing.GetProperty("download").GetProperty("state").GetString() != "not_installed"
                || missing.GetProperty("dependencies").GetProperty("status").GetString() != "complete"
                || missing.GetProperty("dependencies").GetProperty("mods").GetArrayLength() == 0) continue;
            foundNotInstalled = true;
            Check(!missing.GetProperty("download").GetProperty("ready").GetBoolean(), "Subject not downloaded still exposes nonempty native dependencies");
            break;
        }
        Check(foundNotInstalled, "Read-only search found an uninstalled dependency-bearing subject");
        foreach (var details in new[] { false, true })
        {
            var max = details ? 5 : 3; var size = details ? 10 : 25;
            var all = new List<string>();
            for (var page = 1; page <= max; page++)
            {
                var result = await Run(page == 1 ? details ? ["steamworkshops", "search", "失能机关2", "--details"] : ["steamworkshops", "search", "失能机关2"] : ["steamworkshops", "search", "next"]);
                Check(result.GetProperty("page").GetInt32() == page && result.GetProperty("returnedCount").GetInt32() == size, "Live pagination retains page/count");
                foreach (var item in result.GetProperty("items").EnumerateArray())
                {
                    all.Add(item.GetProperty("publishedFileId").GetString()!);
                    if (details)
                    {
                        var description = item.GetProperty("description").GetString()!;
                        Check(!description.Contains("https://") && !description.Contains("http://") && !description.Contains("[url") && !description.Contains("[img"), "Detailed result contains only cleaned official summary");
                    }
                    else Check(item.EnumerateObject().Count() == 3, "Simple search still returns exactly three fields");
                }
            }
            Check(all.Count == all.Distinct().Count(), "No duplicate IDs across pages");
        }
        var cachePath = Path.Combine(Project, ".tools", "ProjectData", "steamworkshops-search-cache.json");
        var cache = JsonSerializer.Deserialize<WorkshopSearchCache>(File.ReadAllText(cachePath), JsonDefaults.Storage)!;
        Check(cache.Pages.Values.SelectMany(x => x.Items).Any(x => x.Description.Contains("https://")), "Official raw descriptions remain unchanged in search cache");
        await Run(["steamworkshops", "upload", "--dry-run"]);
        Check(Hash(receipt) == receiptHash && Hash(modsConfig!) == modsHash, "Read-only queries leave receipt and active-mod configuration unchanged");
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++; Console.Error.WriteLine("PASS LIVE: " + message);
    }
    private static async Task<WorkshopItemInfo> Inspect(string? language)
    {
        var invocation = await Invoke(["--steamworkshops-helper"], JsonSerializer.Serialize(new SteamworksRequest(Path.Combine(Game, "steam_api64.dll"), "detail", 30, Id: Id, Language: language), JsonDefaults.Storage));
        var line = invocation.Output.Split('\n').Last(x => x.StartsWith("RIMDEVTOOLS_STEAMWORKSHOPS="));
        var response = JsonSerializer.Deserialize<SteamworksResponse>(line["RIMDEVTOOLS_STEAMWORKSHOPS=".Length..], JsonDefaults.Storage)!;
        Check(invocation.Exit == 0 && response.ErrorCode is null && response.Page?.Items.Length == 1, "Native language readback: " + language + " " + response.Reason);
        return response.Page!.Items.Single();
    }
    private static async Task<JsonElement> Run(string[] args)
    {
        var invocation = await Invoke(args);
        using var doc = JsonDocument.Parse(invocation.Output);
        Check(invocation.Exit == 0 && doc.RootElement.GetProperty("ok").GetBoolean(), string.Join(' ', args) + ": " + (invocation.Exit == 0 ? "ok" : invocation.Output));
        return doc.RootElement.GetProperty("result").Clone();
    }
    private static async Task ExpectInvalid(string[] args)
    {
        var invocation = await Invoke(args);
        using var doc = JsonDocument.Parse(invocation.Output);
        Check(invocation.Exit == ExitCodes.InvalidArguments && doc.RootElement.GetProperty("error").GetProperty("code").GetString() == "INVALID_ARGUMENT", "Invalid edit rejected before submission");
    }
    private static async Task<(int Exit, string Output)> Invoke(string[] args, string? input = null)
    {
        if (CliArgs.Value(args, "--id") is { } explicitId) Check(explicitId == Id.ToString(), "Only authorized item writes");
        Console.Error.WriteLine("LIVE " + string.Join(' ', args));
        var start = new ProcessStartInfo(tool) { WorkingDirectory = input is null ? Project : Game, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input is not null, StandardOutputEncoding = Encoding.UTF8, StandardInputEncoding = input is null ? null : new UTF8Encoding(false) };
        if (input is not null) { start.Environment["SteamAppId"] = "294100"; start.Environment["SteamGameId"] = "294100"; }
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new Exception("Cannot start CLI");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        if (input is not null) { await process.StandardInput.WriteLineAsync(input); process.StandardInput.Close(); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(40));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); throw; }
        var diagnostic = await stderr; if (!string.IsNullOrWhiteSpace(diagnostic)) Console.Error.WriteLine(diagnostic.Trim());
        return (process.ExitCode, (await stdout).TrimStart('\uFEFF'));
    }
}
