using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using RimDevTools;

if (args.Length == 2 && args[0] == "--abandon-lock")
{
    _ = EnvironmentLock.TryAcquire(args[1], "abandon", args[1], "abandoned", out _);
    Environment.Exit(99);
}

if (Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "").Equals("RimWorldWin64", StringComparison.OrdinalIgnoreCase))
{
    await FakeRimWorld.RunAsync(args);
    return;
}

var suite = new TestSuite();
await suite.RunAsync();

internal static class FakeRimWorld
{
    public static async Task RunAsync(string[] args)
    {
        Console.WriteLine("FAKE_GAME_STDOUT");
        var configDirectory = Environment.GetEnvironmentVariable("RIMDEVTOOLS_CONFIG_DIR")!;
        var mode = Environment.GetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE") ?? "quick";
        Directory.CreateDirectory(configDirectory);
        File.WriteAllText(Path.Combine(configDirectory, "fake-args.txt"), string.Join(" ", args));

        if (mode.StartsWith("data", StringComparison.Ordinal))
        {
            await RunDataExportAsync(configDirectory, mode);
            return;
        }

        var settingsPath = Path.Combine(configDirectory, "Mod_LogSyncMod_LogSyncMod.xml");
        var settings = XDocument.Load(settingsPath);
        File.WriteAllText(Path.Combine(configDirectory, "fake-settings.xml"), settings.ToString());
        var values = settings.Descendants("ModSettings").First().Elements().ToDictionary(x => x.Name.LocalName, x => x.Value);
        var playerLog = Path.Combine(Directory.GetParent(configDirectory)!.FullName, "Player.log");
        Directory.CreateDirectory(Path.GetDirectoryName(playerLog)!);

        if (mode == "manual")
        {
            await Task.Delay(200);
            return;
        }
        if (mode == "hang")
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            return;
        }
        if (mode == "modify")
        {
            File.WriteAllText(settingsPath, "game-modified");
            File.AppendAllText(playerLog, "[LogSyncMod] RUNTIME_READY_TO_CLOSE\n");
            await Task.Delay(TimeSpan.FromSeconds(30));
            return;
        }
        if (mode == "break-backup")
        {
            var transaction = Directory.EnumerateDirectories(Path.Combine(configDirectory, "RimDevToolsTransactions")).Single();
            var settingsBackup = Directory.EnumerateFiles(transaction, "*Mod_LogSyncMod_LogSyncMod.xml.original").Single();
            File.Delete(settingsBackup);
            File.WriteAllText(settingsPath, "game-modified");
            File.AppendAllText(playerLog, "[LogSyncMod] RUNTIME_READY_TO_CLOSE\n");
            await Task.Delay(TimeSpan.FromSeconds(30));
            return;
        }
        if (!values.TryGetValue("autoTestEnabled", out var enabled) || !enabled.Equals("True", StringComparison.OrdinalIgnoreCase))
        {
            File.AppendAllText(playerLog, "[LogSyncMod] RUNTIME_READY_TO_CLOSE\n");
            await Task.Delay(TimeSpan.FromSeconds(30));
            return;
        }

        var outputPath = values["autoTestOutputPath"];
        var packageId = values["autoTestTargetPackageId"];
        var runId = values["autoTestRunId"];
        Directory.CreateDirectory(outputPath);
        var status = mode == "auto-fail" ? "failed" : "passed";
        var result = new
        {
            packageId,
            runId,
            completeness = "Complete",
            manifestDiscovered = 1,
            manifestPlanned = 1,
            manifestExecuted = 1,
            manifestMissing = 0,
            results = new[] { new { caseId = "case-1", status } }
        };
        File.WriteAllText(Path.Combine(outputPath, $"{packageId}_20260806_120000_Result.json"), JsonSerializer.Serialize(result));
        File.AppendAllText(playerLog, $"[LogSyncAutoTest] COMPLETED runId={runId} packageId={packageId}\n");

        // The real AutoTest extension persists autoTestEnabled=false after completion.
        settings.Descendants("autoTestEnabled").First().Value = "False";
        File.WriteAllText(settingsPath, settings.ToString());
        await Task.Delay(TimeSpan.FromSeconds(30));
    }

    private static async Task RunDataExportAsync(string configDirectory, string mode)
    {
        if (mode == "data-hang")
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            return;
        }

        var requestPath = Path.Combine(configDirectory, "RimDevTools.ExportRequest.json");
        var request = JsonSerializer.Deserialize<DataExportRequest>(File.ReadAllText(requestPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var processingPath = Path.Combine(configDirectory, $"RimDevTools.ExportRequest.processing.{request.RunId}.json");
        File.Move(requestPath, processingPath);
        var resultPath = Path.Combine(configDirectory, $"RimDevTools.ExportResult.{request.RunId}.json");
        var playerLog = Path.Combine(Directory.GetParent(configDirectory)!.FullName, "Player.log");
        Directory.CreateDirectory(Path.GetDirectoryName(playerLog)!);
        if (mode == "data-fail")
        {
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new { schemaVersion = 1, runId = request.RunId, ok = false, errorCode = "FAKE_EXPORT_FAILED", errorMessage = "simulated failure" }));
            File.AppendAllText(playerLog, $"[RimSearcher] EXPORT_FAILED runId={request.RunId} code=FAKE_EXPORT_FAILED\n");
            return;
        }

        var rimSearcherPath = Environment.GetEnvironmentVariable("RIMDEVTOOLS_RIMSEARCHER_PATH")!;
        var exporterVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(rimSearcherPath).FileVersion!;
        var userVersion = ComponentLocator.EncodedVersion(exporterVersion)!.Value;
        var effectiveHash = mode == "data-bad-hash" ? "sha256:bad" : request.RequestedModsHash;
        WriteDatabase(request, exporterVersion, userVersion, effectiveHash);
        var result = new
        {
            schemaVersion = 1,
            runId = request.RunId,
            ok = true,
            databasePath = request.OutputPath,
            databaseBytes = new FileInfo(request.OutputPath).Length,
            exporterVersion,
            gameVersion = "1.6.4871",
            language = "ChineseSimplified",
            effectiveModsHash = request.RequestedModsHash,
            completedUtc = DateTimeOffset.UtcNow
        };
        File.WriteAllText(resultPath, JsonSerializer.Serialize(result));
        File.AppendAllText(playerLog, mode == "data-signal-mismatch"
            ? $"[RimSearcher] EXPORT_FAILED runId={request.RunId} code=WRONG_SIGNAL\n"
            : $"[RimSearcher] EXPORT_COMPLETE runId={request.RunId}\n");
        await Task.Delay(100);
    }

    private static void WriteDatabase(DataExportRequest request, string exporterVersion, int userVersion, string effectiveHash)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
        var temp = request.OutputPath + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);
        using (var connection = new SqliteConnection($"Data Source={temp};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $@"
                CREATE TABLE defs (id INTEGER PRIMARY KEY, def_name TEXT NOT NULL);
                CREATE TABLE export_metadata (
                    schema_version INTEGER NOT NULL, run_id TEXT NOT NULL, exporter_version TEXT NOT NULL,
                    sqlite_user_version INTEGER NOT NULL, game_version TEXT NOT NULL, language TEXT NOT NULL,
                    exported_utc TEXT NOT NULL, input_mods_hash TEXT NOT NULL, requested_mods_hash TEXT NOT NULL,
                    effective_mods_hash TEXT NOT NULL, project_package_id TEXT NOT NULL);
                CREATE TABLE loaded_mods (
                    load_order INTEGER NOT NULL, package_id TEXT NOT NULL, name TEXT NOT NULL, version TEXT NOT NULL,
                    root_path TEXT NOT NULL, is_official INTEGER NOT NULL, is_tooling INTEGER NOT NULL);
                INSERT INTO export_metadata VALUES (1, $runId, $exporterVersion, $userVersion, '1.6.4871', 'ChineseSimplified', $utc, $inputHash, $requestedHash, $effectiveHash, $projectPackageId);
                INSERT INTO loaded_mods VALUES (0, $projectPackageId, 'Current', '1.0', 'C:\\Current', 0, 0);
                INSERT INTO loaded_mods VALUES (1, 'kearril.rimsearcherdatamod', 'RimSearcher', $exporterVersion, 'C:\\RimSearcher', 0, 1);
                PRAGMA user_version = {userVersion};";
            command.Parameters.AddWithValue("$runId", request.RunId);
            command.Parameters.AddWithValue("$exporterVersion", exporterVersion);
            command.Parameters.AddWithValue("$userVersion", userVersion);
            command.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$inputHash", request.InputModsHash);
            command.Parameters.AddWithValue("$requestedHash", request.RequestedModsHash);
            command.Parameters.AddWithValue("$effectiveHash", effectiveHash);
            command.Parameters.AddWithValue("$projectPackageId", request.ProjectPackageId);
            command.ExecuteNonQuery();
        }
        File.Move(temp, request.OutputPath, true);
    }
}

internal sealed class TestSuite
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "RimDevToolsTests", Guid.NewGuid().ToString("N"));
    private int _passed;

    public async Task RunAsync()
    {
        Directory.CreateDirectory(_temp);
        try
        {
            TestJsoncAndDiscovery();
            TestModListParser();
            await TestSettingsAsync();
            await TestTransactionsAsync();
            await TestEnvironmentLockAsync();
            await TestRefAsync();
            await TestDeployAsync();
            TestAutoResultValidator();
            TestLogArchive();
            await TestDoctorIsReadOnlyAsync();
            await TestRunPreflightAsync();
            await TestGameWorkflowsAsync();
            await TestDataWorkflowsAsync();
            Console.WriteLine($"PASS {_passed} tests");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE", null);
            if (Directory.Exists(_temp)) Directory.Delete(_temp, true);
        }
    }

    private void TestJsoncAndDiscovery()
    {
        var root = NewDirectory("discovery-project");
        File.WriteAllText(Path.Combine(root, "mod_config.jsonc"), "{\n // comment\n \"config_version\": 2, \"mod_name\": \"Demo//Name\", \"rimworld_dir\": \"C:\\\\Game\",\n}");
        var context = ProjectDiscovery.Find(root, out var error);
        Assert(context is not null && error is null && context.Config.ModName == "Demo//Name", "JSONC preserves // inside strings");
        var child = NewDirectory(Path.Combine("discovery-project", "source"));
        Assert(ProjectDiscovery.Find(child, out _)?.Root == root, "project discovery checks direct parent");
        var grandchild = NewDirectory(Path.Combine("discovery-project", "source", "Core"));
        Assert(ProjectDiscovery.Find(grandchild, out var missing) is null && missing?.Code == "PROJECT_NOT_FOUND", "project discovery stops after direct parent");
    }

    private void TestModListParser()
    {
        var environment = CreateProjectEnvironment("mod-list");
        CreateMod(environment.Game, "Helper", "demo.helper");
        File.WriteAllText(environment.ConfigPath, Config(environment, minMods: "[\"demo.helper\"]"));
        var modsFile = Path.Combine(environment.Root, "mods.xml");
        File.WriteAllText(modsFile, "<ModsConfigData><ignored><li>not.allowed</li></ignored><activeMods><li>demo.helper</li><li>DEMO.HELPER</li></activeMods></ModsConfigData>");
        var context = ProjectDiscovery.Find(environment.Root, out _)!;
        var resolution = ModListParser.Resolve(context, modsFile, out var error);
        Assert(error is null && resolution!.Mods.SequenceEqual(["demo.helper", "demo.current", "trigger.logsyncmod"], StringComparer.OrdinalIgnoreCase), "ModList reads direct activeMods entries and deduplicates");
        Assert(resolution!.Additions.SequenceEqual([new ModListAddition("demo.current", 1, "current_project"), new ModListAddition("trigger.logsyncmod", 2, "logsync_tooling")]), "ModList reports automatic additions and positions");
    }

    private async Task TestSettingsAsync()
    {
        var environment = CreateProjectEnvironment("settings");
        File.WriteAllText(environment.ConfigPath, Config(environment, extra: "\"log_keywords\":[\"Ready\"]"));
        using var scope = UseEnvironment(environment);
        var context = ProjectDiscovery.Find(environment.Root, out _)!;
        var changed = await SettingsEditor.UpdateAsync(context, "auto", runId: "settings-run");
        var document = XDocument.Load(environment.SettingsPath);
        var settings = document.Descendants("ModSettings").First();
        Assert(changed && settings.Element("autoTestTargetPackageId")?.Value == "demo.current", "auto settings include current packageId");
        Assert(settings.Element("autoTestRunId")?.Value == "settings-run", "auto settings bind the requested runId");
        Assert(settings.Element("autoTestOutputPath")?.Value == SettingsEditor.AutoTestOutputPath(context) && settings.Element("autoTestEnabled")?.Value == "True", "auto settings include output path and enabled state");
        Assert(settings.Element("keywords")?.Elements("li").Single().Value == "Ready", "settings writer updates keywords");
    }

    private async Task TestTransactionsAsync()
    {
        var directory = NewDirectory("transactions");
        var first = Path.Combine(directory, "first.xml");
        var second = Path.Combine(directory, "second.xml");
        File.WriteAllText(first, "original");

        var transaction = await ConfigTransaction.CreateAsync(directory, "recover", [first, second]);
        await transaction.WriteAllTextAsync(first, "tool-value");
        await transaction.WriteAllTextAsync(second, "created");
        var recovered = await TransactionRecovery.RecoverAsync(directory);
        Assert(recovered.Ok && File.ReadAllText(first) == "original" && !File.Exists(second), "crash recovery restores existing and nonexistent files");

        var conflict = await ConfigTransaction.CreateAsync(directory, "conflict", [first]);
        await conflict.WriteAllTextAsync(first, "tool-value");
        File.WriteAllText(first, "user-value");
        var refused = await TransactionRecovery.RecoverAsync(directory);
        Assert(!refused.Ok && refused.Error?.Code == "TRANSACTION_CONFLICT" && File.ReadAllText(first) == "user-value", "recovery refuses user-modified configuration");

        await conflict.RestoreAsync(force: true);
        Assert(File.ReadAllText(first) == "original", "owned transaction can force exact restoration after game writes");

        var creating = Path.Combine(directory, "RimDevToolsTransactions", ".creating-dead");
        Directory.CreateDirectory(creating);
        File.WriteAllText(Path.Combine(creating, "partial"), "incomplete");
        var cleaned = await TransactionRecovery.RecoverAsync(directory);
        Assert(cleaned.Ok && !Directory.Exists(creating), "recovery removes transaction creation directories that never became active");
    }

    private async Task TestEnvironmentLockAsync()
    {
        var directory = NewDirectory("lock");
        using (var first = EnvironmentLock.TryAcquire(directory, "first", _temp, "one", out _))
        {
            Assert(first is not null, "first environment lock is acquired");
            Assert(EnvironmentLock.IsBusy(directory), "lock diagnostics probe the named mutex");
            await Task.Delay(100);
            using var second = EnvironmentLock.TryAcquire(directory, "second", _temp, "two", out _);
            Assert(second is null, "environment lock remains exclusive across await");
        }
        using (var third = EnvironmentLock.TryAcquire(directory, "third", _temp, "three", out _))
            Assert(third is not null, "environment lock releases cleanly");
        Assert(!EnvironmentLock.IsBusy(directory), "named mutex probe reports released environment");

        var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "RimDevTools.Tests.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "--abandon-lock", directory }
        })!;
        await child.WaitForExitAsync();
        child.Dispose();
        using var afterCrash = EnvironmentLock.TryAcquire(directory, "recovery", _temp, "after-crash", out _);
        Assert(afterCrash is not null, "abandoned named mutex is acquired by the next writer");
    }

    private async Task TestRefAsync()
    {
        AssertThrows<InvalidDataException>(() => RefCommand.SafeFolderName(".."), "unsafe packageId is rejected");
        Assert(RefCommand.SafeFolderName("CON") == "_CON", "Windows reserved packageId is sanitized");

        var environment = CreateProjectEnvironment("ref");
        var workshop = NewDirectory(Path.Combine("ref", "workshop"));
        File.WriteAllText(environment.ConfigPath, Config(environment, extra: $"\"workshop_paths\":[\"{Escape(workshop)}\"]"));
        CreateMod(environment.Game, "DuplicateOne", "demo.duplicate");
        CreateModAt(workshop, "DuplicateTwo", "demo.duplicate");
        using var scope = UseEnvironment(environment);
        var ambiguous = await CaptureAsync(() => RefCommand.DepsAsync(["ref", "deps", "demo.duplicate"]));
        Assert(ambiguous.ExitCode == ExitCodes.InvalidArguments && JsonErrorCode(ambiguous.Output) == "MOD_AMBIGUOUS", "same-priority duplicate candidates fail explicitly");
        var reorderedSearch = await CaptureAsync(() => RefCommand.SearchAsync(["ref", "search", "--fuzzy", "Duplicate"]));
        Assert(reorderedSearch.ExitCode == 0, "ref query parsing accepts flags before the query");

        var cycleA = CreateMod(environment.Game, "CycleA", "demo.a", "demo.b");
        CreateMod(environment.Game, "CycleB", "demo.b", "demo.a");
        var cycle = await CaptureAsync(() => RefCommand.PullAsync(["ref", "pull", "demo.a", "--deps"]));
        Assert(cycle.ExitCode == ExitCodes.InvalidArguments && JsonErrorCode(cycle.Output) == "DEPENDENCY_CYCLE", "dependency cycles fail before writing targets");
        Assert(!Directory.Exists(Path.Combine(environment.Root, "参考文档", "demo.a")) && Directory.Exists(cycleA), "cycle failure leaves source and targets untouched");

        var versioned = CreateMod(environment.Game, "Versioned", "demo.versioned");
        File.WriteAllText(Path.Combine(versioned, "About", "About.xml"), "<ModMetaData><name>Versioned</name><packageId>demo.versioned</packageId><modDependenciesByVersion><v1.5><li><packageId>demo.old</packageId></li></v1.5><v1.6><li><packageId>demo.new</packageId></li></v1.6></modDependenciesByVersion></ModMetaData>");
        var versionResult = await CaptureAsync(() => RefCommand.DepsAsync(["ref", "deps", "demo.versioned", "--version", "1.5"]));
        Assert(versionResult.ExitCode == 0 && versionResult.Output.Contains("demo.old") && !versionResult.Output.Contains("demo.new"), "ref --version selects version-specific dependencies");

        CreateMod(environment.Game, "CollisionSlash", "demo/x");
        CreateMod(environment.Game, "CollisionBackslash", "demo\\x");
        var collisionRoot = CreateMod(environment.Game, "CollisionRoot", "demo.collision-root");
        File.WriteAllText(Path.Combine(collisionRoot, "About", "About.xml"), "<ModMetaData><name>Root</name><packageId>demo.collision-root</packageId><modDependencies><li><packageId>demo/x</packageId></li><li><packageId>demo\\x</packageId></li></modDependencies></ModMetaData>");
        var collision = await CaptureAsync(() => RefCommand.PullAsync(["ref", "pull", "demo.collision-root", "--deps"]));
        Assert(collision.ExitCode == ExitCodes.InvalidArguments && JsonErrorCode(collision.Output) == "TARGET_COLLISION", "ref pull rejects packageIds that map to the same safe target");

        CreateMod(environment.Game, "Reference", "demo.reference", content: "v1");
        var pulled = await CaptureAsync(() => RefCommand.PullAsync(["ref", "pull", "demo.reference"]));
        Assert(pulled.ExitCode == 0 && File.ReadAllText(Path.Combine(environment.Root, "参考文档", "demo.reference", "content.txt")) == "v1", "ref pull installs staged mod");

        var unsafeEnvironment = CreateProjectEnvironment("ref-reparse");
        CreateMod(unsafeEnvironment.Game, "Reference", "demo.reference", content: "safe");
        var referenceRoot = Path.Combine(unsafeEnvironment.Root, "参考文档");
        var externalTarget = NewDirectory(Path.Combine("ref-reparse", "external-target"));
        File.WriteAllText(Path.Combine(externalTarget, "sentinel.txt"), "outside");
        var junction = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "/c", "mklink", "/J", referenceRoot, externalTarget }
        })!;
        await junction.WaitForExitAsync();
        var junctionCreated = junction.ExitCode == 0;
        junction.Dispose();
        Assert(junctionCreated, "test fixture creates reference target junction");
        try
        {
            using var unsafeScope = UseEnvironment(unsafeEnvironment);
            var unsafePull = await CaptureAsync(() => RefCommand.PullAsync(["ref", "pull", "demo.reference"]));
            Assert(unsafePull.ExitCode == ExitCodes.InvalidArguments && JsonErrorCode(unsafePull.Output) == "REFERENCE_TARGET_UNSAFE", "ref pull rejects reparse points in the target path");
            Assert(File.ReadAllText(Path.Combine(externalTarget, "sentinel.txt")) == "outside", "unsafe ref target remains untouched");
        }
        finally
        {
            if (Directory.Exists(referenceRoot)) Directory.Delete(referenceRoot);
        }

        var batch = NewDirectory(Path.Combine("ref", "batch"));
        var target1 = NewDirectory(Path.Combine("ref", "batch", "target1"));
        var target2 = NewDirectory(Path.Combine("ref", "batch", "target2"));
        File.WriteAllText(Path.Combine(target1, "old.txt"), "one");
        File.WriteAllText(Path.Combine(target2, "old.txt"), "two");
        var staging1 = NewDirectory(Path.Combine("ref", "batch", "staging1"));
        File.WriteAllText(Path.Combine(staging1, "new.txt"), "new");
        var candidate = new ModCandidate("x", "x", "x", batch, false, null, []);
        var targets = new[]
        {
            new RefCommand.PullTarget(candidate, target1, staging1, Path.Combine(batch, "backup1")),
            new RefCommand.PullTarget(candidate, target2, Path.Combine(batch, "missing-staging"), Path.Combine(batch, "backup2"))
        };
        AssertThrows<DirectoryNotFoundException>(() => RefCommand.InstallBatch(targets), "batch install surfaces replacement failure");
        Assert(File.Exists(Path.Combine(target1, "old.txt")) && File.Exists(Path.Combine(target2, "old.txt")), "batch install rolls back all replaced targets");
    }

    private async Task TestDeployAsync()
    {
        var environment = CreateProjectEnvironment("deploy");
        var target = NewDirectory(Path.Combine("deploy", "game", "Mods", "Demo"));
        File.WriteAllText(Path.Combine(target, "PublishedFileId.txt"), "123");
        File.WriteAllText(Path.Combine(target, "stale.txt"), "stale");
        File.WriteAllText(Path.Combine(environment.Root, "About", "About.html"), "filtered");
        File.WriteAllText(Path.Combine(environment.Root, "About", "About.txt"), "kept");
        File.WriteAllText(environment.ConfigPath, Config(environment, modName: "Demo", extra: "\"sync_items\":[\"About\"],\"protected_files_regex\":\"^(PublishedFileId\\\\.txt)$\""));
        using var scope = UseEnvironment(environment);

        var dryRun = await DeployCommand.ExecuteAsync(["deploy", "--dry-run"]);
        var dryResult = (DeployResult)dryRun.Result!;
        Assert(dryRun.Ok && dryResult.Plan.TargetActions.Any(x => x.Action == "protect") && dryResult.Plan.TargetActions.Any(x => x.Action == "replace"), "deploy dry-run reports protected and replaced targets");

        var unknownArgument = await CaptureAsync(() => CliApplication.RunAsync(["deploy", "--dry-run", "--definitely-unknown"]));
        Assert(unknownArgument.ExitCode == ExitCodes.InvalidArguments && JsonErrorCode(unknownArgument.Output) == "INVALID_ARGUMENT", "unknown deploy options fail before execution");

        var pending = await ConfigTransaction.CreateAsync(environment.ConfigDirectory, "before-deploy", [environment.SettingsPath]);
        await pending.WriteAllTextAsync(environment.SettingsPath, "temporary");
        var deployed = await DeployCommand.ExecuteAsync(["deploy"]);
        Assert(deployed.Ok && ((DeployResult)deployed.Result!).RecoveredTransaction && File.ReadAllText(environment.SettingsPath) == environment.OriginalSettings, "deploy restores interrupted configuration transaction before work");
        Assert(File.Exists(Path.Combine(target, "PublishedFileId.txt")) && !File.Exists(Path.Combine(target, "stale.txt")), "deploy preserves protected items and replaces stale target");
        Assert(File.Exists(Path.Combine(target, "About", "About.txt")) && !File.Exists(Path.Combine(target, "About", "About.html")), "deploy filters About html and svg files");

        File.WriteAllText(environment.ConfigPath, Config(environment, modName: "Demo", extra: "\"sync_items\":[\"..\\\\outside\"]"));
        var traversal = await DeployCommand.ExecuteAsync(["deploy", "--dry-run"]);
        Assert(!traversal.Ok && traversal.Error?.Code == "SYNC_PATH_INVALID", "deploy rejects sync path traversal");

        File.WriteAllText(environment.ConfigPath, Config(environment, modName: "Demo", extra: "\"sync_items\":[\"About\",\"RequiredMissing\"]"));
        var missingRequired = await DeployCommand.ExecuteAsync(["deploy"]);
        Assert(!missingRequired.Ok && missingRequired.Error?.Code == "SYNC_ITEM_NOT_FOUND" && File.Exists(Path.Combine(target, "PublishedFileId.txt")), "missing required sync items fail before replacing deployment");

        var invalidProject = Path.Combine(environment.Root, "bad.csproj");
        File.WriteAllText(invalidProject, "not a project");
        File.WriteAllText(Path.Combine(target, "still-here.txt"), "old");
        File.WriteAllText(environment.ConfigPath, Config(environment, modName: "Demo", extra: "\"sync_items\":[\"About\"],\"csproj_paths\":[\"bad.csproj\"]"));
        var failedBuild = await DeployCommand.ExecuteAsync(["deploy"]);
        Assert(!failedBuild.Ok && failedBuild.Error?.Code == "BUILD_FAILED" && File.Exists(Path.Combine(target, "still-here.txt")), "build failure preserves deployed directory");
    }

    private void TestAutoResultValidator()
    {
        var output = NewDirectory("results");
        var snapshot = AutoResultValidator.Snapshot(output);
        WriteResult(output, "demo.current", "run-one", "failed");
        var failed = AutoResultValidator.Validate(output, "demo.current", "run-one", snapshot);
        Assert(!failed.Ok && failed.Code == "AUTO_RESULT_FAILED", "auto result validator rejects failed cases");

        var freshSnapshot = AutoResultValidator.Snapshot(output);
        WriteResult(output, "demo.current", "run-two", "passed", "second_Result.json");
        var passed = AutoResultValidator.Validate(output, "demo.current", "run-two", freshSnapshot);
        Assert(passed.Ok, "auto result validator accepts complete passing run");
        var stale = AutoResultValidator.Validate(output, "demo.current", "run-three", AutoResultValidator.Snapshot(output));
        Assert(!stale.Ok && stale.Code == "AUTO_RESULT_MISSING", "old result files cannot satisfy a new run");

        var mismatchedSnapshot = AutoResultValidator.Snapshot(output);
        WriteResult(output, "demo.current", "old-run", "passed", "mismatch_Result.json");
        var mismatched = AutoResultValidator.Validate(output, "demo.current", "new-run", mismatchedSnapshot);
        Assert(!mismatched.Ok && mismatched.Code == "AUTO_RESULT_MISMATCH", "modified results with a stale runId cannot satisfy a new run");
    }

    private void TestLogArchive()
    {
        var directory = NewDirectory("logs");
        var log = Path.Combine(directory, "精选日志.log");
        for (var index = 0; index < 4; index++)
        {
            File.WriteAllText(log, $"run-{index}");
            LogArchive.Rotate(log, 3);
        }
        Assert(Directory.EnumerateFiles(directory, "精选日志*.log").Count() == 3 && File.ReadAllText(log) == "", "log rotation enforces retention and clears current log");
    }

    private async Task TestDoctorIsReadOnlyAsync()
    {
        var environment = CreateProjectEnvironment("doctor", createConfigDirectory: false);
        File.WriteAllText(environment.ConfigPath, Config(environment));
        using var scope = UseEnvironment(environment);
        var result = await CaptureAsync(() => DoctorCommand.RunAsync(["doctor"]));
        Assert(result.ExitCode == 0 && !Directory.Exists(environment.ConfigDirectory), "doctor does not create RimWorld configuration directory");
        Assert(result.Output.Contains("environment.transactions") && result.Output.Contains("deploy.dotnet"), "doctor reports transaction and tool capabilities");
    }

    private async Task TestRunPreflightAsync()
    {
        var environment = CreateProjectEnvironment("preflight", createGameExecutable: false);
        using var scope = UseEnvironment(environment);
        var result = await CaptureAsync(() => TestCommand.RunAsync(["test", "run", "--mode", "auto", "--timeout-seconds", "1"]));
        Assert(result.ExitCode == ExitCodes.MissingDependency && JsonErrorCode(result.Output) == "GAME_NOT_FOUND", "test run rejects missing game before mutation");
        Assert(File.ReadAllText(environment.ModsConfigPath) == environment.OriginalMods && File.ReadAllText(environment.SettingsPath) == environment.OriginalSettings, "preflight failure preserves global configuration");
    }

    private async Task TestGameWorkflowsAsync()
    {
        var environment = CreateProjectEnvironment("game-workflows", createGameExecutable: true);
        using var scope = UseEnvironment(environment);

        Environment.SetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE", "quick");
        var quick = await CaptureAsync(() => TestCommand.RunAsync(["test", "run", "--mode", "quick", "--timeout-seconds", "5"]));
        Assert(quick.ExitCode == 0 && !File.ReadAllText(Path.Combine(environment.ConfigDirectory, "fake-args.txt")).Contains("quicktest"), "quick mode waits for ready signal without quicktest argument");
        AssertConfigurationRestored(environment, "quick mode restores exact configuration");

        Environment.SetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE", "auto");
        var auto = await CaptureAsync(() => TestCommand.RunAsync(["test", "run", "--mode", "auto", "--timeout-seconds", "5"]));
        var observedSettings = File.ReadAllText(Path.Combine(environment.ConfigDirectory, "fake-settings.xml"));
        Assert(auto.ExitCode == 0 && File.ReadAllText(Path.Combine(environment.ConfigDirectory, "fake-args.txt")).Contains("quicktest"), "auto mode launches with quicktest and validates result");
        Assert(observedSettings.Contains("demo.current") && observedSettings.Contains("AutoTestOutput"), "auto mode passes target package and output path to LogSync");
        AssertConfigurationRestored(environment, "auto mode restores settings changed by the game");

        Environment.SetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE", "auto-fail");
        var failed = await CaptureAsync(() => TestCommand.RunAsync(["test", "run", "--mode", "xml", "--timeout-seconds", "5"]));
        Assert(failed.ExitCode == ExitCodes.GameFailed && JsonErrorCode(failed.Output) == "AUTO_RESULT_FAILED", "xml mode rejects failed auto-test JSON");
        AssertConfigurationRestored(environment, "failed auto test restores configuration");

        Environment.SetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE", "manual");
        var manual = await CaptureAsync(() => TestCommand.RunAsync(["test", "run", "--mode", "manual", "--timeout-seconds", "5"]));
        Assert(manual.ExitCode == 0, "manual mode succeeds after user process exits");

        Environment.SetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE", "hang");
        var timeout = await CaptureAsync(() => TestCommand.RunAsync(["test", "run", "--mode", "manual", "--timeout-seconds", "1"]));
        Assert(timeout.ExitCode == ExitCodes.Timeout && JsonErrorCode(timeout.Output) == "TEST_TIMEOUT", "manual mode honors timeout");
        AssertConfigurationRestored(environment, "manual timeout restores configuration");

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var cancelled = await CaptureAsync(() => TestCommand.RunAsync(["test", "run", "--mode", "quick", "--timeout-seconds", "10"], cancellation.Token));
        Assert(cancelled.ExitCode == ExitCodes.Cancelled && JsonErrorCode(cancelled.Output) == "CANCELLED", "cancellation enters unified cleanup path");
        AssertConfigurationRestored(environment, "cancellation restores configuration");

        Environment.SetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE", "modify");
        var oneJson = await CaptureAsync(() => TestCommand.RunAsync(["test", "run", "--mode", "quick", "--timeout-seconds", "5"]));
        using var parsed = JsonDocument.Parse(oneJson.Output);
        Assert(oneJson.ExitCode == 0 && parsed.RootElement.GetProperty("ok").GetBoolean(), "game-side setting writes still produce one valid JSON result after restoration");

        Environment.SetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE", "break-backup");
        var restoreFailure = await CaptureAsync(() => TestCommand.RunAsync(["test", "run", "--mode", "quick", "--timeout-seconds", "5"]));
        using var failureJson = JsonDocument.Parse(restoreFailure.Output);
        Assert(restoreFailure.ExitCode == ExitCodes.TaskFailed && JsonErrorCode(restoreFailure.Output) == "TRANSACTION_RESTORE_FAILED", "restore failure returns a stable task error");
        Assert(failureJson.RootElement.GetProperty("ok").ValueKind == JsonValueKind.False && TransactionRecovery.PendingDirectories(environment.ConfigDirectory).Count == 1, "restore failure emits one JSON object and retains the transaction");
    }

    private async Task TestDataWorkflowsAsync()
    {
        var environment = CreateProjectEnvironment("data-workflows", createGameExecutable: true);
        CreateMod(environment.Game, "RimSearcherDataMod", "kearril.rimsearcherdatamod");
        File.WriteAllText(environment.ConfigPath, Config(environment, extra: "\"rimsearcher_db_path\":\".rimdev\\\\defs.db\""));
        using var scope = UseEnvironment(environment);
        Environment.SetEnvironmentVariable("RIMDEVTOOLS_RIMSEARCHER_PATH", Path.Combine(AppContext.BaseDirectory, "RimDevTools.Tests.exe"));

        Environment.SetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE", "data-success");
        var refresh = await CaptureAsync(() => DataCommand.RefreshAsync(["data", "refresh", "--timeout-seconds", "5"]));
        Assert(refresh.ExitCode == 0, "data refresh accepts matching result, log, database version and metadata");
        AssertConfigurationRestored(environment, "data refresh restores exact ModsConfig and leaves LogSync settings unchanged");
        var databasePath = Path.Combine(environment.Root, ".rimdev", "defs.db");
        var inspected = DatabaseInspector.Inspect(databasePath, "test");
        Assert(inspected.Integrity == "ok" && inspected.VersionMatches && inspected.Metadata?.RunId is not null && inspected.LoadedMods.Count == 2, "database inspector validates metadata and ordered loaded mods");
        Assert(
            !File.Exists(Path.Combine(environment.ConfigDirectory, "RimDevTools.ExportRequest.json"))
            && !Directory.EnumerateFiles(environment.ConfigDirectory, "RimDevTools.ExportRequest.processing.*.json").Any()
            && TransactionRecovery.PendingDirectories(environment.ConfigDirectory).Count == 0,
            "data refresh cleans request and transaction state");

        var status = await CaptureAsync(() => DataCommand.StatusAsync(["data", "status"]));
        Assert(status.ExitCode == 0 && status.Output.Contains("\"refreshRecommended\": false"), "data status reports a current valid database");

        File.WriteAllText(Path.Combine(environment.ConfigDirectory, "RimDevTools.ExportRequest.json"), "{}");
        var orphanedRequest = await CaptureAsync(() => DataCommand.RefreshAsync(["data", "refresh", "--timeout-seconds", "5"]));
        Assert(orphanedRequest.ExitCode == ExitCodes.TaskFailed && JsonErrorCode(orphanedRequest.Output) == "EXPORT_REQUEST_CONFLICT", "data refresh refuses orphaned request files");
        File.Delete(Path.Combine(environment.ConfigDirectory, "RimDevTools.ExportRequest.json"));

        Environment.SetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE", "data-signal-mismatch");
        var signalMismatch = await CaptureAsync(() => DataCommand.RefreshAsync(["data", "refresh", "--timeout-seconds", "5"]));
        Assert(signalMismatch.ExitCode == ExitCodes.GameFailed && JsonErrorCode(signalMismatch.Output) == "EXPORT_SIGNAL_MISMATCH", "data refresh requires result and dedicated log to agree");
        AssertConfigurationRestored(environment, "signal mismatch restores configuration");

        Environment.SetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE", "data-bad-hash");
        var badHash = await CaptureAsync(() => DataCommand.RefreshAsync(["data", "refresh", "--timeout-seconds", "5"]));
        Assert(badHash.ExitCode == ExitCodes.GameFailed && JsonErrorCode(badHash.Output) == "EFFECTIVE_MODS_HASH_MISMATCH", "data refresh rejects inconsistent effective mod metadata");
        AssertConfigurationRestored(environment, "metadata validation failure restores configuration");

        Environment.SetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE", "data-fail");
        var failed = await CaptureAsync(() => DataCommand.RefreshAsync(["data", "refresh", "--timeout-seconds", "5"]));
        Assert(failed.ExitCode == ExitCodes.GameFailed && JsonErrorCode(failed.Output) == "FAKE_EXPORT_FAILED", "data refresh surfaces DataMod failure code");
        AssertConfigurationRestored(environment, "DataMod failure restores configuration");

        Environment.SetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE", "data-hang");
        var timeout = await CaptureAsync(() => DataCommand.RefreshAsync(["data", "refresh", "--timeout-seconds", "1"]));
        Assert(timeout.ExitCode == ExitCodes.Timeout && JsonErrorCode(timeout.Output) == "DATA_REFRESH_TIMEOUT", "data refresh enforces timeout");
        AssertConfigurationRestored(environment, "data timeout restores configuration");
    }

    private ProjectEnvironment CreateProjectEnvironment(string name, bool createConfigDirectory = true, bool createGameExecutable = false)
    {
        var root = NewDirectory(name);
        var game = NewDirectory(Path.Combine(name, "game"));
        var config = Path.Combine(_temp, name, "Config");
        if (createConfigDirectory) Directory.CreateDirectory(config);
        Directory.CreateDirectory(Path.Combine(root, "About"));
        File.WriteAllText(Path.Combine(root, "About", "About.xml"), "<ModMetaData><name>Current</name><packageId>demo.current</packageId></ModMetaData>");
        CreateMod(game, "LogSync", "trigger.logsyncmod");

        var settings = Path.Combine(config, "Mod_LogSyncMod_LogSyncMod.xml");
        var mods = Path.Combine(config, "ModsConfig.xml");
        const string originalSettings = "<Root><ModSettings><logPath>old</logPath><keywords><li>Old</li></keywords><autoTestEnabled>False</autoTestEnabled></ModSettings></Root>";
        const string originalMods = "<ModsConfigData><activeMods><li>ludeon.rimworld</li></activeMods></ModsConfigData>";
        if (createConfigDirectory)
        {
            File.WriteAllText(settings, originalSettings);
            File.WriteAllText(mods, originalMods);
        }

        var environment = new ProjectEnvironment(root, game, config, Path.Combine(root, "mod_config.jsonc"), settings, mods, originalSettings, originalMods);
        File.WriteAllText(environment.ConfigPath, Config(environment));
        if (createGameExecutable) InstallFakeGame(environment.Game);
        return environment;
    }

    private void InstallFakeGame(string gameDirectory)
    {
        var output = AppContext.BaseDirectory;
        File.Copy(Path.Combine(output, "RimDevTools.Tests.exe"), Path.Combine(gameDirectory, "RimWorldWin64.exe"), true);
        foreach (var file in Directory.EnumerateFiles(output))
        {
            var destination = Path.Combine(gameDirectory, Path.GetFileName(file));
            if (!Path.GetFullPath(file).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) File.Copy(file, destination, true);
        }
        var windowsRuntime = Path.Combine(output, "runtimes", "win-x64");
        if (Directory.Exists(windowsRuntime)) CopyTestDirectory(windowsRuntime, Path.Combine(gameDirectory, "runtimes", "win-x64"));
    }

    private static void CopyTestDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var directory in Directory.EnumerateDirectories(source)) CopyTestDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private string Config(ProjectEnvironment environment, string modName = "Demo", string minMods = "[]", string? extra = null)
    {
        var members = new List<string>
        {
            "\"config_version\":2",
            $"\"mod_name\":\"{modName}\"",
            "\"game_version\":\"1.6\"",
            $"\"rimworld_dir\":\"{Escape(environment.Game)}\"",
            $"\"min_active_mods\":{minMods}",
            "\"timeouts\":{\"existing_game_exit\":2,\"post_exit_delay\":0,\"max_wait\":5}",
            "\"retention\":{\"max_log_files\":3,\"max_auto_test_runs\":2}"
        };
        if (!string.IsNullOrWhiteSpace(extra)) members.Add(extra);
        return "{" + string.Join(",", members) + "}";
    }

    private static string CreateMod(string gameDirectory, string folder, string packageId, string? dependency = null, string? content = null)
        => CreateModAt(Path.Combine(gameDirectory, "Mods"), folder, packageId, dependency, content);

    private static string CreateModAt(string root, string folder, string packageId, string? dependency = null, string? content = null)
    {
        var directory = Path.Combine(root, folder);
        Directory.CreateDirectory(Path.Combine(directory, "About"));
        var dependencies = dependency is null ? "" : $"<modDependencies><li><packageId>{dependency}</packageId></li></modDependencies>";
        File.WriteAllText(Path.Combine(directory, "About", "About.xml"), $"<ModMetaData><name>{folder}</name><packageId>{packageId}</packageId>{dependencies}</ModMetaData>");
        if (content is not null) File.WriteAllText(Path.Combine(directory, "content.txt"), content);
        return directory;
    }

    private static void WriteResult(string output, string packageId, string runId, string status, string name = "first_Result.json")
    {
        var value = new { packageId, runId, completeness = "Complete", manifestPlanned = 1, manifestExecuted = 1, manifestMissing = 0, results = new[] { new { caseId = "case", status } } };
        File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(value));
    }

    private IDisposable UseEnvironment(ProjectEnvironment environment)
    {
        var oldDirectory = Environment.CurrentDirectory;
        var oldConfig = Environment.GetEnvironmentVariable("RIMDEVTOOLS_CONFIG_DIR");
        var oldRimSearcher = Environment.GetEnvironmentVariable("RIMDEVTOOLS_RIMSEARCHER_PATH");
        Environment.CurrentDirectory = environment.Root;
        Environment.SetEnvironmentVariable("RIMDEVTOOLS_CONFIG_DIR", environment.ConfigDirectory);
        return new ActionScope(() =>
        {
            Environment.CurrentDirectory = oldDirectory;
            Environment.SetEnvironmentVariable("RIMDEVTOOLS_CONFIG_DIR", oldConfig);
            Environment.SetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE", null);
            Environment.SetEnvironmentVariable("RIMDEVTOOLS_RIMSEARCHER_PATH", oldRimSearcher);
        });
    }

    private string NewDirectory(string relative)
    {
        var path = Path.IsPathRooted(relative) ? relative : Path.Combine(_temp, relative);
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<CapturedResult> CaptureAsync(Func<Task<int>> action)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try { return new CapturedResult(await action(), writer.ToString().Trim()); }
        finally { Console.SetOut(original); }
    }

    private static string? JsonErrorCode(string output)
    {
        using var document = JsonDocument.Parse(output);
        return document.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private void AssertConfigurationRestored(ProjectEnvironment environment, string name)
        => Assert(File.ReadAllText(environment.ModsConfigPath) == environment.OriginalMods && File.ReadAllText(environment.SettingsPath) == environment.OriginalSettings, name);

    private void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"FAIL: {name}");
        _passed++;
        Console.WriteLine($"PASS: {name}");
    }

    private void AssertThrows<T>(Action action, string name) where T : Exception
    {
        try { action(); }
        catch (T) { Assert(true, name); return; }
        Assert(false, name);
    }

    private static string Escape(string path) => path.Replace("\\", "\\\\");

    private sealed record CapturedResult(int ExitCode, string Output);
    private sealed record ProjectEnvironment(string Root, string Game, string ConfigDirectory, string ConfigPath, string SettingsPath, string ModsConfigPath, string OriginalSettings, string OriginalMods);
    private sealed class ActionScope(Action action) : IDisposable { public void Dispose() => action(); }
}
