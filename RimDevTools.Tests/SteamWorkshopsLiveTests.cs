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
    private static SteamWorkshopsTestIdentityScope? activeIdentity;

    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            tool = Path.GetFullPath(CliArgs.RequiredValue(args, "--tool"));
            var batch = CliArgs.RequiredValue(args, "--batch");
            Check(batch is "1" or "2", "Supported live batches: 1, 2");
            Check(Environment.GetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE") is null && SteamworksSessionProbe.ProbeOverride is null
                && SteamSessionGuard.ProbeOverride is null && ProcessUtil.RimWorldRunningOverride is null && WorkshopPublishingService.PublishOverride is null, "Live driver refuses fake mode and injected probes");
            Check(File.Exists(tool) && Path.GetFileName(tool).Equals("RimDevTools.exe", StringComparison.OrdinalIgnoreCase), "Use actual release CLI executable");
            await SteamWorkshopsTestIdentityScope.RecoverLocalAsync(Run);
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
            if (batch == "1") await Batch1(context);
            else if (args.Contains("--prepare-inputs")) PrepareBatch2Inputs();
            else if (args.Contains("--reference-bake")) await ReferenceBake(context);
            else if (args.Contains("--bake-probe")) await SampleBakeProbe();
            else await Batch2(context);
            Console.WriteLine(JsonSerializer.Serialize(new { ok = true, mode = "live-steamworkshops", batch = int.Parse(batch), partialBatch = args.Any(x => x is "--prepare-inputs" or "--reference-bake" or "--bake-probe"), tool, project = Project, target = Target, evidence }));
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { ok = false, mode = "live-steamworkshops", tool, error = ex.Message, evidence }));
            return 1;
        }
    }

    private static async Task Batch2(ProjectContext context)
    {
        PrepareBatch2Inputs();
        await Run(["deploy"]);
        await ReferenceBake(context);
        await Run(["steamworkshops", "upload", "--dry-run"]);
        var upload = await Run(["steamworkshops", "upload"]);
        Check(!upload.GetProperty("created").GetBoolean() && upload.GetProperty("submitResult").GetInt32() == 1
            && upload.GetProperty("publishedFileId").GetString() == "3833398574"
            && upload.GetProperty("gameProcessIdsBefore").GetArrayLength() == 0 && upload.GetProperty("gameProcessIdsAfter").GetArrayLength() == 0, "Authorized existing item update without running game");
        evidence.Add(new { check = "independent-upload", operationId = upload.GetProperty("operationId").GetString(), deployRunId = upload.GetProperty("deployRunId").GetString(), contentBytes = upload.GetProperty("contentBytes").GetInt64() });
        var initial = await Run(["steamworkshops", "status"]);
        var edited = await Run(["steamworkshops", "edit"]);
        Check(!edited.GetProperty("setItemContentCalled").GetBoolean(), "Default edit is metadata only");
        var projectDescription = System.Xml.Linq.XDocument.Load(Path.Combine(Project, "About", "About.xml")).Root!.Element("description")!.Value.Trim();
        var afterDescription = await Run(["steamworkshops", "status"]);
        Check(afterDescription.GetProperty("description").GetString() == projectDescription && afterDescription.GetProperty("fileBytes").GetInt32() == initial.GetProperty("fileBytes").GetInt32(), "Real description changed without content transfer");
        var descriptionPath = ProjectDataPaths.File(Project, "workshop-edit-description.txt");
        var manualDescription = "[h1]奶牛枪元数据保留验证[/h1]\nRimDevTools content upload must preserve this explicit edit. " + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(descriptionPath, manualDescription);
            var preview = Path.Combine(Project, "1.7", "Textures", "WorkshopTestAssets", "ShezhangOC", "CEO", "Icon", "President.png");
            var metadata = await Run(["steamworkshops", "edit", "--description-file", descriptionPath, "--preview-file", preview, "--visibility", "private"]);
            Check(!metadata.GetProperty("setItemContentCalled").GetBoolean(), "Explicit metadata fields do not call SetItemContent");
            var customized = await Run(["steamworkshops", "status"]);
            Check(customized.GetProperty("description").GetString() == manualDescription && customized.GetProperty("previewUrl").GetString() != initial.GetProperty("previewUrl").GetString(), "Real different preview and explicit description");
            var plan = await Run(["deploy", "--upload", "--dry-run"]);
            Check(plan.GetProperty("upload").GetProperty("verification").GetString() == "pending_after_deploy", "Combined dry-run defers future output checks");
            await ExpectError(["deploy", "--xml-hot", "--upload"], "INVALID_ARGUMENT");
            var combined = await Run(["deploy", "--upload"]);
            Check(combined.GetProperty("deploymentSucceeded").GetBoolean() && combined.GetProperty("uploadSucceeded").GetBoolean(), "Both real combined stages succeed");
            var preserved = await Run(["steamworkshops", "status"]);
            Check(preserved.GetProperty("description").GetString() == manualDescription
                && preserved.GetProperty("previewUrl").GetString() == customized.GetProperty("previewUrl").GetString()
                && preserved.GetProperty("visibility").GetString() == "private"
                && preserved.GetProperty("tags").GetString() == customized.GetProperty("tags").GetString(), "Existing content upload preserves all manually edited metadata");
            evidence.Add(new { check = "metadata-and-combined-upload", editOperationId = metadata.GetProperty("operationId").GetString(),
                combinedOperationId = combined.GetProperty("upload").GetProperty("operationId").GetString(),
                deployRunId = combined.GetProperty("deployment").GetProperty("runId").GetString(), contentPreservedByEdit = true, metadataPreservedByContent = true });
        }
        finally
        {
            File.WriteAllText(descriptionPath, projectDescription);
            await Run(["steamworkshops", "edit", "--description-file", descriptionPath, "--preview-file", Path.Combine(Project, "About", "Preview.png"), "--visibility", "private"]);
            File.Delete(descriptionPath);
        }
        var packPath = Path.Combine(Target, "1.7", "Assets.zip");
        var corrupt = File.ReadAllBytes(packPath); corrupt[0] ^= 1;
        await Fault(context, "target", "modified", packPath, corrupt);
        await Fault(context, "target", "added", Path.Combine(Target, "RimDevToolsIntegrityProbe.txt"), [1, 2, 3]);
        var sample = Path.Combine(Target, "LoadFolders.xml");
        await Fault(context, "target", "missing", sample, null);
        var sameLength = File.ReadAllBytes(sample); sameLength[^1] ^= 1;
        await Fault(context, "target", "modified", sample, sameLength);
        var sourceCs = Path.Combine(Project, "source", "CowGun", "Projectile_CowGun.cs");
        await Fault(context, "project", "modified", sourceCs, File.ReadAllBytes(sourceCs).Concat(new byte[] { 32 }).ToArray());
        await FirstUploadAndVisibility();
        var game = await Run(["test", "run", "--mode", "quick", "--verify-workshop-assets"]);
        Check(game.GetProperty("workshopAssetsVerified").GetBoolean() && game.GetProperty("logSummary").GetProperty("errorCount").GetInt32() == 0, "Fresh runtime package-only read");
        evidence.Add(new { check = "package-only-game-read", runId = game.GetProperty("runId").GetString(), artifacts = game.GetProperty("artifacts").Clone() });
        await Run(["steamworkshops", "upload", "--dry-run"]);
        var final = await Run(["steamworkshops", "status"]);
        Check(final.GetProperty("publishedFileId").GetString() == "3833398574" && final.GetProperty("visibility").GetString() == "private", "Final main identity is private and restored");
        evidence.Add(new { check = "batch2-final-baseline", deployRunId = DeploymentReceiptStore.Read(context)!.DeployRunId, publishedFileId = "3833398574" });
    }

    private static async Task FirstUploadAndVisibility()
    {
        var scope = SteamWorkshopsTestIdentityScope.Begin(Run); activeIdentity = scope;
        try
        {
            await Run(["deploy"]);
            var first = await Run(["steamworkshops", "upload"]);
            var fresh = scope.PublishedFileId is null;
            Check(first.GetProperty("created").GetBoolean() == fresh, "Automatic creation/update matches this exact test identity");
            scope.CaptureCreatedId(first);
            var second = await Run(["steamworkshops", "upload"]);
            Check(!second.GetProperty("created").GetBoolean() && second.GetProperty("publishedFileId").GetString() == scope.PublishedFileId, "Second test upload updates the same item");
            var visibilityEvidence = new List<object>();
            foreach (var visibility in new[] { "private", "public", "friends-only", "unlisted", "private" })
            {
                var edit = await Run(["steamworkshops", "edit", "--visibility", visibility]);
                var status = await Run(["steamworkshops", "status"]);
                Check(status.GetProperty("publishedFileId").GetString() == scope.PublishedFileId && status.GetProperty("visibility").GetString() == visibility, "Real visibility readback: " + visibility);
                visibilityEvidence.Add(new { visibility, operationId = edit.GetProperty("operationId").GetString() });
            }
            evidence.Add(new { check = "first-upload-and-visibility", packageId = scope.PackageId, publishedFileId = scope.PublishedFileId,
                url = "https://steamcommunity.com/sharedfiles/filedetails/?id=" + scope.PublishedFileId,
                creationOperationId = first.GetProperty("operationId").GetString(), updateOperationId = second.GetProperty("operationId").GetString(), visibilities = visibilityEvidence, retainedForUserCleanup = true });
            scope.MarkFinished();
        }
        finally
        {
            activeIdentity = null;
            await scope.RestoreMainAsync();
        }
    }

    private static async Task ReferenceBake(ProjectContext context)
    {
        Check(!ProcessUtil.IsRimWorldRunning(), "Close games before official reference verification");
        using var gate = EnvironmentLock.TryAcquire(GameEnvironment.ConfigDirectory(context)!, "official reference bake test", Project, Guid.NewGuid().ToString("N"), out _)
            ?? throw new Exception("Environment busy");
        var receipt = DeploymentReceiptStore.Read(context) ?? throw new Exception("Missing normal deployment receipt");
        var normalBake = receipt.AssetBake ?? throw new Exception("Missing official bake metadata");
        Check(receipt.AssetBake?.Succeeded == true && receipt.AssetBake.Packs.Sum(p => p.SourceTextures) == 180
            && receipt.AssetBake.Packs.Sum(p => p.SourceSounds) == 1, "Normal deploy baked all PNGs and WAV");
        var staging = Path.Combine(Game, "Mods", ".rimdev-staging-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            foreach (var file in receipt.Files)
            {
                if (Path.GetFileName(file.Path).StartsWith("Assets.zip", StringComparison.OrdinalIgnoreCase)) continue;
                var destination = Path.Combine(staging, file.Path); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(Path.Combine(Target, file.Path), destination);
            }
            var start = new ProcessStartInfo(Path.Combine(Game, "RimWorld.exe"))
            {
                WorkingDirectory = Game, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { "-Headless", "-NoFileLog", "-BakeModAssetPacks=" + staging }) start.ArgumentList.Add(argument);
            Console.Error.WriteLine("LIVE explicit official reference: RimWorld.exe -Headless -BakeModAssetPacks=<owned staging>");
            using var process = Process.Start(start) ?? throw new Exception("Cannot start official reference mode");
            var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); throw; }
            var stdout = await output; var stderr = await errors;
            Check(process.ExitCode == 0, "Official reference mode failed: " + stderr + stdout);
            var inspection = await Invoke(["--official-bake-helper"], parse: false,
                input: JsonSerializer.Serialize(new OfficialBakeRequest(Game, staging, InspectOnly: true), JsonDefaults.Storage));
            var line = inspection.Output.Split('\n').LastOrDefault(x => x.StartsWith("RIMDEVTOOLS_OFFICIAL_BAKE=")) ?? throw new Exception(inspection.Output);
            var reference = JsonSerializer.Deserialize<OfficialBakeResult>(line["RIMDEVTOOLS_OFFICIAL_BAKE=".Length..], JsonDefaults.Storage)!;
            Check(reference.Succeeded && reference.Packs.Length == normalBake.Packs.Length, "Official reference package inspection");
            foreach (var expected in normalBake.Packs)
            {
                var actual = reference.Packs.Single(p => p.LoadFolder == expected.LoadFolder);
                Check(actual.Settings.OrderBy(p => p.Key).SequenceEqual(expected.Settings.OrderBy(p => p.Key)) && actual.Backend == expected.Backend, "Same official settings and CPU backend");
                Check(actual.EntrySha256.OrderBy(p => p.Key).SequenceEqual(expected.EntrySha256.OrderBy(p => p.Key)), "Every decoded official reference entry equals direct-library output");
            }
            evidence.Add(new { check = "official-reference-equality", producer = "RimWorld.exe dedicated Headless BakeModAssetPacks test mode", gameExitCode = process.ExitCode,
                sourceTextures = reference.Packs.Sum(p => p.SourceTextures), sourceSounds = reference.Packs.Sum(p => p.SourceSounds),
                comparedDecodedEntries = reference.Packs.Sum(p => p.EntrySha256.Count), backend = "official_cpu", receipt.DeployRunId });
        }
        finally { if (Directory.Exists(staging)) DeploymentFileTransaction.Remove(staging); }
    }

    private static void PrepareBatch2Inputs()
    {
        var aboutPath = Path.Combine(Project, "About", "About.xml");
        var about = System.Xml.Linq.XDocument.Load(aboutPath);
        Check(about.Root?.Element("packageId")?.Value == "trigger.cowgun", "Only the main authorized identity is prepared");
        var version = about.Root!.Element("modVersion")?.Value;
        Check(version is "0.1.1" or "0.1.2", "Unexpected source version during batch 2 preparation");
        var tone = Path.Combine(Project, "1.7", "Sounds", "WorkshopTestAssets", "CowGunTone.wav");
        WriteTone(tone);
        var configPath = Path.Combine(Project, "mod_config.jsonc");
        var config = File.ReadAllText(configPath);
        if (!config.Contains("1.7\\\\Sounds", StringComparison.Ordinal))
            config = config.Replace("\"1.7\\\\Textures\",", "\"1.7\\\\Textures\",\n        \"1.7\\\\Sounds\",", StringComparison.Ordinal);
        Check(ProjectConfig.Load(configPath).SyncItems.Contains("1.7\\Textures"), "Textures already part of full deployment");
        File.WriteAllText(configPath, config);
        Check(ProjectConfig.Load(configPath).SyncItems.Contains("1.7\\Sounds"), "Sounds added to actual sync scope");
        about.Root.SetElementValue("modVersion", "0.1.2");
        about.Root.SetElementValue("description", "\n[h1]奶牛枪[/h1]\n\n一把沿用原版速射机枪射速与弹道的武器。子弹命中敌方人类或人形生物时，会将其变成一头仍属于原阵营的奶牛。\n\n[b]0.1.2[/b]：包含 180 张作者授权的工坊资源打包测试贴图及一个本地合成短 WAV，用于官方资产包与游戏读取验证。射击前预热时间为 2 秒。\n");
        about.Save(aboutPath);
        var assemblyInfo = Path.Combine(Project, "source", "CowGun", "Properties", "AssemblyInfo.cs");
        File.WriteAllText(assemblyInfo, File.ReadAllText(assemblyInfo).Replace("1.0.1.0", "1.0.2.0", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(Project, "source", "CowGun", "WorkshopAssetProbeMod.cs"),
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "CowGunWorkshopAssetProbe.cs.txt")));
        var readme = Path.Combine(Project, "README.md");
        if (!File.ReadAllText(readme).Contains("CowGunTone.wav", StringComparison.Ordinal))
            File.AppendAllText(readme, "\n`1.7/Sounds/WorkshopTestAssets/CowGunTone.wav` 是工具生成的 0.2 秒、22050 Hz、单声道 PCM16 测试音频，只用于官方资源包与游戏读取验证，不来自游戏或其他作者的音频。\n");
        evidence.Add(new { check = "batch2-source-inputs-prepared", version = "0.1.2", sourceTone = tone, pngs = 180 });
    }

    private static async Task SampleBakeProbe()
    {
        var staging = Path.Combine(Game, "Mods", ".rimdev-staging-" + Guid.NewGuid().ToString("N"));
        Check(PathSafety.IsWithin(staging, Path.Combine(Game, "Mods")), "Sample staging stays inside game Mods");
        var context = ProjectDiscovery.Find(Project, out _)!;
        using var gate = EnvironmentLock.TryAcquire(GameEnvironment.ConfigDirectory(context)!, "official asset sample bake", Project, Guid.NewGuid().ToString("N"), out _)
            ?? throw new Exception("Environment busy");
        try
        {
            Directory.CreateDirectory(staging);
            File.Copy(Path.Combine(Project, "LoadFolders.xml"), Path.Combine(staging, "LoadFolders.xml"));
            foreach (var relative in new[] { "CEO/Weapon/PresidentSpear.png", "CEO/Icon/President.png", "CEO/Effects/ReflectShield/ForceField.png", "Wolfein/Heads/WolfeinHead11_south.png" })
            {
                var modRelative = "1.7/Textures/WorkshopTestAssets/ShezhangOC/" + relative;
                var destination = Path.Combine(staging, modRelative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(Path.Combine(Project, modRelative), destination);
            }
            WriteTone(Path.Combine(staging, "1.7", "Sounds", "WorkshopTestAssets", "CowGunTone.wav"));
            var response = await Invoke(["--official-bake-helper"], parse: false,
                input: JsonSerializer.Serialize(new OfficialBakeRequest(Game, staging), JsonDefaults.Storage));
            var line = response.Output.Split('\n').LastOrDefault(x => x.StartsWith("RIMDEVTOOLS_OFFICIAL_BAKE=")) ?? throw new Exception(response.Output);
            var result = JsonSerializer.Deserialize<OfficialBakeResult>(line["RIMDEVTOOLS_OFFICIAL_BAKE=".Length..], JsonDefaults.Storage)!;
            Check(result.Succeeded, "Official sample bake failed: " + string.Join('\n', result.Errors));
            Check(result.Packs.Length == 1 && result.Packs[0].SourceTextures == 4 && result.Packs[0].SourceSounds == 1, "All four PNG samples and WAV baked");
            evidence.Add(new { check = "official-sample-bake", result });
        }
        finally { if (Directory.Exists(staging)) DeploymentFileTransaction.Remove(staging); }
    }

    internal static void WriteTone(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = new BinaryWriter(File.Create(path));
        const int rate = 22050, samples = rate / 5;
        file.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); file.Write(36 + samples * 2);
        file.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); file.Write(16); file.Write((short)1); file.Write((short)1);
        file.Write(rate); file.Write(rate * 2); file.Write((short)2); file.Write((short)16);
        file.Write(System.Text.Encoding.ASCII.GetBytes("data")); file.Write(samples * 2);
        for (var i = 0; i < samples; i++) file.Write((short)(Math.Sin(i * Math.Tau * 440 / rate) * 5000));
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
            Check(error.GetProperty("details").GetProperty("submitCallCount").GetInt32() == 0, "Rejected before SubmitItemUpdate");
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
    private static async Task<(int Exit, string Output)> Invoke(string[] args, bool parse = true, string? input = null)
    {
        if (args.Length >= 2 && (args[0] == "steamworkshops" && args[1] is "upload" or "edit" || args[0] == "deploy" && args.Contains("--upload")))
        {
            var context = ProjectDiscovery.Find(Project, out _)!;
            var package = context.Config.CurrentPackageId(Project);
            var id = WorkshopUploadCommand.ReadId(Path.Combine(Project, "About", "PublishedFileId.txt"))?.ToString();
            Check(package == "trigger.cowgun" && id == "3833398574" || activeIdentity is not null && package == activeIdentity.PackageId
                && id == activeIdentity.PublishedFileId, "Write is restricted to the main identity or the exact active test identity");
            if (CliArgs.Value(args, "--id") is { } explicitId) Check(explicitId == "3833398574" || explicitId == activeIdentity?.PublishedFileId, "Explicit writes restricted to authorized exact IDs");
        }
        Console.Error.WriteLine("LIVE " + string.Join(' ', args));
        var start = new ProcessStartInfo(tool) { WorkingDirectory = Project, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input is not null };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new Exception("Unable to start CLI");
        if (input is not null) { await process.StandardInput.WriteLineAsync(input); process.StandardInput.Close(); }
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
