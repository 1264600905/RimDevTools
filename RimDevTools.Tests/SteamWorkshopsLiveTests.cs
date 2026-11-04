using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using RimDevTools;

internal static class SteamWorkshopsLiveTests
{
    internal const string Project = @"D:\RimworldProject3\奶牛枪";
    internal const string Game = @"H:\SteamLibrary\steamapps\common\RimWorld";
    internal const string Target = @"H:\SteamLibrary\steamapps\common\RimWorld\Mods\CowGun";
    private static readonly List<object> evidence = [];
    private static string tool = "";

    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            tool = Path.GetFullPath(CliArgs.RequiredValue(args, "--tool"));
            var batch = CliArgs.RequiredValue(args, "--batch");
            Check(batch == "1", "Only batch 1 is implemented in this driver so far");
            Check(Environment.GetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE") is null && SteamworksSessionProbe.ProbeOverride is null
                && SteamSessionGuard.ProbeOverride is null && ProcessUtil.RimWorldRunningOverride is null, "Live driver refuses fake mode and injected probes");
            Check(File.Exists(tool) && Path.GetFileName(tool).Equals("RimDevTools.exe", StringComparison.OrdinalIgnoreCase), "Use actual release CLI executable");
            var context = ProjectDiscovery.Find(Project, out _) ?? throw new Exception("Missing fixed project");
            Check(Path.GetFullPath(context.Root) == Project && context.Config.CurrentPackageId(Project) == "trigger.cowgun", "Fixed source identity");
            Check(Path.GetFullPath(context.Config.RimWorldDir!) == Game && context.Config.ModName == "CowGun", "Fixed game and mod target");
            var resolved = RimWorldEnvironmentResolver.Resolve(context);
            Check(resolved.References.ExecutablePath == Path.Combine(Game, "RimWorld.exe") && File.Exists(resolved.References.ExecutablePath), "Actual 1.7 game executable");
            Check(File.Exists(Path.Combine(Game, "steam_api64.dll")) && File.Exists(Path.Combine(Game, "Waylith.AssetBake.dll")), "Actual native and baking libraries");
            Check(WorkshopUploadCommand.ReadId(Path.Combine(Project, "About", "PublishedFileId.txt")) == 3833398574
                && WorkshopUploadCommand.ReadId(Path.Combine(Target, "About", "PublishedFileId.txt")) == 3833398574, "Only authorized Workshop binding");
            var help = await Invoke(["steamworkshops", "search", "--help"], parse: false);
            Check(help.Output.Contains("search next") && help.Output.Contains("pages 1-3"), "New CLI search capabilities");
            await Batch1(context);
            Console.WriteLine(JsonSerializer.Serialize(new { ok = true, mode = "live-steamworkshops", batch = 1, tool, project = Project, target = Target, evidence }));
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { ok = false, mode = "live-steamworkshops", tool, error = ex.Message, evidence }));
            return 1;
        }
    }

    private static async Task Batch1(ProjectContext context)
    {
        var libraryProbe = await Invoke(["--official-asset-library-probe", Game]);
        using var libraries = JsonDocument.Parse(libraryProbe.Output);
        Check(libraryProbe.Exit == 0 && libraries.RootElement.GetProperty("staticLoadSucceeded").GetBoolean(), "Actual official libraries load/signature probe");
        evidence.Add(new { check = "official-library-static-probe", libraries = libraries.RootElement.Clone() });
        var doctor = await Run(["doctor"]);
        var checks = doctor.GetProperty("checks").EnumerateArray().ToArray();
        Check(new[] { "steam.runtime", "steam.rimworld_library", "steam.workshop_online" }.All(id => checks.Any(c => c.GetProperty("id").GetString() == id && c.GetProperty("status").GetString() == "available")), "Doctor always-visible live checks");
        await Run(["deploy", "--dry-run"]);
        var deploy = await Run(["deploy"]);
        var receipt = DeploymentReceiptStore.Read(context)!;
        Check(receipt.DeployRunId == deploy.GetProperty("runId").GetString() && receipt.State == "completed"
            && !receipt.PublishReady && receipt.AssetPackState == "required_unverified", "This deployment's real transitional receipt");
        var textures = Path.Combine(Project, "1.7", "Textures", "WorkshopTestAssets", "ShezhangOC");
        var pngs = Directory.GetFiles(textures, "*.png", SearchOption.AllDirectories);
        Check(pngs.Length == 180, "All 180 authorized PNGs are present");
        foreach (var file in pngs)
        {
            var destination = Path.Combine(Target, Path.GetRelativePath(Project, file));
            Check(File.Exists(destination) && SHA256.HashData(File.ReadAllBytes(file)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(destination))), "Deployed PNG " + Path.GetRelativePath(Project, file));
        }
        Check(!Directory.GetFiles(Target, "*.pdb", SearchOption.AllDirectories).Any() && !Directory.GetFiles(Target, "*.deps.json", SearchOption.AllDirectories).Any(), "No prohibited build artifacts");
        evidence.Add(new { check = "deployed-textures", count = pngs.Length, deployRunId = receipt.DeployRunId, artifacts = new[] { DeploymentReceiptStore.PathFor(context) } });
        var status = await Run(["steamworkshops", "status"]);
        Check(status.GetProperty("publishedFileId").GetString() == "3833398574" && status.GetProperty("packageId").GetString() == "trigger.cowgun"
            && status.GetProperty("visibility").GetString() == "private" && status.GetProperty("permissions").GetProperty("userRole").GetString() == "owner"
            && status.GetProperty("lastUpdatedAtUnix").GetUInt32() > 0, "Real private owner status and Steam update timestamp");
        await Run(["steamworkshops", "search", "3833398574"]);
        var other = await Run(["steamworkshops", "status", "2009463077"]);
        Check(other.GetProperty("permissions").GetProperty("userRole").GetString() == "not_owner"
            && !other.GetProperty("permissions").GetProperty("canUpdateContent").GetBoolean(), "Public other-owner item remains read-only");
        var simple1 = await Run(["steamworkshops", "search", "Harmony"]);
        var cache = WorkshopSearchPages.Read(context, DateTimeOffset.UtcNow);
        var simple2 = await Run(["steamworkshops", "search", "next"]);
        Check(simple2.GetProperty("page").GetInt32() == 2 && simple2.GetProperty("source").GetString() == "cache", "Cross-process next reuses native page");
        Check(Ids(simple2).SequenceEqual(cache.Pages[1].Items.Skip(25).Take(25).Select(x => x.PublishedFileId)), "Simple page 2 is native positions 26-50");
        var simple3 = await Run(["steamworkshops", "search", "next"]);
        cache = WorkshopSearchPages.Read(context, DateTimeOffset.UtcNow);
        Check(Ids(simple3).SequenceEqual(cache.Pages[2].Items.Take(25).Select(x => x.PublishedFileId)), "Simple page 3 is native page 2 positions 1-25");
        await ExpectError(["steamworkshops", "search", "next"], "SEARCH_PAGE_LIMIT_EXCEEDED");
        await ExpectError(["steamworkshops", "search", "Harmony", "--page", "4"], "SEARCH_PAGE_LIMIT_EXCEEDED");
        await Run(["steamworkshops", "search", "Harmony", "--page", "2"]);
        await Run(["steamworkshops", "search", "Harmony", "--page", "3"]);
        await Run(["steamworkshops", "search", "Harmony", "--details"]);
        var detail2 = await Run(["steamworkshops", "search", "next"]);
        Check(detail2.GetProperty("pageSize").GetInt32() == 10 && detail2.GetProperty("details").GetBoolean(), "Next preserves detailed mode");
        var detail5 = await Run(["steamworkshops", "search", "Harmony", "--details", "--page", "5"]);
        cache = WorkshopSearchPages.Read(context, DateTimeOffset.UtcNow);
        Check(Ids(detail5).SequenceEqual(cache.Pages[1].Items.Skip(40).Take(10).Select(x => x.PublishedFileId)), "Detailed page 5 maps to native positions 41-50");
        await ExpectError(["steamworkshops", "search", "next"], "SEARCH_PAGE_LIMIT_EXCEEDED");
        await ExpectError(["steamworkshops", "search", "Harmony", "--details", "--page", "6"], "SEARCH_PAGE_LIMIT_EXCEEDED");
        Check(simple1.GetProperty("items").EnumerateArray().All(x => x.EnumerateObject().Count() == 3)
            && detail5.GetProperty("items").EnumerateArray().All(x => x.EnumerateObject().Count() == 4), "Actual compact output fields");
        evidence.Add(new { check = "search-mapping", simplePage1 = Ids(simple1).Length, simplePage2 = Ids(simple2).Length,
            simplePage3 = Ids(simple3).Length, detailsPage5 = Ids(detail5).Length, sourceNext = "cache", artifacts = new[] { ProjectDataPaths.File(Project, "steamworkshops-search-cache.json") } });
        await ExpectError(["steamworkshops", "upload", "--dry-run"], "ASSET_PACK_UNVERIFIED");
        var sourceCs = Directory.GetFiles(Path.Combine(Project, "source"), "*.cs", SearchOption.AllDirectories).First(p => !p.Contains("\\obj\\"));
        var sample = Path.Combine(Target, "LoadFolders.xml");
        await Fault(context, "target", "added", Path.Combine(Target, "RimDevToolsIntegrityProbe.txt"), [1, 2, 3]);
        await Fault(context, "target", "missing", sample, null);
        var changed = File.ReadAllBytes(sample); changed[^1] ^= 1;
        await Fault(context, "target", "modified", sample, changed);
        await Fault(context, "project", "added", Path.Combine(Project, "1.7", "Textures", "RimDevToolsIntegrityProbe.txt"), [1, 2, 3]);
        var source = File.ReadAllBytes(sourceCs); var sourceChanged = source.Concat(new byte[] { 32 }).ToArray();
        await Fault(context, "project", "modified", sourceCs, sourceChanged);
        await ExpectError(["steamworkshops", "upload", "--dry-run"], "ASSET_PACK_UNVERIFIED");
        evidence.Add(new { check = "final-restored-baseline", deployRunId = DeploymentReceiptStore.Read(context)!.DeployRunId,
            targetFiles = DeploymentReceiptStore.ScanTarget(Target).Length, publishedFileId = "3833398574", remoteWrites = 0 });
    }

    private static string[] Ids(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("publishedFileId").GetString()!).ToArray();

    private static async Task Fault(ProjectContext context, string scope, string kind, string path, byte[]? replacement)
    {
        Check(PathSafety.IsWithin(path, scope == "target" ? Target : Project), "Fault injection restricted to authorized path");
        var original = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var config = GameEnvironment.ConfigDirectory(context)!;
        try
        {
            using var gate = EnvironmentLock.TryAcquire(config, "steamworkshops live integrity", Project, Guid.NewGuid().ToString("N"), out _)
                ?? throw new Exception("Environment busy during controlled fault");
            if (replacement is null) File.Delete(path); else File.WriteAllBytes(path, replacement);
            var error = await ExpectError(["steamworkshops", "upload", "--dry-run"], scope == "project" ? "PROJECT_NOT_DEPLOYED" : "DEPLOYMENT_VERIFICATION_FAILED");
            Check(error.GetProperty("details").GetProperty("differences").EnumerateArray().Any(x => x.GetProperty("scope").GetString() == scope
                && x.GetProperty("kind").GetString() == kind && x.GetProperty("path").GetString() == Path.GetRelativePath(scope == "target" ? Target : Project, path).Replace('\\', '/')), "Actual difference reported");
            evidence.Add(new { check = "integrity-rejection", scope, kind, path });
        }
        finally
        {
            if (original is null) { if (File.Exists(path)) File.Delete(path); } else File.WriteAllBytes(path, original);
            // Normal deployment creates a new baseline after every restored fault.
            await Run(["deploy"]);
        }
    }

    private static async Task<JsonElement> Run(string[] args)
    {
        var invocation = await Invoke(args);
        using var doc = JsonDocument.Parse(invocation.Output);
        Check(invocation.Exit == 0 && doc.RootElement.GetProperty("ok").GetBoolean(), string.Join(' ', args) + ": " + invocation.Output);
        return doc.RootElement.GetProperty("result").Clone();
    }
    private static async Task<JsonElement> ExpectError(string[] args, string code)
    {
        var invocation = await Invoke(args);
        using var doc = JsonDocument.Parse(invocation.Output);
        Check(invocation.Exit != 0 && !doc.RootElement.GetProperty("ok").GetBoolean() && doc.RootElement.GetProperty("error").GetProperty("code").GetString() == code,
            "Expected " + code + ": " + invocation.Output);
        return doc.RootElement.GetProperty("error").Clone();
    }
    private static async Task<(int Exit, string Output)> Invoke(string[] args, bool parse = true)
    {
        Console.Error.WriteLine("LIVE " + string.Join(' ', args));
        var start = new ProcessStartInfo(tool) { WorkingDirectory = Project, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new Exception("Unable to start CLI");
        var output = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); throw; }
        var text = await output; var error = await stderr;
        if (!string.IsNullOrWhiteSpace(error)) Console.Error.WriteLine(error.Trim());
        if (parse) using (JsonDocument.Parse(text)) { }
        return (process.ExitCode, text.TrimStart('\uFEFF'));
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
