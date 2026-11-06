using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using RimDevTools;

internal static partial class ToolUpdateTests
{
    private static readonly string CurrentVersion = typeof(CliApplication).Assembly.GetName().Version!.ToString();
    private static readonly string FutureVersion = new Version(1, 3, 1, 0).ToString();
    private const string DownloadUrl = "https://github.com/1264600905/RimDevTools/releases/download/test/RimDevTools-1.3.1.0-Tools.zip";
    private static int _passed;

    internal static async Task<int> RunAsync(string temporary)
    {
        _passed = 0;
        var originalLocator = UpdateInstallation.LocateOverride;
        var originalClient = ToolReleaseSource.ClientFactory;
        try
        {
            var root = Path.Combine(temporary, "tool-update-unit");
            Directory.CreateDirectory(root);
            var original = Fixture(Path.Combine(root, "original"), CurrentVersion);
            var target = Fixture(Path.Combine(root, "target"), FutureVersion);
            Assert(ToolReleaseSource.ParseVersion("v1.3.0") == ToolReleaseSource.ParseVersion("1.3.0.0"), "update versions normalize three and four components");
            foreach (var path in new[] { ".tools/Ilspy/NUL.txt", ".tools/Ilspy/CON", ".tools/Ilspy/a.", ".tools/Ilspy/a ", ".tools/Ilspy/../x", ".tools/Data/project-template.manifest.json", "mod_config.jsonc", ".tools/ProjectData/secret", ".tools/Ilspy/x:stream" })
                Assert(!ToolPackageManifest.IsManagedPath(path), "update whitelist rejects " + path);
            var manifest = ToolPackageManifest.Load(target.Root);
            var source = "{\r\n  // retained 用户注释\r\n  \"config_version\": 2,\r\n  \"game_version\": \"1.6\",\r\n  \"sync_items\": [],\r\n  \"sync_source\": false,\r\n  \"timeouts\": {\"max_wait\":0},\r\n  \"unknown\": {\"url\":\"https://example.invalid/x//y\",\"nested\":[null,false,0,[]]},\r\n}\r\n";
            var sourceBytes = Encoding.UTF8.Preamble.ToArray().Concat(Encoding.UTF8.GetBytes(source)).ToArray();
            var migrated = JsoncConfigMigration.Prepare(sourceBytes, manifest);
            var resultText = Encoding.UTF8.GetString(migrated.Content).TrimStart('\uFEFF');
            Assert(migrated.Changed && migrated.Content.AsSpan().StartsWith(Encoding.UTF8.Preamble) && resultText.Replace("\"existing_project_mode\": false,\r\n  ", "") == source, "JSONC migration adds only the missing mode while retaining BOM, CRLF, comments and unknown values");
            foreach (var mode in new[] { "true", "false" })
            {
                var bytes = Encoding.UTF8.GetBytes(source.Replace("\"config_version\": 2,", "\"config_version\": 2,\"existing_project_mode\":" + mode + ","));
                Assert(JsoncConfigMigration.Prepare(bytes, manifest).Content.SequenceEqual(bytes), "JSONC retains explicit mode " + mode + " byte-for-byte");
            }
            foreach (var invalid in new[] { "{\"config_version\":1}", "{\"config_version\":2,\"existing_project_mode\":null}", "{\"config_version\":2,\"Config_Version\":2}", "{\"config_version\":2,\"x\":{\"a\":1,\"A\":2}}", "{broken" })
                Throws(() => JsoncConfigMigration.Prepare(Encoding.UTF8.GetBytes(invalid), manifest), "JSONC rejects invalid, unsupported or ambiguous configuration");

            var zip = CreateArchive(target.Root, Path.Combine(root, "target.zip"));
            var release = Release(zip);
            var calls = 0;
            ToolReleaseSource.ClientFactory = () => new HttpClient(new Handler(async (request, token) =>
            {
                calls++;
                await Task.Yield();
                return request.RequestUri!.AbsoluteUri == ToolReleaseSource.ApiUrl ? JsonResponse(Metadata(release)) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(zip)) };
            }));
            UpdateInstallation.LocateOverride = () => original;
            var checkedResult = await Capture(() => CliApplication.RunAsync(["update", "--check"]));
            Assert(checkedResult.ExitCode == 0 && checkedResult.Output.Contains("updateAvailable"), "explicit check reports a newer stable version without game or Steam");
            var configBefore = File.ReadAllBytes(original.Config);
            var baselineBefore = File.ReadAllBytes(Path.Combine(original.Root, ToolPackageManifest.RelativePath));
            var preview = await Capture(() => CliApplication.RunAsync(["update", "--dry-run"]));
            Assert(preview.ExitCode == 0 && preview.Output.Contains("ready") && !File.Exists(original.StatePath)
                && File.ReadAllBytes(original.Config).SequenceEqual(configBefore) && File.ReadAllBytes(Path.Combine(original.Root, ToolPackageManifest.RelativePath)).SequenceEqual(baselineBefore), "dry-run verifies the ZIP and leaves config, baseline and state unchanged");
            await ToolStartupUpdateCheck.RunAsync(original, CancellationToken.None);
            var cachedCalls = calls;
            await ToolStartupUpdateCheck.RunAsync(original, CancellationToken.None);
            Assert(calls == cachedCalls, "startup version checks reuse the 24-hour cache");
            var help = await Capture(() => CliApplication.RunAsync(["update", "--help"]));
            var status = await Capture(() => CliApplication.RunAsync(["update", "status"]));
            var version = await Capture(() => CliApplication.RunAsync(["--version"]));
            Assert(help.ExitCode == 0 && status.ExitCode == 0 && version.ExitCode == 0 && calls == cachedCalls, "help, version and update status perform no network requests");
            File.WriteAllText(original.CachePath, "{broken cache");
            await ToolStartupUpdateCheck.RunAsync(original, CancellationToken.None);
            Assert(calls > cachedCalls && JsonDocument.Parse(File.ReadAllText(original.CachePath)).RootElement.TryGetProperty("nextCheckUtc", out _), "corrupt startup cache is refreshed without changing business behavior");
            var badArgs = await Capture(() => CliApplication.RunAsync(["update", "--check", "--dry-run"]));
            Assert(badArgs.ExitCode == ExitCodes.InvalidArguments, "update rejects mutually exclusive flags");

            var skill = Path.Combine(original.Root, ".agents/skills/rimdevtools/SKILL.md");
            File.AppendAllText(skill, "user edit");
            var conflict = await Capture(() => CliApplication.RunAsync(["update", "--dry-run"]));
            Assert(conflict.ExitCode != 0 && conflict.Output.Contains("UPDATE_CONFLICT") && conflict.Output.Contains("SKILL.md") && File.ReadAllText(skill).EndsWith("user edit"), "local skill edits block the whole update and report conflict paths");
            File.WriteAllText(skill, "fixture .agents/skills/rimdevtools/SKILL.md");

            var downloaded = Path.Combine(root, "download.zip");
            var fallback = release with { Sha256 = null, ChecksumUrl = DownloadUrl + ".sha256" };
            ToolReleaseSource.ClientFactory = () => new HttpClient(new Handler((request, _) => Task.FromResult(
                request.RequestUri!.AbsoluteUri.EndsWith(".sha256") ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(release.Sha256 + "  " + release.AssetName + "\n") }
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(zip)) })));
            await ToolReleaseSource.DownloadAsync(fallback, downloaded, CancellationToken.None);
            Assert(ToolPackageManifest.Hash(downloaded) == release.Sha256, "download verifies a release checksum attachment when API digest is absent");
            await ThrowsAsync(() => ToolReleaseSource.DownloadAsync(release with { Sha256 = new string('0', 64) }, Path.Combine(root, "bad-download.zip"), CancellationToken.None), "download rejects a wrong digest");

            foreach (var metadata in new[] { Metadata(release, prerelease: true), Metadata(release, draft: true), Metadata(release, assets: false), Metadata(release, digest: "sha256:bad") })
            {
                ToolReleaseSource.ClientFactory = () => new HttpClient(new Handler((_, _) => Task.FromResult(JsonResponse(metadata))));
                await ThrowsAsync(() => ToolReleaseSource.LatestAsync(CancellationToken.None), "release validation rejects prerelease, draft, missing asset or malformed digest");
            }
            ToolReleaseSource.ClientFactory = () => new HttpClient(new Handler((_, _) => Task.FromResult(JsonResponse(Metadata(release, version: "1.0.0.0", assets: false)))));
            var older = await Capture(() => CliApplication.RunAsync(["update"]));
            Assert(older.ExitCode == 0 && older.Output.Contains("upToDate") && !File.Exists(original.StatePath), "an older release without Tools never causes downgrade or installation");
            ToolReleaseSource.ClientFactory = () => new HttpClient(new Handler((_, _) => Task.FromResult(JsonResponse(Metadata(release, version: CurrentVersion, assets: false)))));
            var equal = await Capture(() => CliApplication.RunAsync(["update"]));
            Assert(equal.ExitCode == 0 && equal.Output.Contains("upToDate") && !File.Exists(original.StatePath), "the same version is a no-op without scheduling an installation");
            ToolReleaseSource.ClientFactory = () => new HttpClient(new Handler(async (_, token) => { await Task.Delay(TimeSpan.FromSeconds(10), token); throw new HttpRequestException(); }));
            File.Delete(original.CachePath);
            var stopwatch = Stopwatch.StartNew();
            var business = await Capture(() => CliApplication.RunAsync(["workshop", "upload"]));
            Assert(stopwatch.Elapsed < TimeSpan.FromSeconds(3) && business.ExitCode == ExitCodes.InvalidArguments && business.Output.Contains("WORKSHOP_LEGACY_ENTRY_DISABLED"), "startup timeout stays within the two-second budget and preserves business failure");

            foreach (var extra in new[] { "../escape", ".tools/Ilspy/../../outside", ".tools/ProjectData/fake", ".tools/Data/project-template.manifest.json", ".agents/skills/other/SKILL.md" })
            {
                var malicious = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
                File.Copy(zip, malicious);
                using (var archive = ZipFile.Open(malicious, ZipArchiveMode.Update))
                using (var writer = new StreamWriter(archive.CreateEntry(extra).Open())) writer.Write("bad");
                Throws(() => ToolUpdatePlan.Extract(malicious, Path.Combine(root, Guid.NewGuid().ToString("N"))), "ZIP whitelist rejects " + extra);
            }
            var collision = Path.Combine(root, "collision.zip");
            File.Copy(zip, collision);
            using (var archive = ZipFile.Open(collision, ZipArchiveMode.Update))
            using (var writer = new StreamWriter(archive.CreateEntry(".AGENTS/skills/rimdevtools/SKILL.md").Open())) writer.Write("collision");
            Throws(() => ToolUpdatePlan.Extract(collision, Path.Combine(root, "collision")), "ZIP rejects Windows case collisions");
            var symlink = Path.Combine(root, "symlink.zip");
            File.Copy(zip, symlink);
            using (var archive = ZipFile.Open(symlink, ZipArchiveMode.Update))
            {
                var entry = archive.CreateEntry(".tools/Ilspy/link");
                entry.ExternalAttributes = unchecked((int)0xA1FF0000);
            }
            Throws(() => ToolUpdatePlan.Extract(symlink, Path.Combine(root, "symlink")), "ZIP rejects symbolic links before extraction");
            var tampered = Path.Combine(root, "tampered.zip");
            File.Copy(zip, tampered);
            using (var archive = ZipFile.Open(tampered, ZipArchiveMode.Update))
            {
                archive.GetEntry(".tools/Ilspy/ilspycmd.exe")!.Delete();
                using var writer = new StreamWriter(archive.CreateEntry(".tools/Ilspy/ilspycmd.exe").Open());
                writer.Write("tampered");
            }
            Throws(() => ToolUpdatePlan.Extract(tampered, Path.Combine(root, "tampered")), "ZIP file hashes must match the declared manifest");

            ToolReleaseSource.ClientFactory = () => new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
            var unavailable = await Capture(() => CliApplication.RunAsync(["update", "--check"]));
            Assert(unavailable.ExitCode == ExitCodes.TaskFailed && unavailable.Output.Contains("UPDATE_CHECK_FAILED") && !unavailable.Output.Contains("upToDate"), "explicit network failure is reported as unable to check, never up to date");
            var failedHandoff = Fixture(Path.Combine(root, "failed-handoff"), CurrentVersion);
            UpdateInstallation.LocateOverride = () => failedHandoff;
            ToolReleaseSource.ClientFactory = () => new HttpClient(new Handler((request, _) => Task.FromResult(request.RequestUri!.AbsoluteUri == ToolReleaseSource.ApiUrl
                ? JsonResponse(Metadata(release)) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(zip)) })));
            var failedUpdate = await Capture(() => CliApplication.RunAsync(["update"]));
            Assert(failedUpdate.ExitCode == ExitCodes.TaskFailed && failedUpdate.Output.Contains("UPDATE_FAILED") && failedHandoff.ReadState()?.Status == "failed"
                && File.ReadAllText(failedHandoff.Executable) == "fixture .tools/RimDevTools.exe", "helper launch failure is never reported as scheduled and leaves the original executable intact");

            var targetManifest = ToolPackageManifest.Load(target.Root);
            targetManifest.RuntimeMajor = 100;
            AtomicFile.WriteAllText(Path.Combine(target.Root, ToolPackageManifest.RelativePath), JsonSerializer.Serialize(targetManifest, JsonDefaults.Storage));
            var runtimeZip = CreateArchive(target.Root, Path.Combine(root, "runtime.zip"));
            Throws(() => Prepare(original, runtimeZip, Release(runtimeZip)), "incompatible Runtime requirements stop before committing tool files");

            ToolReleaseSource.ClientFactory = originalClient;
            await TestTransactions(root, zip, release);
            UpdateInstallation.LocateOverride = () => null;
            var sourceRejected = await Capture(() => CliApplication.RunAsync(["update", "--check"]));
            Assert(sourceRejected.ExitCode == ExitCodes.InvalidArguments && sourceRejected.Output.Contains("UPDATE_INSTALLATION_UNRECOGNIZED"), "source or unknown installations cannot self-update");
            Console.WriteLine($"PASS {_passed} update checks");
            return _passed;
        }
        finally
        {
            UpdateInstallation.LocateOverride = originalLocator;
            ToolReleaseSource.ClientFactory = originalClient;
            ToolUpdateTransaction.AfterWriteOverride = null;
        }
    }

    private static async Task TestTransactions(string temporary, string zip, ToolRelease release)
    {
        foreach (var failAt in new[] { 1, 2 })
        {
            var installation = Fixture(Path.Combine(temporary, "rollback-" + failAt), CurrentVersion);
            var plan = Prepare(installation, zip, release);
            var before = ToolPackageManifest.Hash(installation.Executable);
            ToolUpdateTransaction.AfterWriteOverride = index => { if (index == failAt) throw new UnauthorizedAccessException("injected commit failure"); };
            var state = new UpdateState { Status = "scheduled" };
            await ToolUpdateTransaction.ApplyAsync(installation, plan, state);
            Assert(state.Status == "rolledBack" && ToolPackageManifest.Hash(installation.Executable) == before, "transaction restores all originals after failure at write " + failAt);
            ToolPackageManifest.Load(installation.Root).VerifyFiles(installation.Root);
        }
        ToolUpdateTransaction.AfterWriteOverride = null;
        var busy = Fixture(Path.Combine(temporary, "busy"), CurrentVersion);
        var busyPlan = Prepare(busy, zip, release);
        using (var lease = busy.EnterBusiness(out var leaseError))
        {
            Assert(lease is not null && leaseError is null && busy.HasBusinessLeases(), "business commands hold an installation lease");
            await ThrowsAsync(() => ToolUpdateTransaction.ScheduleAsync(busy, busyPlan, CancellationToken.None), "update cannot schedule over a live business lease");
        }
        var changedConfig = Fixture(Path.Combine(temporary, "config-concurrent"), CurrentVersion);
        var changedPlan = Prepare(changedConfig, zip, release);
        File.AppendAllText(changedConfig.Config, "\n// concurrent edit");
        var configState = new UpdateState();
        await ToolUpdateTransaction.ApplyAsync(changedConfig, changedPlan, configState);
        Assert(configState.Status == "failed" && File.ReadAllText(changedConfig.Config).EndsWith("// concurrent edit"), "concurrent config edits stop before writes and are preserved");
        var locked = Fixture(Path.Combine(temporary, "locked"), CurrentVersion);
        var lockedPlan = Prepare(locked, zip, release);
        using (var handle = new FileStream(locked.Executable, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var state = new UpdateState();
            await ToolUpdateTransaction.ApplyAsync(locked, lockedPlan, state);
            Assert(state.Status == "failed", "a locked managed file prevents partial installation");
        }
        var missingBaseline = Fixture(Path.Combine(temporary, "unknown-baseline"), CurrentVersion);
        File.Delete(Path.Combine(missingBaseline.Root, ToolPackageManifest.RelativePath));
        Throws(() => Prepare(missingBaseline, zip, release), "missing baseline is never assumed unmodified");
        var deleted = Fixture(Path.Combine(temporary, "deleted-managed"), CurrentVersion);
        File.Delete(Path.Combine(deleted.Root, ".claude/skills/rimdevtools/SKILL.md"));
        Throws(() => Prepare(deleted, zip, release), "missing or deleted managed skill blocks update");
        var newCollision = Fixture(Path.Combine(temporary, "new-collision"), CurrentVersion);
        File.WriteAllText(Path.Combine(newCollision.Root, ".agents/skills/rimdevtools/new-managed.md"), "user added content");
        Throws(() => Prepare(newCollision, zip, release), "an untracked local file at a newly managed path blocks update");
        var recovery = Fixture(Path.Combine(temporary, "recovery-conflict"), CurrentVersion);
        var recoveryPlan = Prepare(recovery, zip, release);
        var changedPath = recoveryPlan.Destination(recoveryPlan.Changes[0].Path);
        ToolUpdateTransaction.AfterWriteOverride = index =>
        {
            if (index != 1) return;
            File.WriteAllText(changedPath, "unexpected concurrent user edit");
            throw new IOException("injected failure with conflict");
        };
        var recoveryState = new UpdateState();
        await ToolUpdateTransaction.ApplyAsync(recovery, recoveryPlan, recoveryState);
        Assert(recoveryState.Status == "recoveryRequired" && File.ReadAllText(changedPath) == "unexpected concurrent user edit" && File.Exists(recoveryPlan.Backup(recoveryPlan.Changes[0].Path)), "rollback preserves unknown concurrent edits and retains recovery backups");
        ToolUpdateTransaction.AfterWriteOverride = null;
        var incompatible = Fixture(Path.Combine(temporary, "incompatible-game"), CurrentVersion);
        File.WriteAllText(incompatible.Config, "{\"config_version\":2,\"game_version\":\"unknown\"}");
        Throws(() => Prepare(incompatible, zip, release), "unsupported project game version stops before installation");
        var cleanup = Fixture(Path.Combine(temporary, "retention"), CurrentVersion);
        var completeDirectories = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var complete = Prepare(cleanup, zip, release);
            complete.Outcome = "rolledBack";
            complete.Save();
            completeDirectories.Add(complete.DirectoryPath);
        }
        var pending = Prepare(cleanup, zip, release);
        pending.Save();
        cleanup.WriteState(new UpdateState { Status = "succeeded", TransactionDirectory = completeDirectories[^1] });
        ToolUpdateTransaction.CleanupCompleted(cleanup);
        Assert(completeDirectories.Count(Directory.Exists) == 3 && Directory.Exists(pending.DirectoryPath), "retention keeps three completed transactions and all incomplete recovery data");
        var stale = Fixture(Path.Combine(temporary, "stale-state"), CurrentVersion);
        stale.WriteState(new UpdateState { Status = "applying", ParentPid = 0, HelperPid = 0, RecoveryCommand = "retained-helper" });
        UpdateInstallation.LocateOverride = () => stale;
        var statusBefore = File.ReadAllBytes(stale.StatePath);
        var status = await Capture(() => CliApplication.RunAsync(["update", "status"]));
        Assert(status.ExitCode == 0 && status.Output.Contains("recoveryRequired") && File.ReadAllBytes(stale.StatePath).SequenceEqual(statusBefore), "status observes abandoned updates without modifying state");
        var blocked = await Capture(() => CliApplication.RunAsync(["deploy", "--dry-run"]));
        Assert(blocked.ExitCode == ExitCodes.Busy && blocked.Output.Contains("TOOL_UPDATE_RECOVERY_REQUIRED"), "incomplete installations block new business commands");
    }

    private static UpdateInstallation Fixture(string root, string version)
    {
        var files = new List<string> { ".tools/RimDevTools.exe", ".tools/Data/tool-mods.manifest.json", ".tools/Ilspy/ilspycmd.exe" };
        foreach (var game in new[] { "1.6", "1.7" })
        foreach (var assembly in new[] { "LogSyncMod.dll", "AutoTestRunner.dll" }) files.Add($".tools/Data/LogSyncMod/{game}/Assemblies/{assembly}");
        foreach (var client in new[] { ".agents", ".claude" })
        {
            files.Add($"{client}/skills/rimdevtools/SKILL.md");
            files.Add($"{client}/skills/rimdevtools/references/commands-and-config.md");
        }
        files.Add(version == FutureVersion ? ".agents/skills/rimdevtools/new-managed.md" : ".agents/skills/rimdevtools/retired-managed.md");
        foreach (var file in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, file))!);
            File.WriteAllText(Path.Combine(root, file), "fixture " + file + (version == FutureVersion ? " newer" : ""));
        }
        var manifest = new ToolPackageManifest
        {
            SchemaVersion = 1, ToolVersion = version, Platform = "win-x64", RuntimeMajor = 10, SupportedGameVersions = ["1.6", "1.7"], ConfigVersion = 2,
            ConfigDefaults = new() { ["existing_project_mode"] = JsonSerializer.SerializeToElement(false) },
            Files = files.Select(path => new ToolPackageFile(path, new FileInfo(Path.Combine(root, path)).Length, ToolPackageManifest.Hash(Path.Combine(root, path)), "component", "replace")).ToList()
        };
        AtomicFile.WriteAllText(Path.Combine(root, ToolPackageManifest.RelativePath), JsonSerializer.Serialize(manifest, JsonDefaults.Storage));
        File.WriteAllText(Path.Combine(root, "mod_config.jsonc"), "{\"config_version\":2,\"game_version\":\"1.6\",\"existing_project_mode\":true,\"unknown\":null}");
        return new(root);
    }

    private static string CreateArchive(string root, string zip)
    {
        var manifest = ToolPackageManifest.Load(root);
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var path in manifest.Files.Select(f => f.Path).Append(ToolPackageManifest.RelativePath).Append("mod_config.jsonc")) archive.CreateEntryFromFile(Path.Combine(root, path), path);
        return zip;
    }

    private static ToolRelease Release(string zip) => new(FutureVersion, "RimDevTools-" + FutureVersion + "-Tools.zip", DownloadUrl, new FileInfo(zip).Length, ToolPackageManifest.Hash(zip), null);
    private static ToolUpdatePlan Prepare(UpdateInstallation installation, string zip, ToolRelease release)
    {
        var id = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(installation.Data, "tool-update-" + id);
        Directory.CreateDirectory(directory);
        File.Copy(zip, Path.Combine(directory, "package.zip"));
        return ToolUpdatePlan.Prepare(installation, release, id);
    }

    private static string Metadata(ToolRelease release, bool prerelease = false, bool draft = false, bool assets = true, string? digest = null, string? version = null)
        => JsonSerializer.Serialize(new { tag_name = "v" + (version ?? release.Version), prerelease, draft, assets = assets ? new[] { new { name = release.AssetName, size = release.Size, browser_download_url = release.DownloadUrl, digest = digest ?? "sha256:" + release.Sha256 } } : [] });
    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private static async Task<(int ExitCode, string Output)> Capture(Func<Task<int>> call)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try { return (await call(), writer.ToString()); }
        finally { Console.SetOut(original); }
    }
    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
        _passed++;
        Console.WriteLine("PASS: " + name);
    }
    private static void Throws(Action action, string name)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException) { Assert(true, name); return; }
        Assert(false, name);
    }
    private static async Task ThrowsAsync(Func<Task> action, string name)
    {
        try { await action(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or HttpRequestException or InvalidOperationException) { Assert(true, name); return; }
        Assert(false, name);
    }
}
