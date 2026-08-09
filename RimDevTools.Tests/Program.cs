using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using System.Runtime.Versioning;
using RimDevTools;

[assembly: SupportedOSPlatform("windows")]

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
        File.WriteAllText(Path.Combine(configDirectory, "fake-desktop.txt"), GameProcessLauncher.CurrentDesktopName() ?? "<unknown>");

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
        if (mode == "quick-split")
        {
            File.AppendAllText(playerLog, "[LogSyncMod] RUNTIME_READY_");
            await Task.Delay(700);
            File.AppendAllText(playerLog, "TO_CLOSE\n");
            await Task.Delay(TimeSpan.FromSeconds(30));
            return;
        }
        if (mode == "quick-large-log")
        {
            var largeLog = "[LogSyncMod] RUNTIME_READY_TO_CLOSE\n" + string.Concat(Enumerable.Repeat(new string('x', 96) + "\n", 600));
            File.AppendAllText(playerLog, largeLog);
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
        File.WriteAllText(Path.Combine(outputPath, $"{packageId}_{runId}_Result.json"), JsonSerializer.Serialize(result));
        var completion = $"[LogSyncAutoTest] COMPLETED runId={runId} packageId={packageId}\n";
        File.AppendAllText(playerLog, mode == "auto-large-log"
            ? completion + string.Concat(Enumerable.Repeat(new string('y', 96) + "\n", 600))
            : completion);

        // The real AutoTest extension persists autoTestEnabled=false after completion.
        settings.Descendants("autoTestEnabled").First().Value = "False";
        File.WriteAllText(settingsPath, settings.ToString());
        await Task.Delay(TimeSpan.FromSeconds(30));
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
            TestEnvironmentDiscovery();
            await TestTemplatePreflightAsync();
            await TestHelpAsync();
            TestCliSelectionParsing();
            await TestToolModDeploymentAsync();
            TestModListParser();
            TestIncrementalLogSignalScanner();
            await TestSettingsAsync();
            await TestTransactionsAsync();
            await TestCleanupCommandAsync();
            TestDeploymentArtifactRecovery();
            await TestEnvironmentLockAsync();
            await TestModAsync();
            await TestDeployAsync();
            TestAutoResultValidator();
            TestLogArchive();
            await TestDoctorIsReadOnlyAsync();
            await TestRunPreflightAsync();
            await TestGameWorkflowsAsync();
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
        Assert(context!.Config.Retention.MaxAutoTestRuns == 3, "automatic test retention defaults to three runs");
        Assert(!context.Config.IsolatedDesktop, "isolated desktop defaults to disabled when omitted from JSONC");
        Assert(!context.Config.SyncSource, "source synchronization defaults to disabled when omitted from JSONC");
        var disabledConfig = new ProjectConfig { IsolatedDesktop = false };
        var configured = IsolatedDesktopOptions.Resolve(disabledConfig, "auto", [], out _);
        var overridden = IsolatedDesktopOptions.Resolve(disabledConfig, "auto", ["--isolated-desktop"], out _);
        var manual = IsolatedDesktopOptions.Resolve(context.Config, "manual", [], out _);
        Assert(configured is { Enabled: false, Source: "project_config" } && overridden is { Enabled: true, Source: "command_line" }, "isolated desktop CLI option overrides a disabled project setting");
        Assert(manual is { Enabled: false, Source: "mode_default" }, "manual isolation ignores the enabled project default");
        var steamLoggedIn = SteamSessionGuard.Evaluate(true, "C:\\Steam", "\"users\" { \"76561198000000000\" { \"AccountName\" \"tester\" \"MostRecent\" \"1\" \"WantsOfflineMode\" \"0\" } }");
        var steamOffline = SteamSessionGuard.Evaluate(true, "C:\\Steam", "\"users\" { \"76561198000000000\" { \"AccountName\" \"tester\" \"MostRecent\" \"1\" \"WantsOfflineMode\" \"1\" } }");
        var steamLoggedInNewFormat = SteamSessionGuard.Evaluate(true, "C:\\Steam", "\"users\" { \"1\" { \"AccountName\" \"older\" \"RememberPassword\" \"1\" \"WantsOfflineMode\" \"0\" \"Timestamp\" \"10\" } \"2\" { \"AccountName\" \"current\" \"RememberPassword\" \"1\" \"WantsOfflineMode\" \"0\" \"Timestamp\" \"20\" } }");
        var steamOfflineNewFormat = SteamSessionGuard.Evaluate(true, "C:\\Steam", "\"users\" { \"1\" { \"AccountName\" \"older\" \"RememberPassword\" \"1\" \"WantsOfflineMode\" \"0\" \"Timestamp\" \"10\" } \"2\" { \"AccountName\" \"current\" \"RememberPassword\" \"1\" \"WantsOfflineMode\" \"1\" \"Timestamp\" \"20\" } }");
        var steamHistoricalAccount = SteamSessionGuard.Evaluate(true, "C:\\Steam", "\"users\" { \"1\" { \"AccountName\" \"historical\" \"RememberPassword\" \"1\" \"WantsOfflineMode\" \"0\" } }");
        var steamStopped = SteamSessionGuard.Evaluate(false, "C:\\Steam", null);
        Assert(steamLoggedIn.IsLoggedIn && steamLoggedIn.Code == "STEAM_LOGGED_IN", "Steam session guard accepts a running online account");
        Assert(!steamOffline.IsLoggedIn && steamOffline.Code == "STEAM_NOT_LOGGED_IN", "Steam session guard rejects offline mode");
        Assert(steamLoggedInNewFormat.IsLoggedIn && steamLoggedInNewFormat.Code == "STEAM_LOGGED_IN", "Steam session guard accepts the newest remembered account in the current Steam format");
        Assert(!steamOfflineNewFormat.IsLoggedIn && steamOfflineNewFormat.Code == "STEAM_NOT_LOGGED_IN", "Steam session guard rejects the newest current-format account when it requests offline mode");
        Assert(!steamHistoricalAccount.IsLoggedIn && steamHistoricalAccount.Code == "STEAM_NOT_LOGGED_IN", "Steam session guard does not treat an undated remembered account as an active session");
        Assert(!steamStopped.IsLoggedIn && steamStopped.Code == "STEAM_NOT_RUNNING", "Steam session guard rejects a stopped client");
        var child = NewDirectory(Path.Combine("discovery-project", "source"));
        Assert(ProjectDiscovery.Find(child, out _)?.Root == root, "project discovery checks direct parent");
        var grandchild = NewDirectory(Path.Combine("discovery-project", "source", "Core"));
        Assert(ProjectDiscovery.Find(grandchild, out var missing) is null && missing?.Code == "PROJECT_NOT_FOUND", "project discovery stops after direct parent");
    }

    private async Task TestHelpAsync()
    {
        var paths = new[]
        {
            Array.Empty<string>(), ["--help"], ["doctor", "--help"], ["deploy", "--help"],
            ["mod", "--help"], ["mod", "search", "--help"], ["mod", "copy", "--help"], ["mod", "dependencies", "--help"], ["mod", "dependents", "--help"],
            ["test", "--help"], ["test", "run", "--help"], ["test", "settings", "--help"], ["test", "cleanup", "--help"],
            ["mods", "--help"], ["mods", "install-tools", "--help"]
        };
        foreach (var args in paths)
        {
            var result = await CaptureAsync(() => CliApplication.RunAsync(args));
            Assert(result.ExitCode == 0 && result.Output.Contains("Usage:", StringComparison.Ordinal), $"help succeeds without project discovery: {string.Join(' ', args)}");
        }
        var topLevel = await CaptureAsync(() => CliApplication.RunAsync([]));
        Assert(topLevel.Output.Contains("mod search", StringComparison.Ordinal)
            && topLevel.Output.Contains("mod copy", StringComparison.Ordinal)
            && topLevel.Output.Contains("test cleanup", StringComparison.Ordinal)
            && topLevel.Output.Contains("mods install-tools", StringComparison.Ordinal), "top-level help lists concrete subcommands");

        foreach (var command in new[] { "deploy", "mod search", "mod copy", "mod dependencies", "mod dependents", "test run", "test cleanup", "mods install-tools" })
        {
            using var writer = new StringWriter();
            var original = Console.Out;
            Console.SetOut(writer);
            try { Assert(CliHelp.TryWrite(command), $"help metadata exists for {command}"); }
            finally { Console.SetOut(original); }
            var help = writer.ToString();
            foreach (var option in CliArgs.OptionNames(command))
                Assert(help.Contains(option, StringComparison.Ordinal), $"{command} help documents {option}");
        }

        var unknown = await CaptureAsync(() => CliApplication.RunAsync(["unknown", "--help"]));
        Assert(unknown.ExitCode == ExitCodes.InvalidArguments && JsonErrorCode(unknown.Output) == "UNKNOWN_COMMAND", "unknown command with help remains a structured error");
        var removedInit = await CaptureAsync(() => CliApplication.RunAsync(["init", "1.6", "--output", "unused"]));
        Assert(removedInit.ExitCode == ExitCodes.InvalidArguments && JsonErrorCode(removedInit.Output) == "UNKNOWN_COMMAND", "removed init command returns the standard unknown-command error");
        var removedData = await CaptureAsync(() => CliApplication.RunAsync(["data", "status"]));
        Assert(removedData.ExitCode == ExitCodes.InvalidArguments && JsonErrorCode(removedData.Output) == "UNKNOWN_COMMAND", "removed data command returns the standard unknown-command error");
        var removedRef = await CaptureAsync(() => CliApplication.RunAsync(["ref", "search", "anything"]));
        Assert(removedRef.ExitCode == ExitCodes.InvalidArguments && JsonErrorCode(removedRef.Output) == "UNKNOWN_COMMAND", "reference command namespace is no longer exposed");
        var removedOption = await CaptureAsync(() => CliApplication.RunAsync(["mod", "copy", "anything", "--force"]));
        Assert(removedOption.ExitCode == ExitCodes.InvalidArguments && JsonErrorCode(removedOption.Output) == "INVALID_ARGUMENT", "old force option is no longer accepted");
        foreach (var oldArgs in new[]
        {
            new[] { "mod", "copy", "anything", "--all" },
            ["test", "run", "--mods-file", "ModsConfig.xml"],
            ["test", "run", "--auto-test-scope", "standard"],
            ["test", "run", "--auto-test-defname", "Demo_Coat"]
        })
        {
            var removed = await CaptureAsync(() => CliApplication.RunAsync(oldArgs));
            Assert(removed.ExitCode == ExitCodes.InvalidArguments && JsonErrorCode(removed.Output) == "INVALID_ARGUMENT", $"old option is no longer accepted: {string.Join(' ', oldArgs)}");
        }
        var version = await CaptureAsync(() => CliApplication.RunAsync(["--version"]));
        using var versionJson = JsonDocument.Parse(version.Output);
        Assert(version.ExitCode == 0
            && versionJson.RootElement.EnumerateObject().Select(property => property.Name).SequenceEqual(["ok", "command", "rimDevToolsVersion"]), "version output only reports the RimDevTools version");

        var localized = await CaptureAsync(() => Task.FromResult(CliResult.WriteFailure("deploy", ExitCodes.InvalidArguments,
            "PROJECT_TEMPLATE_NOT_CONFIGURED", "部署或测试被拒绝：项目仍包含模板占位符。")));
        using var localizedJson = JsonDocument.Parse(localized.Output);
        Assert(localizedJson.RootElement.GetProperty("error").GetProperty("message").GetString() == "部署或测试被拒绝：项目仍包含模板占位符。"
            && localized.Output.Contains("部署或测试被拒绝", StringComparison.Ordinal)
            && !localized.Output.Contains("\\u90E8", StringComparison.OrdinalIgnoreCase), "structured JSON output preserves readable Unicode text");
    }

    private async Task TestTemplatePreflightAsync()
    {
        var environment = CreateProjectEnvironment("template-preflight", createGameExecutable: false);
        File.WriteAllText(environment.ConfigPath, Config(environment, modName: ProjectTemplatePreflight.TemplateModName));
        File.WriteAllText(Path.Combine(environment.Root, "About", "About.xml"), $"<ModMetaData><name>{ProjectTemplatePreflight.TemplateDisplayName}</name><packageId>{ProjectTemplatePreflight.TemplatePackageId}</packageId></ModMetaData>");
        var projectPath = Path.Combine(environment.Root, "source", "TestProject", "TestProject.csproj");
        File.WriteAllText(projectPath, File.ReadAllText(projectPath).Replace("<AssemblyName>TestProject</AssemblyName>", "<AssemblyName>MyCustomMod</AssemblyName>", StringComparison.Ordinal));
        var outputDirectory = Path.Combine(environment.Root, "1.6", "Assemblies");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, "MyCustomMod.dll"), "template");
        using var scope = UseEnvironment(environment);

        var context = ProjectDiscovery.Find(environment.Root, out var discoveryError);
        var issues = context is null ? Array.Empty<ProjectTemplateIssue>() : ProjectTemplatePreflight.Inspect(context);
        Assert(discoveryError is null && issues.Any(issue => issue.field == "mod_config.mod_name") && issues.Any(issue => issue.field == "About/About.xml:name") && issues.Any(issue => issue.field == "About/About.xml:packageId") && issues.Any(issue => issue.field.EndsWith(":AssemblyName", StringComparison.Ordinal)) && issues.Any(issue => issue.field.EndsWith("MyCustomMod.dll", StringComparison.Ordinal)), "template inspection detects branded defaults and the template DLL name");

        var doctor = await CaptureAsync(() => DoctorCommand.RunAsync(["doctor"]));
        using (var doctorDocument = JsonDocument.Parse(doctor.Output))
        {
            var result = doctorDocument.RootElement.GetProperty("result");
            var templateCheck = result.GetProperty("checks").EnumerateArray().Single(check => check.GetProperty("id").GetString() == "project.template_configuration");
            Assert(!result.GetProperty("ready").GetBoolean() && templateCheck.GetProperty("status").GetString() == "invalid", "doctor marks a branded template as not deployable");
        }

        var deploy = await DeployCommand.ExecuteAsync(["deploy", "--dry-run"]);
        Assert(!deploy.Ok && deploy.Error?.Code == "PROJECT_TEMPLATE_NOT_CONFIGURED", "deploy rejects the template DLL before planning a deployment");
        var testRun = await CaptureAsync(() => TestCommand.RunAsync(["test", "run", "--mode", "quick"]));
        Assert(testRun.ExitCode == ExitCodes.InvalidArguments && JsonErrorCode(testRun.Output) == "PROJECT_TEMPLATE_NOT_CONFIGURED", "test run rejects the template DLL before starting RimWorld");
        var testSettings = await CaptureAsync(() => TestCommand.SettingsAsync(["test", "settings"]));
        Assert(testSettings.ExitCode == ExitCodes.InvalidArguments && JsonErrorCode(testSettings.Output) == "PROJECT_TEMPLATE_NOT_CONFIGURED", "test settings rejects a branded template");
    }

    private void TestEnvironmentDiscovery()
    {
        var valve = ValveKeyValues.Parse("\"root\" { \"path\" \"D:\\\\SteamLibrary\" \"nested\" { \"value\" \"ok\" } }").Single();
        Assert(valve.Scalar("path") == @"D:\SteamLibrary" && valve.Child("nested")?.Scalar("value") == "ok", "Valve KeyValues parser handles nested objects and escaped paths");
        AssertThrows<InvalidDataException>(() => ValveKeyValues.Parse("\"root\" { \"path\" \"unterminated"), "Valve KeyValues parser rejects malformed input");

        var steam = NewDirectory(Path.Combine("discovery", "Steam"));
        File.WriteAllText(Path.Combine(steam, "steam.exe"), "steam");
        var library = NewDirectory(Path.Combine("discovery", "Library With Space"));
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), $"\"libraryfolders\" {{ \"0\" {{ \"path\" \"{Escape(steam)}\" }} \"1\" {{ \"path\" \"{Escape(library)}\" }} }}");
        Directory.CreateDirectory(Path.Combine(library, "steamapps"));
        File.WriteAllText(Path.Combine(library, "steamapps", "appmanifest_294100.acf"), "\"AppState\" { \"appid\" \"294100\" \"installdir\" \"RimWorld\" \"StateFlags\" \"4\" }");
        var discoveredGame = Path.Combine(library, "steamapps", "common", "RimWorld");
        CreateGameStructure(discoveredGame);
        var workshop = NewDirectory(Path.Combine("discovery", "Library With Space", "steamapps", "workshop", "content", "294100"));
        var resolved = SteamInstallationResolver.Resolve(null, [], new SteamResolverInput([steam], []));
        Assert(resolved.RimWorldDirectory == discoveredGame && resolved.RimWorldSource == "steam" && resolved.WorkshopPaths.SequenceEqual([workshop], StringComparer.OrdinalIgnoreCase), "Steam resolver reads multiple libraries, appmanifest 294100, and Workshop paths");

        File.WriteAllText(Path.Combine(library, "steamapps", "appmanifest_294100.acf"), "\"AppState\" { \"appid\" \"294100\" \"installdir\" \"RimWorld\" \"StateFlags\" \"2\" }");
        var incompleteApp = SteamInstallationResolver.Resolve(null, [], new SteamResolverInput([steam], []));
        Assert(incompleteApp.RimWorldDirectory is null && incompleteApp.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("完整安装", StringComparison.Ordinal)), "Steam resolver rejects appmanifest entries not marked as fully installed");
        File.Delete(Path.Combine(library, "steamapps", "appmanifest_294100.acf"));
        var missingApp = SteamInstallationResolver.Resolve(null, [], new SteamResolverInput([steam], []));
        Assert(missingApp.AppManifestPath is null && missingApp.RimWorldDirectory is null, "Steam resolver reports a missing RimWorld appmanifest without guessing an installation");
        File.WriteAllText(Path.Combine(library, "steamapps", "appmanifest_294100.acf"), "\"AppState\" { \"appid\" \"294100\" \"installdir\" \"RimWorld\" \"StateFlags\" \"4\" }");

        var configuredGame = NewDirectory(Path.Combine("discovery", "ConfiguredGame"));
        CreateGameStructure(configuredGame);
        var configured = SteamInstallationResolver.Resolve(configuredGame, [workshop, workshop], new SteamResolverInput([steam], []));
        Assert(configured.RimWorldDirectory == configuredGame && configured.RimWorldSource == "configured" && configured.WorkshopPaths.Count == 1, "valid configured paths take priority and Workshop paths are deduplicated");

        var modsRoot = Path.Combine(configuredGame, "Mods");
        var prohibitedModsProject = Path.Combine(modsRoot, "UnsafeProject");
        var prohibitedWorkshopProject = Path.Combine(workshop, "UnsafeProject");
        Assert(ProjectLocationGuard.FindDeploymentLocations(prohibitedModsProject, configuredGame, [workshop]).Single().Kind == "rimworld_mods", "project location guard rejects projects inside RimWorld Mods");
        Assert(ProjectLocationGuard.FindDeploymentLocations(prohibitedWorkshopProject, configuredGame, [workshop]).Single().Kind == "steam_workshop", "project location guard rejects projects inside Steam Workshop content");
        var harmony = CreateModAt(modsRoot, "Harmony", "brrainz.harmony");
        Directory.CreateDirectory(Path.Combine(harmony, "1.5", "Assemblies"));
        File.WriteAllText(Path.Combine(harmony, "1.5", "Assemblies", "0Harmony.dll"), "old");
        Directory.CreateDirectory(Path.Combine(harmony, "Current", "Assemblies"));
        File.WriteAllText(Path.Combine(harmony, "Current", "Assemblies", "0Harmony.dll"), "current");
        File.WriteAllText(Path.Combine(harmony, "LoadFolders.xml"), "<loadFolders><v1.5><li>1.5</li></v1.5><v1.6><li>/</li><li>Current</li></v1.6></loadFolders>");
        var official = CreateModAt(Path.Combine(configuredGame, "Data"), "Core", "ludeon.rimworld");
        var workshopMod = CreateModAt(workshop, "MixedCase", "Demo.MixedCase");
        var broken = Path.Combine(workshop, "Broken", "About");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "About.xml"), "<broken>");
        var currentProject = NewDirectory(Path.Combine("discovery", "CurrentProject"));
        Directory.CreateDirectory(Path.Combine(currentProject, "About"));
        File.WriteAllText(Path.Combine(currentProject, "About", "About.xml"), "<ModMetaData><packageId>demo.current-project</packageId></ModMetaData>");
        Assert(ProjectLocationGuard.FindDeploymentLocations(currentProject, configuredGame, [workshop]).Count == 0, "project location guard accepts an independent working directory");
        var index = ModInstallationIndex.Build(configuredGame, [workshop], currentProject);
        Assert(index.Find("LUDEON.RIMWORLD")?.Directory == official && index.Find("demo.mixedcase")?.Directory == workshopMod && index.Find("demo.current-project")?.Source == "project" && index.Diagnostics.Count == 1, "mod index scans official, local, Workshop, and current-project roots case-insensitively and aggregates broken metadata");
        var harmonyAssembly = ModAssemblyResolver.Resolve(harmony, "1.6", "0Harmony.dll");
        Assert(harmonyAssembly.AssemblyPath == Path.Combine(harmony, "Current", "Assemblies", "0Harmony.dll"), "Harmony resolver follows RimWorld 1.6 LoadFolders instead of selecting an older assembly");

        var targetingPack48 = NewDirectory(Path.Combine("discovery", "Reference Assemblies", "v4.8"));
        var targetingPack472 = NewDirectory(Path.Combine("discovery", "Reference Assemblies", "v4.7.2"));
        File.WriteAllText(Path.Combine(targetingPack48, "mscorlib.dll"), "reference");
        File.WriteAllText(Path.Combine(targetingPack472, "mscorlib.dll"), "reference");
        var fakeDotnet = Path.Combine(_temp, "discovery", "dotnet.exe");
        File.WriteAllText(fakeDotnet, "dotnet");
        var build = DotNetBuildEnvironmentInspector.Inspect(new DotNetBuildEnvironmentInput(fakeDotnet, ["10.0.100 [sdk]"], [targetingPack472, targetingPack48]));
        Assert(build.HasDotNet && build.HasSdk && build.HasSupportedNetFrameworkTargetingPack && build.TargetFrameworkVersion == "v4.8" && build.TargetingPackPath == targetingPack48, "build environment inspector prefers .NET Framework 4.8 when both supported targeting packs are installed");
        var fallbackBuild = DotNetBuildEnvironmentInspector.Inspect(new DotNetBuildEnvironmentInput(fakeDotnet, ["10.0.100 [sdk]"], [targetingPack472]));
        Assert(fallbackBuild.HasSupportedNetFrameworkTargetingPack && fallbackBuild.TargetFrameworkVersion == "v4.7.2" && fallbackBuild.TargetingPackPath == targetingPack472, "build environment inspector accepts .NET Framework 4.7.2 as a compilation fallback");
        var missingBuild = DotNetBuildEnvironmentInspector.Inspect(new DotNetBuildEnvironmentInput(null, [], [Path.Combine(_temp, "missing-pack")]));
        Assert(!missingBuild.HasDotNet && !missingBuild.HasSdk && !missingBuild.HasSupportedNetFrameworkTargetingPack, "build environment inspector reports each missing prerequisite independently");

        var project48 = Path.Combine(currentProject, "net48.csproj");
        var project472 = Path.Combine(currentProject, "net472.csproj");
        var projectUnsupported = Path.Combine(currentProject, "net462.csproj");
        File.WriteAllText(project48, "<Project><PropertyGroup><TargetFrameworkVersion>v4.8</TargetFrameworkVersion></PropertyGroup></Project>");
        File.WriteAllText(project472, "<Project><PropertyGroup><TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion></PropertyGroup></Project>");
        File.WriteAllText(projectUnsupported, "<Project><PropertyGroup><TargetFrameworkVersion>v4.6.2</TargetFrameworkVersion></PropertyGroup></Project>");
        Assert(DoctorCommand.EvaluateProjectCheck(currentProject, "net48.csproj").Status == "available" && DoctorCommand.EvaluateProjectCheck(currentProject, "net472.csproj").Status == "available", "doctor accepts projects targeting .NET Framework 4.8 or 4.7.2");
        Assert(DoctorCommand.EvaluateProjectCheck(currentProject, "net462.csproj").Status == "unsupported", "doctor still rejects unsupported .NET Framework project targets");

        var fallbackMessage = @"已找到 .NET Framework 4.7.2 Developer/Targeting Pack，可满足编译条件；仍建议安装 .NET Framework 4.8。AI 可能需要将当前 C# 工程的 TargetFrameworkVersion 修改为 v4.7.2。";
        var fallbackSummary = DoctorCommand.Summarize([new DoctorCheck("build.netfx_targeting_pack", "available", true, targetingPack472, fallbackMessage)], verbose: false);
        Assert(fallbackSummary.Ready && fallbackSummary.IssueCount == 0 && fallbackSummary.Checks.Single().Message == fallbackMessage, "doctor keeps the .NET Framework 4.7.2 fallback ready while showing the AI repair hint by default");

        var defaultSummary = DoctorCommand.Summarize(
        [
            new DoctorCheck("ok", "available", true, null),
            new DoctorCheck("build.game_assembly.Assembly-CSharp", "available", true, @"H:\RimWorld\Managed\Assembly-CSharp.dll"),
            new DoctorCheck("build.harmony_assembly", "available", true, @"H:\Workshop\Harmony\0Harmony.dll"),
            new DoctorCheck("required", "missing", true, null),
            new DoctorCheck("optional", "stale", false, null)
        ], verbose: false);
        var verboseSummary = DoctorCommand.Summarize([new DoctorCheck("ok", "available", true, null), new DoctorCheck("required", "missing", true, null)], verbose: true);
        Assert(!defaultSummary.Ready && defaultSummary.IssueCount == 1 && defaultSummary.WarningCount == 1 && defaultSummary.Checks.Count == 4
            && defaultSummary.Checks.Any(check => check.Id == "build.game_assembly.Assembly-CSharp" && check.Path == @"H:\RimWorld\Managed\Assembly-CSharp.dll")
            && defaultSummary.Checks.Any(check => check.Id == "build.harmony_assembly" && check.Path == @"H:\Workshop\Harmony\0Harmony.dll"), "doctor default summary keeps both critical assembly paths visible while hiding unrelated successful checks");
        Assert(verboseSummary.Checks.Count == 2, "doctor verbose summary includes successful checks");
        var harmonyLow = HarmonyVersionInspector.Inspect(typeof(TestSuite).Assembly.Location, new Version(99, 0, 0, 0));
        var harmonyEqual = HarmonyVersionInspector.Inspect(typeof(TestSuite).Assembly.Location, new Version(1, 0, 0, 0));
        var harmonyHigher = HarmonyVersionInspector.Inspect(typeof(TestSuite).Assembly.Location, new Version(0, 9, 0, 0));
        var brokenHarmony = Path.Combine(currentProject, "broken-harmony.dll");
        File.WriteAllText(brokenHarmony, "not-an-assembly");
        Assert(harmonyLow.Status == "stale" && harmonyLow.Message!.Contains("请尝试重新订阅 Harmony"), "doctor warns when installed Harmony is below the required version");
        Assert(harmonyEqual.Status == "available" && harmonyHigher.Status == "available", "doctor accepts an equal or newer Harmony version");
        Assert(HarmonyVersionInspector.Inspect(brokenHarmony).Status == "invalid" && HarmonyVersionInspector.Inspect(Path.Combine(currentProject, "missing-harmony.dll")).Status == "missing", "doctor distinguishes invalid and missing Harmony DLL versions");
        var gitMissing = DoctorCommand.EvaluateGitChecks(currentProject, null, null);
        var gitNotWorktree = DoctorCommand.EvaluateGitChecks(currentProject, fakeDotnet, null);
        var gitNested = DoctorCommand.EvaluateGitChecks(currentProject, fakeDotnet, Path.GetDirectoryName(currentProject));
        var gitExact = DoctorCommand.EvaluateGitChecks(currentProject, fakeDotnet, currentProject);
        Assert(gitMissing.Single(check => check.Id == "git.command").Status == "missing" && gitMissing.Single(check => check.Id == "git.worktree").Status == "not_applicable", "doctor Git checks distinguish a missing command from worktree state");
        Assert(gitNotWorktree.Single(check => check.Id == "git.worktree").Status == "missing" && gitNested.Single(check => check.Id == "git.worktree").Status == "invalid" && gitExact.Single(check => check.Id == "git.worktree").Status == "available", "doctor Git checks distinguish non-worktree, nested repository, and exact project root");
    }

    private async Task TestToolModDeploymentAsync()
    {
        var dataRoot = NewDirectory(Path.Combine("tool-mods", "data"));
        var modsRoot = NewDirectory(Path.Combine("tool-mods", "Mods"));
        var hashFixture = NewDirectory(Path.Combine("tool-mods", "hash-fixture"));
        File.WriteAllText(Path.Combine(hashFixture, "value.txt"), "first");
        var firstFixtureHash = DirectoryHasher.Compute(hashFixture);
        File.WriteAllText(Path.Combine(hashFixture, "value.txt"), "second");
        Assert(firstFixtureHash != DirectoryHasher.Compute(hashFixture), "tool Mod hash changes when file content changes");
        File.Move(Path.Combine(hashFixture, "value.txt"), Path.Combine(hashFixture, "renamed.txt"));
        Assert(firstFixtureHash != DirectoryHasher.Compute(hashFixture), "tool Mod hash includes normalized relative paths");
        var externalHashTarget = NewDirectory(Path.Combine("tool-mods", "hash-external"));
        var hashJunction = Path.Combine(hashFixture, "junction");
        var junctionProcess = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "/c", "mklink", "/J", hashJunction, externalHashTarget }
        })!;
        await junctionProcess.WaitForExitAsync();
        Assert(junctionProcess.ExitCode == 0, "test fixture creates tool Mod hash junction");
        junctionProcess.Dispose();
        try { AssertThrows<IOException>(() => DirectoryHasher.Compute(hashFixture), "tool Mod hash rejects Reparse Points"); }
        finally { if (Directory.Exists(hashJunction)) Directory.Delete(hashJunction); }
        var assemblySource = typeof(TestSuite).Assembly.Location;
        var definitions = new[]
        {
            (Folder: "LogSyncMod", PackageId: "trigger.logsyncmod", PrimaryAssembly: "1.6/Assemblies/LogSyncMod.dll")
        };
        var manifestComponents = new List<object>();
        foreach (var definition in definitions)
        {
            var root = NewDirectory(Path.Combine("tool-mods", "data", definition.Folder));
            Directory.CreateDirectory(Path.Combine(root, "About"));
            var assemblyPath = Path.Combine(root, definition.PrimaryAssembly.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(assemblyPath)!);
            File.Copy(assemblySource, assemblyPath, true);
            File.WriteAllText(Path.Combine(root, "About", "About.xml"), $"<ModMetaData><name>{definition.Folder}</name><modVersion>1.0.0.0</modVersion><packageId>{definition.PackageId}</packageId></ModMetaData>");
            var hash = DirectoryHasher.Compute(root);
            manifestComponents.Add(new { folder = definition.Folder, packageId = definition.PackageId, version = "1.0.0.0", primaryAssembly = definition.PrimaryAssembly, contentHash = hash });
        }
        File.WriteAllText(Path.Combine(dataRoot, "tool-mods.manifest.json"), JsonSerializer.Serialize(new { schemaVersion = 1, components = manifestComponents }));
        var extraManifest = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            components = new object[]
            {
                manifestComponents.Single(),
                new { folder = "Extra", packageId = "demo.extra", version = "1.0.0.0", primaryAssembly = "Extra.dll", contentHash = "sha256:00" }
            }
        });
        File.WriteAllText(Path.Combine(dataRoot, "tool-mods.manifest.json"), extraManifest);
        AssertThrows<InvalidDataException>(() => ToolModManifestReader.Load(Path.Combine(dataRoot, "tool-mods.manifest.json")), "tool Mod manifest rejects additional components");
        File.WriteAllText(Path.Combine(dataRoot, "tool-mods.manifest.json"), JsonSerializer.Serialize(new { schemaVersion = 1, components = manifestComponents }));

        File.WriteAllText(Path.Combine(dataRoot, "LogSyncMod", "unexpected.txt"), "changed");
        AssertThrows<ToolModDeploymentException>(() => ToolModInstaller.Plan(dataRoot, modsRoot), "tool Mod planning rejects a source hash mismatch");
        File.Delete(Path.Combine(dataRoot, "LogSyncMod", "unexpected.txt"));

        var firstPlan = ToolModInstaller.Plan(dataRoot, modsRoot);
        Assert(firstPlan.Components is [var firstComponent] && firstComponent.Action == "installed", "tool Mod plan installs the missing LogSyncMod component");
        var installed = ToolModInstaller.Install(firstPlan, "first-install");
        Assert(installed is [var installedComponent] && installedComponent.Action == "installed", "tool Mod installation completes the single component");
        Assert(Directory.Exists(Path.Combine(modsRoot, "LogSyncMod")), "tool Mod installation creates the LogSyncMod directory");

        var rollbackMods = NewDirectory(Path.Combine("tool-mods", "rollback-Mods"));
        var rollbackPlan = ToolModInstaller.Plan(dataRoot, rollbackMods);
        File.WriteAllText(Path.Combine(rollbackMods, "LogSyncMod"), "block replacement");
        AssertThrows<IOException>(() => ToolModInstaller.Install(rollbackPlan, "rollback"), "tool Mod installation surfaces a target replacement failure");
        Assert(File.Exists(Path.Combine(rollbackMods, "LogSyncMod"))
            && !Directory.EnumerateFileSystemEntries(rollbackMods).Any(path => Path.GetFileName(path).StartsWith(".rimdev-tools-", StringComparison.OrdinalIgnoreCase)), "failed tool Mod installation preserves the target and cleans transaction directories");

        var stableHash = DirectoryHasher.Compute(Path.Combine(modsRoot, "LogSyncMod"), ["PublishedFileId.txt"]);
        File.SetLastWriteTimeUtc(Path.Combine(modsRoot, "LogSyncMod", "About", "About.xml"), DateTime.UtcNow.AddDays(-3));
        Assert(stableHash == DirectoryHasher.Compute(Path.Combine(modsRoot, "LogSyncMod"), ["PublishedFileId.txt"]), "tool Mod hash ignores file timestamps");

        File.WriteAllText(Path.Combine(modsRoot, "LogSyncMod", "PublishedFileId.txt"), "published");
        File.WriteAllText(Path.Combine(modsRoot, "LogSyncMod", "payload.txt"), "local-old");
        File.WriteAllText(Path.Combine(modsRoot, "LogSyncMod", "About", "About.xml"), "<ModMetaData><name>LogSyncMod</name><modVersion>0.9.0.0</modVersion><packageId>trigger.logsyncmod</packageId></ModMetaData>");
        var updatePlan = ToolModInstaller.Plan(dataRoot, modsRoot);
        Assert(updatePlan.Components.Single(x => x.Source.PackageId == "trigger.logsyncmod").Action == "updated", "older installed tool Mod is eligible for upgrade");
        ToolModInstaller.Install(updatePlan, "upgrade");
        Assert(File.ReadAllText(Path.Combine(modsRoot, "LogSyncMod", "PublishedFileId.txt")) == "published", "tool Mod upgrade preserves PublishedFileId.txt");
        var unchangedPlan = ToolModInstaller.Plan(dataRoot, modsRoot);
        Assert(unchangedPlan.Components.All(x => x.Action == "unchanged"), "identical tool Mod content is skipped after upgrade");

        File.WriteAllText(Path.Combine(modsRoot, "LogSyncMod", "payload.txt"), "local-new");
        File.WriteAllText(Path.Combine(modsRoot, "LogSyncMod", "About", "About.xml"), "<ModMetaData><name>LogSyncMod</name><modVersion>2.0.0.0</modVersion><packageId>trigger.logsyncmod</packageId></ModMetaData>");
        var newerPlan = ToolModInstaller.Plan(dataRoot, modsRoot);
        Assert(newerPlan.Components.Single(x => x.Source.PackageId == "trigger.logsyncmod").Action == "target_newer", "newer installed tool Mod is not downgraded");
        ToolModInstaller.Install(newerPlan, "newer-target");
        Assert(File.ReadAllText(Path.Combine(modsRoot, "LogSyncMod", "payload.txt")) == "local-new", "newer target remains unchanged");

        File.WriteAllText(Path.Combine(modsRoot, "LogSyncMod", "About", "About.xml"), "<ModMetaData><name>LogSyncMod</name><modVersion>1.0.0.0</modVersion><packageId>trigger.logsyncmod</packageId></ModMetaData>");
        var conflictPlan = ToolModInstaller.Plan(dataRoot, modsRoot);
        Assert(conflictPlan.Components.Single(x => x.Source.PackageId == "trigger.logsyncmod").Action == "same_version_conflict", "same-version different tool Mod content is rejected");
        AssertThrows<ToolModDeploymentException>(() => ToolModInstaller.Install(conflictPlan, "same-version-conflict"), "same-version tool Mod conflict blocks the batch");

        File.WriteAllText(Path.Combine(modsRoot, "LogSyncMod", "About", "About.xml"), "<ModMetaData><name>LogSyncMod</name><packageId>trigger.logsyncmod</packageId></ModMetaData>");
        File.Delete(Path.Combine(modsRoot, "LogSyncMod", "1.6", "Assemblies", "LogSyncMod.dll"));
        var unknownPlan = ToolModInstaller.Plan(dataRoot, modsRoot);
        Assert(unknownPlan.Components.Single(x => x.Source.PackageId == "trigger.logsyncmod").Action == "unknown_target_version", "unversioned installed tool Mod is not overwritten");

        CreateModAt(modsRoot, "UnrelatedDuplicateA", "demo.unrelated-duplicate");
        CreateModAt(modsRoot, "UnrelatedDuplicateB", "demo.unrelated-duplicate");
        Assert(ToolModInstaller.Plan(dataRoot, modsRoot).Components.Count == 1, "duplicate packageIds for unrelated Mods do not block tool Mod planning");

        var duplicate = Path.Combine(modsRoot, "LogSyncDuplicate");
        CopyDirectoryForTest(Path.Combine(modsRoot, "LogSyncMod"), duplicate);
        AssertThrows<ToolModDeploymentException>(() => ToolModInstaller.Plan(dataRoot, modsRoot), "duplicate tool Mod packageId blocks planning");

        var oldDataPath = Environment.GetEnvironmentVariable("RIMDEVTOOLS_DATA_PATH");
        try
        {
            Environment.SetEnvironmentVariable("RIMDEVTOOLS_DATA_PATH", dataRoot);
            var dryRun = await CaptureAsync(() => ModsCommand.InstallToolsAsync(["mods", "install-tools", "--mods-path", Path.Combine(_temp, "tool-mods", "empty-mods"), "--dry-run"]));
            Assert(dryRun.ExitCode == ExitCodes.MissingDependency && JsonErrorCode(dryRun.Output) == "MODS_PATH_NOT_FOUND", "tool Mod command validates explicit Mods paths before mutation");
            var wrongName = NewDirectory(Path.Combine("tool-mods", "not-a-mods-root"));
            var wrongNameResult = await CaptureAsync(() => ModsCommand.InstallToolsAsync(["mods", "install-tools", "--mods-path", wrongName, "--dry-run"]));
            Assert(wrongNameResult.ExitCode == ExitCodes.InvalidArguments && JsonErrorCode(wrongNameResult.Output) == "MODS_PATH_INVALID", "tool Mod command rejects directories that are not named Mods");
            var duplicateResult = await CaptureAsync(() => ModsCommand.InstallToolsAsync(["mods", "install-tools", "--mods-path", modsRoot, "--dry-run"]));
            Assert(duplicateResult.ExitCode == ExitCodes.TaskFailed && JsonErrorCode(duplicateResult.Output) == "DUPLICATE_PACKAGE_ID", "tool Mod command reports duplicate packageIds as structured errors");
            Directory.Delete(duplicate, true);
            var validDryRun = await CaptureAsync(() => ModsCommand.InstallToolsAsync(["mods", "install-tools", "--mods-path", modsRoot, "--dry-run"]));
            Assert(validDryRun.ExitCode == 0 && JsonDocument.Parse(validDryRun.Output).RootElement.GetProperty("result").GetProperty("modsPath").GetString() == modsRoot, "tool Mod command accepts an explicit Mods path without project discovery");

            var configuredEnvironment = CreateProjectEnvironment("tool-mods-configured", createGameExecutable: true);
            var workshopRoot = NewDirectory(Path.Combine("tool-mods-configured", "Workshop"));
            CreateModAt(workshopRoot, "WorkshopLogSync", "trigger.logsyncmod");
            File.WriteAllText(configuredEnvironment.ConfigPath, Config(configuredEnvironment, extra: $"\"workshop_paths\":[\"{Escape(workshopRoot)}\"]"));
            using (UseEnvironment(configuredEnvironment))
            {
                var configuredDryRun = await CaptureAsync(() => ModsCommand.InstallToolsAsync(["mods", "install-tools", "--dry-run"]));
                Assert(configuredDryRun.ExitCode == 0
                    && JsonDocument.Parse(configuredDryRun.Output).RootElement.GetProperty("result").GetProperty("modsPath").GetString() == Path.Combine(configuredEnvironment.Game, "Mods"), "tool Mod command resolves Mods from project configuration when no explicit path is supplied");
                Assert(JsonDocument.Parse(configuredDryRun.Output).RootElement.GetProperty("result").GetProperty("warnings").GetArrayLength() == 1, "tool Mod command warns when a packaged tool Mod also exists in Workshop");
            }
        }
        finally { Environment.SetEnvironmentVariable("RIMDEVTOOLS_DATA_PATH", oldDataPath); }
    }

    private static void CopyDirectoryForTest(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(entry));
            if (Directory.Exists(entry)) CopyDirectoryForTest(entry, target);
            else File.Copy(entry, target, true);
        }
    }

    private void TestModListParser()
    {
        var environment = CreateProjectEnvironment("mod-list");
        CreateMod(environment.Game, "Harmony", "brrainz.harmony");
        CreateMod(environment.Game, "SmartSpeed", "sarg.smartspeed");
        CreateMod(environment.Game, "CameraPlus", "brrainz.cameraplus");
        CreateMod(environment.Game, "HumanoidAlienRaces", "erdelf.humanoidalienraces");
        CreateMod(environment.Game, "LogSync", "trigger.logsyncmod");
        CreateMod(environment.Game, "Helper", "demo.helper", "demo.parent");
        CreateMod(environment.Game, "Parent", "demo.parent");
        File.WriteAllText(environment.ConfigPath, Config(environment, minMods: "[\"brrainz.harmony\",\"sarg.smartspeed\",\"brrainz.cameraplus\",\"erdelf.humanoidalienraces\",\"trigger.logsyncmod\",\"demo.helper\"]"));
        var modsFile = Path.Combine(environment.Root, "mods.xml");
        File.WriteAllText(modsFile, "<ModsConfigData><ignored><li>not.allowed</li></ignored><activeMods><li>brrainz.harmony</li><li>sarg.smartspeed</li><li>brrainz.cameraplus</li><li>erdelf.humanoidalienraces</li><li>trigger.logsyncmod</li><li>demo.helper</li><li>DEMO.HELPER</li></activeMods></ModsConfigData>");
        var context = ProjectDiscovery.Find(environment.Root, out _)!;
        var resolution = ModListParser.Resolve(context, modsFile, out var error);
        Assert(error is null && resolution!.Mods.SequenceEqual(["brrainz.harmony", "ludeon.rimworld", "sarg.smartspeed", "brrainz.cameraplus", "erdelf.humanoidalienraces", "demo.current", "trigger.logsyncmod", "demo.helper"], StringComparer.OrdinalIgnoreCase), "ModList reads direct activeMods entries, enforces required mods, and deduplicates");
        Assert(resolution!.Additions.SequenceEqual([new ModListAddition("ludeon.rimworld", 1, "rimworld_core"), new ModListAddition("demo.current", 5, "current_project")]), "ModList reports automatic additions and positions");
        Assert(resolution.DependencyWarnings.Count == 1
            && resolution.DependencyWarnings[0].ModPackageId == "demo.helper"
            && resolution.DependencyWarnings[0].MissingDependencyPackageId == "demo.parent"
            && resolution.DependencyWarnings[0].DependencyInstalled, "ModList warns without blocking when an enabled mod's installed hard dependency is disabled");

        File.WriteAllText(modsFile, "<ModsConfigData><activeMods><li>brrainz.harmony</li><li>sarg.smartspeed</li><li>brrainz.cameraplus</li><li>erdelf.humanoidalienraces</li><li>trigger.logsyncmod</li><li>demo.helper</li><li>demo.parent</li></activeMods></ModsConfigData>");
        var completeDependencies = ModListParser.Resolve(context, modsFile, out var completeError);
        Assert(completeError is null && completeDependencies!.DependencyWarnings.Count == 0, "ModList dependency warning clears when the required parent mod is enabled");

        File.WriteAllText(modsFile, "<ModsConfigData><activeMods><li>brrainz.harmony</li><li>trigger.logsyncmod</li></activeMods></ModsConfigData>");
        var rejected = ModListParser.Resolve(context, modsFile, out var rejectedError);
        Assert(rejected is null && rejectedError?.Code == "REQUIRED_MODS_DISABLED" && JsonSerializer.Serialize(rejectedError.Details).Contains("demo.helper", StringComparison.OrdinalIgnoreCase), "ModList rejects external mod lists that omit configured min_active_mods");

    }

    private void TestIncrementalLogSignalScanner()
    {
        const string signal = "[LogSyncAutoTest] COMPLETED runId=current";
        var scanner = new IncrementalLogSignalScanner(signal);
        scanner.Append("prefix " + signal[..20]);
        scanner.Append(signal[20..] + string.Concat(Enumerable.Repeat(new string('x', 96) + "\n", 600)));
        Assert(scanner.HasSeen(signal), "incremental log scanner recognizes a split signal before more than 32 KiB of trailing output");
        scanner.Reset();
        Assert(!scanner.HasSeen(signal), "incremental log scanner resets after log truncation");

    }

    private void TestCliSelectionParsing()
    {
        Assert(CliArgs.Validate("test run", ["test", "run", "--scope", "turrets", "--scope", "threeviews"]) is null, "repeated --scope is accepted");
        Assert(CliArgs.Validate("test run", ["test", "run", "--defname", "Coat", "--defname", "Rifle"]) is null, "repeated --defname is accepted");
        Assert(CliArgs.Validate("test run", ["test", "run", "--mode", "auto", "--mode", "xml"])?.Contains("参数重复") == true, "non-repeatable options still reject duplicates");

        Assert(AutoTestSelectionParser.TryParse(["test", "run", "--mode", "auto", "--scope", "turrets", "--scope", "THREEVIEWS", "--scope", "turrets", "--defname", "Coat", "--defname", "coat"], "auto", out var selection, out _)
            && selection.Scopes.SequenceEqual(["Turrets", "ThreeViews"])
            && selection.DefNames.SequenceEqual(["Coat"]), "selection parser normalizes and preserves first-seen order");
        Assert(!AutoTestSelectionParser.TryParse(["test", "run", "--mode", "xml", "--scope", "all"], "xml", out _, out _), "all scope alias is no longer accepted");
        Assert(!AutoTestSelectionParser.TryParse(["test", "run", "--mode", "quick", "--scope", "turrets"], "quick", out _, out _), "scope is rejected outside auto/xml modes");
        Assert(!AutoTestSelectionParser.TryParse(["test", "run", "--mode", "xml", "--defname", "Coat"], "xml", out _, out _), "defname is rejected outside auto mode");
        Assert(!AutoTestSelectionParser.TryParse(["test", "run", "--mode", "auto", "--scope", "unknown"], "auto", out _, out _), "unknown scope is rejected");
        Assert(CliArgs.Validate("test run", ["test", "run", "--active-mods-file", "ModsConfig.xml"]) is null, "active mods file option is accepted");
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
        Assert(settings.Element("testMuteAudio")?.Value == "False", "persistent test settings do not enable audio muting");

        await SettingsEditor.UpdateAsync(context, "auto", scopes: ["Turrets", "ThreeViews", "turrets"], hasExplicitScopes: true, defNames: ["Coat", "coat"]);
        document = XDocument.Load(environment.SettingsPath);
        settings = document.Descendants("ModSettings").First();
        Assert(settings.Element("autoTestScopes")?.Elements("li").Select(x => x.Value).SequenceEqual(["Turrets", "ThreeViews"]) == true, "settings writer persists normalized multiple scopes");
        Assert(settings.Element("autoTestTargetDefNames")?.Elements("li").Select(x => x.Value).SequenceEqual(["Coat"]) == true, "settings writer persists normalized target DefNames");
        await SettingsEditor.UpdateAsync(context, "auto");
        document = XDocument.Load(environment.SettingsPath);
        settings = document.Descendants("ModSettings").First();
        Assert(settings.Element("autoTestScope")?.Value == "Standard" && settings.Element("autoTestScopes") is null && settings.Element("autoTestTargetDefNames") is null, "default settings clear prior multi-scope and target filters");
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

        var modsConfig = Path.Combine(directory, "ModsConfig.xml");
        File.WriteAllText(modsConfig, "<ModsConfigData><activeMods><li>original.mod</li></activeMods><other>before</other></ModsConfigData>");
        var settings = Path.Combine(directory, "settings.xml");
        File.WriteAllText(settings, "original-settings");
        var semantic = await ConfigTransaction.CreateAsync(directory, "semantic", [modsConfig, settings]);
        await semantic.WriteAllTextAsync(modsConfig, "<ModsConfigData><activeMods><li>temporary.mod</li></activeMods><other>before</other></ModsConfigData>");
        await semantic.WriteAllTextAsync(settings, "temporary-settings");
        File.WriteAllText(modsConfig, "<ModsConfigData><activeMods><li>game.mod</li></activeMods><other>after</other></ModsConfigData>");
        var preview = await TransactionRecovery.CleanupAsync(directory, modsConfig, dryRun: true);
        Assert(preview.Ok && File.ReadAllText(modsConfig).Contains("game.mod"), "transaction cleanup dry-run does not mutate ModsConfig");
        var semanticCleanup = await TransactionRecovery.CleanupAsync(directory, modsConfig, dryRun: false);
        var restoredModsConfig = XDocument.Load(modsConfig);
        Assert(semanticCleanup.Ok && restoredModsConfig.Descendants("activeMods").Single().Descendants("li").Single().Value == "original.mod" && restoredModsConfig.Descendants("other").Single().Value == "after" && File.ReadAllText(settings) == "original-settings", "transaction cleanup restores only original activeMods and ordinary settings");
        var repeatedCleanup = await TransactionRecovery.CleanupAsync(directory, modsConfig, dryRun: false);
        Assert(repeatedCleanup.Ok && !repeatedCleanup.Found, "transaction cleanup is idempotent after successful recovery");

        var malformedDirectory = NewDirectory("transactions-malformed");
        var malformedModsConfig = Path.Combine(malformedDirectory, "ModsConfig.xml");
        File.WriteAllText(malformedModsConfig, "<ModsConfigData><activeMods><li>original.mod</li></activeMods></ModsConfigData>");
        var malformed = await ConfigTransaction.CreateAsync(malformedDirectory, "malformed", [malformedModsConfig]);
        await malformed.WriteAllTextAsync(malformedModsConfig, "<ModsConfigData><activeMods><li>temporary.mod</li></activeMods></ModsConfigData>");
        File.WriteAllText(malformedModsConfig, "<broken");
        var malformedCleanup = await TransactionRecovery.CleanupAsync(malformedDirectory, malformedModsConfig, dryRun: false);
        Assert(malformedCleanup.Ok && XDocument.Load(malformedModsConfig).Descendants("li").Single().Value == "original.mod", "transaction cleanup restores the original ModsConfig when the current XML is truncated");

        var multiDirectory = NewDirectory("transactions-multiple");
        var multiModsConfig = Path.Combine(multiDirectory, "ModsConfig.xml");
        var multiSettings = Path.Combine(multiDirectory, "settings.xml");
        File.WriteAllText(multiModsConfig, "<ModsConfigData><activeMods><li>original.mod</li></activeMods></ModsConfigData>");
        File.WriteAllText(multiSettings, "original");
        var multiOne = await ConfigTransaction.CreateAsync(multiDirectory, "one", [multiModsConfig]);
        await multiOne.WriteAllTextAsync(multiModsConfig, "<ModsConfigData><activeMods><li>temporary.mod</li></activeMods></ModsConfigData>");
        var multiTwo = await ConfigTransaction.CreateAsync(multiDirectory, "two", [multiSettings]);
        await multiTwo.WriteAllTextAsync(multiSettings, "temporary");
        var multiCleanup = await TransactionRecovery.CleanupAsync(multiDirectory, multiModsConfig, dryRun: false);
        Assert(multiCleanup.Ok && multiCleanup.Items.Count == 2 && TransactionRecovery.PendingDirectories(multiDirectory).Count == 0, "transaction cleanup processes all safe pending transactions");

        var cleanupConflictDirectory = NewDirectory("transactions-cleanup-conflict");
        var cleanupConflictPath = Path.Combine(cleanupConflictDirectory, "settings.xml");
        var cleanupModsConfig = Path.Combine(cleanupConflictDirectory, "ModsConfig.xml");
        File.WriteAllText(cleanupModsConfig, "<ModsConfigData><activeMods /></ModsConfigData>");
        File.WriteAllText(cleanupConflictPath, "original");
        var cleanupConflict = await ConfigTransaction.CreateAsync(cleanupConflictDirectory, "conflict", [cleanupConflictPath]);
        await cleanupConflict.WriteAllTextAsync(cleanupConflictPath, "temporary");
        File.WriteAllText(cleanupConflictPath, "user-change");
        var cleanupConflictResult = await TransactionRecovery.CleanupAsync(cleanupConflictDirectory, cleanupModsConfig, dryRun: false);
        Assert(!cleanupConflictResult.Ok && cleanupConflictResult.Items.Single().Status == "conflict" && TransactionRecovery.PendingDirectories(cleanupConflictDirectory).Count == 1, "transaction cleanup retains ordinary files with user conflicts");

        var missingBackupDirectory = NewDirectory("transactions-missing-backup");
        var missingBackupPath = Path.Combine(missingBackupDirectory, "settings.xml");
        var missingBackupModsConfig = Path.Combine(missingBackupDirectory, "ModsConfig.xml");
        File.WriteAllText(missingBackupPath, "original");
        File.WriteAllText(missingBackupModsConfig, "<ModsConfigData><activeMods /></ModsConfigData>");
        var missingBackupTransaction = await ConfigTransaction.CreateAsync(missingBackupDirectory, "missing-backup", [missingBackupPath]);
        await missingBackupTransaction.WriteAllTextAsync(missingBackupPath, "temporary");
        var missingBackupDirectoryPath = TransactionRecovery.PendingDirectories(missingBackupDirectory).Single();
        File.Delete(Directory.EnumerateFiles(missingBackupDirectoryPath, "*.original").Single());
        var missingBackupResult = await TransactionRecovery.CleanupAsync(missingBackupDirectory, missingBackupModsConfig, dryRun: false);
        Assert(!missingBackupResult.Ok && missingBackupResult.Items.Single().Status == "failed" && TransactionRecovery.PendingDirectories(missingBackupDirectory).Count == 1, "transaction cleanup retains a transaction when its backup is missing");
    }

    private async Task TestCleanupCommandAsync()
    {
        var environment = CreateProjectEnvironment("cleanup-command", createGameExecutable: true);
        using var scope = UseEnvironment(environment);
        var transaction = await ConfigTransaction.CreateAsync(environment.ConfigDirectory, "cleanup-command", [environment.ModsConfigPath]);
        await transaction.WriteAllTextAsync(environment.ModsConfigPath, "<ModsConfigData><activeMods><li>temporary.mod</li></activeMods><other>tool</other></ModsConfigData>");
        var staleStaging = Path.Combine(environment.Game, "Mods", ".rimdev-staging-cccccccccccccccccccccccccccccccc");
        Directory.CreateDirectory(staleStaging);
        ProcessUtil.RimWorldRunningOverride = () => true;
        try
        {
            using (var heldLock = EnvironmentLock.TryAcquire(environment.ConfigDirectory, "held-for-status", environment.Root, "held", out _))
            {
                Assert(heldLock is not null, "cleanup status fixture acquires the environment lock");
                var status = await CaptureAsync(() => TestCommand.CleanupAsync(["test", "cleanup", "--status", "--dry-run"]));
                Assert(status.ExitCode == 0
                    && status.Output.Contains("cleanup-command", StringComparison.Ordinal)
                    && status.Output.Contains("\"gameRunning\": true", StringComparison.Ordinal)
                    && status.Output.Contains("\"environmentBusy\": true", StringComparison.Ordinal)
                    && TransactionRecovery.PendingDirectories(environment.ConfigDirectory).Count == 1
                    && Directory.Exists(staleStaging), "cleanup status reports the lock, transactions, game, and deployment artifacts without mutation");
            }
            var refused = await CaptureAsync(() => TestCommand.CleanupAsync(["test", "cleanup"]));
            Assert(refused.ExitCode == ExitCodes.Busy && JsonErrorCode(refused.Output) == "GAME_RUNNING_CLEANUP_REFUSED" && TransactionRecovery.PendingDirectories(environment.ConfigDirectory).Count == 1, "cleanup command refuses mutation while RimWorld is running");
        }
        finally { ProcessUtil.RimWorldRunningOverride = null; }

        var preview = await CaptureAsync(() => TestCommand.CleanupAsync(["test", "cleanup", "--dry-run"]));
        Assert(preview.ExitCode == 0 && preview.Output.Contains("deploymentArtifacts") && Directory.Exists(staleStaging), "cleanup dry-run reports deployment artifacts without removing them");
        var cleaned = await CaptureAsync(() => TestCommand.CleanupAsync(["test", "cleanup"]));
        Assert(cleaned.ExitCode == 0 && TransactionRecovery.PendingDirectories(environment.ConfigDirectory).Count == 0 && !Directory.Exists(staleStaging), "cleanup command restores transactions and removes stale deployment artifacts after RimWorld exits");
    }

    private void TestDeploymentArtifactRecovery()
    {
        var root = NewDirectory("artifact-recovery");
        var mods = NewDirectory(Path.Combine("artifact-recovery", "Mods"));
        var target = Path.Combine(mods, "Demo");
        var backup = Path.Combine(mods, ".rimdev-backup-0123456789abcdef0123456789abcdef");
        Directory.CreateDirectory(Path.Combine(backup, "About"));
        File.WriteAllText(Path.Combine(backup, "About", "About.xml"), "<ModMetaData><packageId>demo.current</packageId></ModMetaData>");
        File.WriteAllText(Path.Combine(backup, "sentinel.txt"), "old");
        var plan = DeploymentArtifactRecovery.Plan(mods, target, "demo.current");
        Assert(plan.Single().Action == "restore", "artifact recovery plans restoration when the target is missing");
        var result = DeploymentArtifactRecovery.Execute(mods, target, plan);
        Assert(result.Single().Action == "restored" && File.Exists(Path.Combine(target, "sentinel.txt")), "artifact recovery restores a unique backup");

        var secondBackup = Path.Combine(mods, ".rimdev-backup-fedcba9876543210fedcba9876543210");
        Directory.CreateDirectory(Path.Combine(secondBackup, "About"));
        File.WriteAllText(Path.Combine(secondBackup, "About", "About.xml"), "<ModMetaData><packageId>demo.current</packageId></ModMetaData>");
        var existingTargetPlan = DeploymentArtifactRecovery.Plan(mods, target, "demo.current");
        Assert(existingTargetPlan.Any(x => x.Action == "remove"), "artifact recovery plans removal when the active target exists");

        var ambiguousMods = NewDirectory(Path.Combine("artifact-recovery-ambiguous", "Mods"));
        var ambiguousTarget = Path.Combine(ambiguousMods, "Demo");
        foreach (var id in new[] { "11111111111111111111111111111111", "22222222222222222222222222222222" })
        {
            var candidate = Path.Combine(ambiguousMods, $".rimdev-backup-{id}");
            Directory.CreateDirectory(Path.Combine(candidate, "About"));
            File.WriteAllText(Path.Combine(candidate, "About", "About.xml"), "<ModMetaData><packageId>demo.current</packageId></ModMetaData>");
        }
        var ambiguousPlan = DeploymentArtifactRecovery.Plan(ambiguousMods, ambiguousTarget, "demo.current");
        Assert(ambiguousPlan.Count == 2 && ambiguousPlan.All(x => x.Action == "retain"), "artifact recovery retains multiple backups when the target is missing");
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

    private async Task TestModAsync()
    {
        AssertThrows<InvalidDataException>(() => ModCommand.SafeFolderName(".."), "unsafe mod name is rejected");
        Assert(ModCommand.SafeFolderName("CON") == "_CON", "Windows reserved mod name is sanitized");
        Assert(ModCommand.SafeFolderName("联合重工-UF Heavy:Industries") == "联合重工-UF_Heavy_Industries", "mod name whitespace and invalid characters are sanitized");

        var environment = CreateProjectEnvironment("ref");
        var workshop = NewDirectory(Path.Combine("ref", "workshop"));
        File.WriteAllText(environment.ConfigPath, Config(environment, extra: $"\"workshop_paths\":[\"{Escape(workshop)}\"]"));
        CreateMod(environment.Game, "DuplicateOne", "demo.duplicate");
        CreateModAt(workshop, "DuplicateTwo", "demo.duplicate");
        using var scope = UseEnvironment(environment);
        var ambiguous = await CaptureAsync(() => ModCommand.DependenciesAsync(["mod", "dependencies", "demo.duplicate"]));
        Assert(ambiguous.ExitCode == ExitCodes.InvalidArguments && JsonErrorCode(ambiguous.Output) == "MOD_AMBIGUOUS", "same-priority duplicate candidates fail explicitly");
        var reorderedSearch = await CaptureAsync(() => ModCommand.SearchAsync(["mod", "search", "--fuzzy", "Duplicate"]));
        Assert(reorderedSearch.ExitCode == 0, "mod query parsing accepts flags before the query");

        var cycleA = CreateMod(environment.Game, "CycleA", "demo.a", "demo.b");
        CreateMod(environment.Game, "CycleB", "demo.b", "demo.a");
        var cycle = await CaptureAsync(() => ModCommand.CopyAsync(["mod", "copy", "demo.a", "--deps"]));
        Assert(cycle.ExitCode == ExitCodes.InvalidArguments && JsonErrorCode(cycle.Output) == "DEPENDENCY_CYCLE", "dependency cycles fail before writing targets");
        Assert(!Directory.Exists(Path.Combine(environment.Root, "参考文档", "CycleA")) && Directory.Exists(cycleA), "cycle failure leaves source and targets untouched");

        var versioned = CreateMod(environment.Game, "Versioned", "demo.versioned");
        File.WriteAllText(Path.Combine(versioned, "About", "About.xml"), "<ModMetaData><name>Versioned</name><packageId>demo.versioned</packageId><modDependenciesByVersion><v1.5><li><packageId>demo.old</packageId></li></v1.5><v1.6><li><packageId>demo.new</packageId></li></v1.6></modDependenciesByVersion></ModMetaData>");
        var versionResult = await CaptureAsync(() => ModCommand.DependenciesAsync(["mod", "dependencies", "demo.versioned", "--version", "1.5"]));
        Assert(versionResult.ExitCode == 0 && versionResult.Output.Contains("demo.old") && !versionResult.Output.Contains("demo.new"), "mod --version selects version-specific dependencies");

        var collisionSlash = CreateMod(environment.Game, "CollisionSlash", "demo/x");
        var collisionBackslash = CreateMod(environment.Game, "CollisionBackslash", "demo\\x");
        File.WriteAllText(Path.Combine(collisionSlash, "About", "About.xml"), "<ModMetaData><name>Same/Name</name><packageId>demo/x</packageId></ModMetaData>");
        File.WriteAllText(Path.Combine(collisionBackslash, "About", "About.xml"), "<ModMetaData><name>Same\\Name</name><packageId>demo\\x</packageId></ModMetaData>");
        var collisionRoot = CreateMod(environment.Game, "CollisionRoot", "demo.collision-root");
        File.WriteAllText(Path.Combine(collisionRoot, "About", "About.xml"), "<ModMetaData><name>Root</name><packageId>demo.collision-root</packageId><modDependencies><li><packageId>demo/x</packageId></li><li><packageId>demo\\x</packageId></li></modDependencies></ModMetaData>");
        var collision = await CaptureAsync(() => ModCommand.CopyAsync(["mod", "copy", "demo.collision-root", "--deps"]));
        Assert(collision.ExitCode == ExitCodes.InvalidArguments && JsonErrorCode(collision.Output) == "TARGET_COLLISION", "mod copy rejects packageIds that map to the same safe target");

        var referenceMod = CreateMod(environment.Game, "Reference", "demo.reference", content: "v1");
        Directory.CreateDirectory(Path.Combine(referenceMod, "Ignored"));
        File.WriteAllText(Path.Combine(referenceMod, "Ignored", "ignored.txt"), "included on request");
        File.WriteAllText(environment.ConfigPath, Config(environment, extra: $"\"workshop_paths\":[\"{Escape(workshop)}\"],\"pull_ignore_folders\":[\"Ignored\"]"));
        var dryRun = await CaptureAsync(() => ModCommand.CopyAsync(["mod", "copy", "demo.reference", "--dry-run"]));
        Assert(dryRun.ExitCode == 0 && dryRun.Output.Contains("dependencyClosure") && !Directory.Exists(Path.Combine(environment.Root, "参考文档")), "mod copy dry-run reports the plan without creating reference targets");
        var pulled = await CaptureAsync(() => ModCommand.CopyAsync(["mod", "copy", "demo.reference"]));
        Assert(pulled.ExitCode == 0
            && File.ReadAllText(Path.Combine(environment.Root, "参考文档", "Reference", "content.txt")) == "v1"
            && !Directory.Exists(Path.Combine(environment.Root, "参考文档", "Reference", "Ignored")), "mod copy installs staged mod and applies pull_ignore_folders");
        var overwritten = await CaptureAsync(() => ModCommand.CopyAsync(["mod", "copy", "demo.reference", "--include-ignored", "--overwrite-existing"]));
        Assert(overwritten.ExitCode == 0 && File.Exists(Path.Combine(environment.Root, "参考文档", "Reference", "Ignored", "ignored.txt")), "mod copy includes ignored folders and overwrites an existing target only when explicitly requested");

        CreateMod(environment.Game, "OfficialDependency", "demo.official-dependency", "ludeon.rimworld.royalty");
        var officialDependency = await CaptureAsync(() => ModCommand.CopyAsync(["mod", "copy", "demo.official-dependency", "--deps"]));
        Assert(officialDependency.ExitCode == 0 && Directory.Exists(Path.Combine(environment.Root, "参考文档", "OfficialDependency")), "mod copy skips official RimWorld dependencies that are not stored under Mods");

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
            var unsafeCopy = await CaptureAsync(() => ModCommand.CopyAsync(["mod", "copy", "demo.reference"]));
            Assert(unsafeCopy.ExitCode == ExitCodes.InvalidArguments && JsonErrorCode(unsafeCopy.Output) == "REFERENCE_TARGET_UNSAFE", "mod copy rejects reparse points in the target path");
            Assert(File.ReadAllText(Path.Combine(externalTarget, "sentinel.txt")) == "outside", "unsafe mod target remains untouched");
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
            new ModCommand.CopyTarget(candidate, target1, staging1, Path.Combine(batch, "backup1")),
            new ModCommand.CopyTarget(candidate, target2, Path.Combine(batch, "missing-staging"), Path.Combine(batch, "backup2"))
        };
        AssertThrows<DirectoryNotFoundException>(() => ModCommand.InstallBatch(targets), "batch install surfaces replacement failure");
        Assert(File.Exists(Path.Combine(target1, "old.txt")) && File.Exists(Path.Combine(target2, "old.txt")), "batch install rolls back all replaced targets");
    }

    private async Task TestDeployAsync()
    {
        var environment = CreateProjectEnvironment("deploy", createGameExecutable: true);
        var target = NewDirectory(Path.Combine("deploy", "game", "Mods", "Demo"));
        File.WriteAllText(Path.Combine(target, "PublishedFileId.txt"), "123");
        File.WriteAllText(Path.Combine(target, "stale.txt"), "stale");
        File.WriteAllText(Path.Combine(environment.Root, "About", "About.html"), "filtered");
        File.WriteAllText(Path.Combine(environment.Root, "About", "About.txt"), "kept");
        File.WriteAllText(environment.ConfigPath, Config(environment, modName: "Demo", extra: "\"sync_items\":[\"About\"],\"protected_files_regex\":\"^(PublishedFileId\\\\.txt)$\""));
        using var scope = UseEnvironment(environment);

        var absentModsConfig = Path.Combine(environment.ConfigDirectory, "missing-ModsConfig.xml");
        File.WriteAllText(environment.ConfigPath, Config(environment, modName: "Demo", modsConfigPath: absentModsConfig, extra: "\"sync_items\":[\"About\"]"));
        var blockedByModsConfig = await DeployCommand.ExecuteAsync(["deploy", "--dry-run"]);
        Assert(!blockedByModsConfig.Ok && blockedByModsConfig.ExitCode == ExitCodes.MissingDependency && blockedByModsConfig.Error?.Code == "MODS_CONFIG_NOT_FOUND", "deploy refuses to continue when mods_config_path does not point to an existing ModsConfig.xml");

        File.WriteAllText(environment.ConfigPath, Config(environment, modName: "Demo", extra: "\"sync_items\":[\"About\"],\"protected_files_regex\":\"^(PublishedFileId\\\\.txt)$\""));
        var dryRun = await DeployCommand.ExecuteAsync(["deploy", "--dry-run"]);
        var dryResult = (DeployResult)dryRun.Result!;
        Assert(dryRun.Ok && dryResult.Plan.TargetActions.Any(x => x.Action == "protect") && dryResult.Plan.TargetActions.Any(x => x.Action == "replace") && dryResult.Plan.References.ManagedDirectory == Path.Combine(environment.Game, "RimWorldWin64_Data", "Managed") && !dryResult.Plan.SyncItems.Any(item => item.Item.Equals("source", StringComparison.OrdinalIgnoreCase)), "deploy dry-run reports protected/replaced targets, resolved build references, and disabled source synchronization");

        File.WriteAllText(environment.ConfigPath, Config(environment, modName: "Demo", extra: "\"sync_items\":[\"About\"],\"sync_source\":true,\"protected_files_regex\":\"^(PublishedFileId\\\\.txt)$\""));
        var configuredSource = await DeployCommand.ExecuteAsync(["deploy", "--dry-run"]);
        var configuredSourceResult = (DeployResult)configuredSource.Result!;
        Assert(configuredSource.Ok && configuredSourceResult.Plan.SyncItems.Any(item => item.Item.Equals("source", StringComparison.OrdinalIgnoreCase)), "deploy includes source when sync_source is enabled in project configuration");

        File.WriteAllText(environment.ConfigPath, Config(environment, modName: "Demo", extra: "\"csproj_paths\":[],\"sync_items\":[\"About\"],\"protected_files_regex\":\"^(PublishedFileId\\\\.txt)$\""));
        var pureXmlDryRun = await DeployCommand.ExecuteAsync(["deploy", "--dry-run"]);
        Assert(pureXmlDryRun.Ok && ((DeployResult)pureXmlDryRun.Result!).Plan.Projects.Count == 0, "pure XML deploy accepts an empty csproj_paths array and skips builds");

        var unknownArgument = await CaptureAsync(() => CliApplication.RunAsync(["deploy", "--dry-run", "--definitely-unknown"]));
        Assert(unknownArgument.ExitCode == ExitCodes.InvalidArguments && JsonErrorCode(unknownArgument.Output) == "INVALID_ARGUMENT", "unknown deploy options fail before execution");

        var pending = await ConfigTransaction.CreateAsync(environment.ConfigDirectory, "before-deploy", [environment.SettingsPath]);
        await pending.WriteAllTextAsync(environment.SettingsPath, "temporary");
        var deployed = await DeployCommand.ExecuteAsync(["deploy"]);
        Assert(deployed.Ok && ((DeployResult)deployed.Result!).RecoveredTransaction && ((DeployResult)deployed.Result!).BuiltProjects.Count == 0 && File.ReadAllText(environment.SettingsPath) == environment.OriginalSettings, "pure XML deploy skips builds and restores interrupted configuration before work");
        Assert(File.Exists(Path.Combine(target, "PublishedFileId.txt")) && !File.Exists(Path.Combine(target, "stale.txt")), "deploy preserves protected items and replaces stale target");
        Assert(File.Exists(Path.Combine(target, "About", "About.txt")) && !File.Exists(Path.Combine(target, "About", "About.html")), "deploy filters About html and svg files");

        var staleBackup = Path.Combine(environment.Game, "Mods", ".rimdev-backup-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        Directory.CreateDirectory(Path.Combine(staleBackup, "About"));
        File.WriteAllText(Path.Combine(staleBackup, "About", "About.xml"), "<ModMetaData><packageId>demo.current</packageId></ModMetaData>");
        var staleStaging = Path.Combine(environment.Game, "Mods", ".rimdev-staging-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        Directory.CreateDirectory(staleStaging);
        File.WriteAllText(environment.ConfigPath, Config(environment, modName: "Demo", extra: "\"csproj_paths\":[],\"sync_items\":[\"About\"],\"protected_files_regex\":\"^(PublishedFileId\\\\.txt)$\""));
        var artifactDryRun = await DeployCommand.ExecuteAsync(["deploy", "--dry-run"]);
        Assert(artifactDryRun.Ok && ((DeployResult)artifactDryRun.Result!).Plan.ArtifactActions.Any(x => x.Action == "remove") && Directory.Exists(staleBackup), "deploy dry-run reports stale artifacts without removing them");
        var artifactDeploy = await DeployCommand.ExecuteAsync(["deploy"]);
        Assert(artifactDeploy.Ok && !Directory.Exists(staleBackup) && !Directory.Exists(staleStaging) && ((DeployResult)artifactDeploy.Result!).ArtifactActions.Count(x => x.Action == "removed") == 2, "next deploy removes stale backup and staging artifacts");

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

        File.WriteAllText(environment.ConfigPath, Config(environment, modName: "Demo", extra: "\"sync_items\":[\"About\"],\"shader_bundle\":{\"enabled\":true,\"unity_exe_path\":\"missing-unity.exe\",\"unity_project_path\":\"missing-project\",\"build_method\":\"Build.Run\"}"));
        var failedShader = await DeployCommand.ExecuteAsync(["deploy"]);
        Assert(!failedShader.Ok && failedShader.Error?.Code == "SHADER_BUILD_FAILED" && File.Exists(Path.Combine(target, "still-here.txt")), "enabled shader failure prevents deployment and preserves the existing target");

        var unsafeRoot = NewDirectory(Path.Combine("deploy", "game", "Mods", "UnsafeProject"));
        Directory.CreateDirectory(Path.Combine(unsafeRoot, "About"));
        File.WriteAllText(Path.Combine(unsafeRoot, "About", "About.xml"), "<ModMetaData><name>Unsafe</name><packageId>demo.unsafe</packageId></ModMetaData>");
        var unsafeEnvironment = environment with { Root = unsafeRoot, ConfigPath = Path.Combine(unsafeRoot, "mod_config.jsonc") };
        File.WriteAllText(unsafeEnvironment.ConfigPath, Config(unsafeEnvironment, modName: "Unsafe"));
        using (UseEnvironment(unsafeEnvironment))
        {
            var unsafeDeploy = await DeployCommand.ExecuteAsync(["deploy", "--dry-run"]);
            Assert(!unsafeDeploy.Ok && unsafeDeploy.ExitCode == ExitCodes.InvalidArguments && unsafeDeploy.Error?.Code == "PROJECT_DEPLOYMENT_LOCATION_INVALID", "deploy rejects a project located inside RimWorld Mods before dry-run or mutation");

            var unsafeDoctor = await CaptureAsync(() => DoctorCommand.RunAsync(["doctor"]));
            using var unsafeDoctorJson = JsonDocument.Parse(unsafeDoctor.Output);
            var locationCheck = unsafeDoctorJson.RootElement.GetProperty("result").GetProperty("checks").EnumerateArray().Single(check => check.GetProperty("id").GetString() == "project.deployment_location");
            Assert(unsafeDoctor.ExitCode == 0 && !unsafeDoctorJson.RootElement.GetProperty("result").GetProperty("ready").GetBoolean() && locationCheck.GetProperty("status").GetString() == "invalid", "doctor marks projects inside RimWorld Mods as an invalid required location");
        }
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
        var environment = CreateProjectEnvironment("doctor", createConfigDirectory: false, createGameExecutable: true);
        File.WriteAllText(environment.ConfigPath, Config(environment));
        using var scope = UseEnvironment(environment);
        var result = await CaptureAsync(() => DoctorCommand.RunAsync(["doctor"]));
        Assert(result.ExitCode == 0 && !Directory.Exists(environment.ConfigDirectory), "doctor does not create RimWorld configuration directory");
        using var compact = JsonDocument.Parse(result.Output);
        var compactChecks = compact.RootElement.GetProperty("result").GetProperty("checks").EnumerateArray().ToArray();
        Assert(compact.RootElement.GetProperty("result").TryGetProperty("ready", out _)
            && !result.Output.Contains("project.discovery", StringComparison.Ordinal)
            && compactChecks.Any(check => check.GetProperty("id").GetString() == "game.mods_config" && check.GetProperty("status").GetString() == "missing")
            && compactChecks.Any(check => check.GetProperty("id").GetString() == "tools.mod_components" && check.GetProperty("required").GetBoolean() && check.GetProperty("status").GetString() == "missing")
            && compactChecks.Any(check => check.GetProperty("id").GetString() == "project.preview_image" && check.GetProperty("status").GetString() == "warning")
            && compactChecks.Any(check => check.GetProperty("id").GetString() == "build.game_assembly.Assembly-CSharp" && check.GetProperty("path").GetString() == Path.Combine(environment.Game, "RimWorldWin64_Data", "Managed", "Assembly-CSharp.dll"))
            && compactChecks.Any(check => check.GetProperty("id").GetString() == "build.harmony_assembly" && check.GetProperty("path").GetString() == Path.Combine(environment.Game, "Mods", "Harmony", "Current", "Assemblies", "0Harmony.dll")), "doctor returns a compact readiness summary with both critical resolved assembly paths visible by default");
        var verbose = await CaptureAsync(() => DoctorCommand.RunAsync(["doctor", "--verbose"]));
        Assert(verbose.Output.Contains("environment.transactions") && verbose.Output.Contains("build.dotnet") && verbose.Output.Contains("project.discovery"), "doctor --verbose reports the complete ordered capability list");

        var aboutRoot = NewDirectory("doctor-about-checks");
        var aboutChecks = new List<DoctorCheck>();
        DoctorCommand.AddAboutChecks(aboutChecks, aboutRoot);
        Assert(aboutChecks.Count == 2 && aboutChecks.All(check => check.Status == "warning" && !check.Required), "doctor reports missing About.xml and Preview.png as non-blocking warnings");
        Directory.CreateDirectory(Path.Combine(aboutRoot, "About"));
        File.WriteAllText(Path.Combine(aboutRoot, "About", "About.xml"), "<ModMetaData />");
        File.WriteAllBytes(Path.Combine(aboutRoot, "About", "Preview.png"), new byte[1_048_576]);
        aboutChecks.Clear();
        DoctorCommand.AddAboutChecks(aboutChecks, aboutRoot);
        Assert(aboutChecks.Single(check => check.Id == "project.about_xml").Status == "available"
            && aboutChecks.Single(check => check.Id == "project.preview_image").Status == "warning", "doctor warns when Preview.png is not smaller than 1 MiB");

        var activeEnvironment = CreateProjectEnvironment("doctor-active", createGameExecutable: true);
        using var activeScope = UseEnvironment(activeEnvironment);
        File.WriteAllText(Path.Combine(activeEnvironment.Game, "Mods", "SmartSpeed", "About", "About.xml"), "<ModMetaData><name>SmartSpeed</name><packageId>sarg.smartspeed</packageId><modDependencies><li><packageId>doctor.parent</packageId><displayName>Doctor Parent</displayName></li></modDependencies></ModMetaData>");
        CreateMod(activeEnvironment.Game, "DoctorParent", "doctor.parent");
        var dataRoot = NewDirectory("doctor-component-data");
        File.WriteAllText(
            Path.Combine(dataRoot, "tool-mods.manifest.json"),
            "{\"schemaVersion\":1,\"components\":[{\"folder\":\"LogSyncMod\",\"packageId\":\"trigger.logsyncmod\",\"version\":\"1.0.0\",\"primaryAssembly\":\"1.6/Assemblies/LogSyncMod.dll\",\"contentHash\":\"sha256:00\"}]}");
        var oldDataPath = Environment.GetEnvironmentVariable("RIMDEVTOOLS_DATA_PATH");
        try
        {
            Environment.SetEnvironmentVariable("RIMDEVTOOLS_DATA_PATH", dataRoot);
            var activeResult = await CaptureAsync(() => DoctorCommand.RunAsync(["doctor", "--verbose"]));
            using var activeJson = JsonDocument.Parse(activeResult.Output);
            var checks = activeJson.RootElement.GetProperty("result").GetProperty("checks").EnumerateArray().ToArray();
            var activeCheck = checks.Single(check => check.GetProperty("id").GetString() == "mods.min_active_enabled");
            var dependencyCheck = checks.Single(check => check.GetProperty("id").GetString() == "mods.min_active_dependencies");
            var componentCheck = checks.Single(check => check.GetProperty("id").GetString() == "tools.component.trigger.logsyncmod");
            Assert(activeCheck.GetProperty("status").GetString() == "available" && activeCheck.GetProperty("details").GetProperty("activeMods").GetArrayLength() > 0, "doctor validates the effective active mod list that test run will write");
            Assert(dependencyCheck.GetProperty("status").GetString() == "warning"
                && dependencyCheck.GetProperty("required").GetBoolean() == false
                && dependencyCheck.GetProperty("details").GetProperty("warnings")[0].GetProperty("missingDependencyPackageId").GetString() == "doctor.parent"
                && activeJson.RootElement.GetProperty("result").GetProperty("warningCount").GetInt32() > 0, "doctor reports disabled min_active_mods dependencies as non-blocking warnings");
            Assert(componentCheck.GetProperty("required").GetBoolean() && componentCheck.GetProperty("status").GetString() == "missing", "doctor treats the bundled LogSyncMod component as a required dependency");
        }
        finally { Environment.SetEnvironmentVariable("RIMDEVTOOLS_DATA_PATH", oldDataPath); }
    }

    private async Task TestRunPreflightAsync()
    {
        var environment = CreateProjectEnvironment("preflight", createGameExecutable: false);
        using var scope = UseEnvironment(environment);
        var result = await CaptureAsync(() => TestCommand.RunAsync(["test", "run", "--mode", "auto", "--timeout-seconds", "1"]));
        Assert(result.ExitCode == ExitCodes.MissingDependency && JsonErrorCode(result.Output) == "GAME_NOT_FOUND", "test run rejects missing game before mutation");
        Assert(File.ReadAllText(environment.ModsConfigPath) == environment.OriginalMods && File.ReadAllText(environment.SettingsPath) == environment.OriginalSettings, "preflight failure preserves global configuration");
        var invalidScopeMode = await CaptureAsync(() => CliApplication.RunAsync(["test", "run", "--mode", "quick", "--scope", "turrets"]));
        var invalidDefMode = await CaptureAsync(() => CliApplication.RunAsync(["test", "run", "--mode", "xml", "--defname", "Demo_Coat"]));
        var invalidScreenshotMode = await CaptureAsync(() => CliApplication.RunAsync(["test", "run", "--mode", "quick", "--capture-screenshots"]));
        Assert(invalidScopeMode.ExitCode == ExitCodes.InvalidArguments && JsonErrorCode(invalidScopeMode.Output) == "INVALID_ARGUMENT", "quick/manual scope misuse returns stable INVALID_ARGUMENT");
        Assert(invalidDefMode.ExitCode == ExitCodes.InvalidArguments && JsonErrorCode(invalidDefMode.Output) == "INVALID_ARGUMENT", "non-auto DefName misuse returns stable INVALID_ARGUMENT");
        Assert(invalidScreenshotMode.ExitCode == ExitCodes.InvalidArguments && JsonErrorCode(invalidScreenshotMode.Output) == "INVALID_ARGUMENT", "non-auto screenshot misuse returns stable INVALID_ARGUMENT");

        var steamBlocked = CreateProjectEnvironment("steam-blocked", createGameExecutable: true);
        using var blockedScope = UseEnvironment(steamBlocked);
        SteamSessionGuard.ProbeOverride = () => new SteamSessionStatus(true, false, "test", "test\\loginusers.vdf", "STEAM_NOT_LOGGED_IN", "测试 Steam 未登录");
        var blocked = await CaptureAsync(() => TestCommand.RunAsync(["test", "run", "--mode", "quick", "--timeout-seconds", "1"]));
        Assert(blocked.ExitCode == ExitCodes.GameFailed && JsonErrorCode(blocked.Output) == "STEAM_NOT_LOGGED_IN", "test run rejects an unlogged Steam session before mutation");
        Assert(File.ReadAllText(steamBlocked.ModsConfigPath) == steamBlocked.OriginalMods && File.ReadAllText(steamBlocked.SettingsPath) == steamBlocked.OriginalSettings, "Steam login rejection preserves global configuration");
    }

    private async Task TestGameWorkflowsAsync()
    {
        var environment = CreateProjectEnvironment("game-workflows", createGameExecutable: true);
        using var scope = UseEnvironment(environment);
        File.WriteAllText(Path.Combine(environment.Root, "About", "About.xml"), "<ModMetaData><name>Current</name><packageId>demo.current</packageId><modDependencies><li><packageId>demo.parent</packageId><displayName>Demo Parent</displayName></li></modDependencies></ModMetaData>");
        CreateMod(environment.Game, "DemoParent", "demo.parent");
        var callerDesktop = GameProcessLauncher.CurrentDesktopName();

        Environment.SetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE", "quick");
        var quick = await CaptureAsync(() => TestCommand.RunAsync(["test", "run", "--mode", "quick", "--timeout-seconds", "5"]));
        var isolatedDesktop = File.ReadAllText(Path.Combine(environment.ConfigDirectory, "fake-desktop.txt"));
        var quickSettings = XDocument.Parse(File.ReadAllText(Path.Combine(environment.ConfigDirectory, "fake-settings.xml")));
        Assert(quick.ExitCode == 0 && quick.Output.Contains("\"isolatedDesktop\": false") && !File.ReadAllText(Path.Combine(environment.ConfigDirectory, "fake-args.txt")).Contains("quicktest"), "quick mode waits for ready signal on the normal desktop by default");
        using (var quickJson = JsonDocument.Parse(quick.Output))
        {
            Assert(quickJson.RootElement.GetProperty("warnings").GetArrayLength() == 1
                && quickJson.RootElement.GetProperty("warnings")[0].GetString()!.Contains("demo.parent", StringComparison.OrdinalIgnoreCase), "test run emits a dependency warning and continues execution");
        }
        Assert(string.Equals(isolatedDesktop, callerDesktop, StringComparison.Ordinal), "default quick process runs on the current desktop");
        Assert(quickSettings.Descendants("testMuteAudio").Single().Value == "True", "quick mode enables transactional audio muting");
        AssertConfigurationRestored(environment, "quick mode restores exact configuration");

        Environment.SetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE", "quick");
        var visibleQuick = await CaptureAsync(() => TestCommand.RunAsync(["test", "run", "--mode", "quick", "--no-isolated-desktop", "--timeout-seconds", "5"]));
        Assert(visibleQuick.ExitCode == 0 && visibleQuick.Output.Contains("\"isolatedDesktop\": false") && File.ReadAllText(Path.Combine(environment.ConfigDirectory, "fake-desktop.txt")) == callerDesktop, "quick CLI override uses the current desktop");

        var conflictingIsolation = await CaptureAsync(() => TestCommand.RunAsync(["test", "run", "--isolated-desktop", "--no-isolated-desktop"]));
        Assert(conflictingIsolation.ExitCode == ExitCodes.InvalidArguments && JsonErrorCode(conflictingIsolation.Output) == "ISOLATED_DESKTOP_OPTIONS_CONFLICT", "conflicting isolation options fail before test mutation");

        Environment.SetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE", "quick-split");
        var splitSignal = await CaptureAsync(() => TestCommand.RunAsync(["test", "run", "--mode", "quick", "--timeout-seconds", "5"]));
        Assert(splitSignal.ExitCode == 0, "quick mode recognizes a ready signal split across log reads");

        Environment.SetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE", "quick-large-log");
        var largeQuick = await CaptureAsync(() => TestCommand.RunAsync(["test", "run", "--mode", "quick", "--timeout-seconds", "5"]));
        Assert(largeQuick.ExitCode == 0, "quick mode recognizes a signal before more than 32 KiB of trailing log output");

        Environment.SetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE", "auto");
        var autoOutput = Path.Combine(environment.Root, ".tools", "log", "AutoTestOutput");
        Directory.CreateDirectory(autoOutput);
        WriteResult(autoOutput, "demo.current", "old-one", "passed", "old-one_Result.json");
        WriteResult(autoOutput, "demo.current", "old-two", "passed", "old-two_Result.json");
        WriteResult(autoOutput, "demo.current", "old-three", "passed", "old-three_Result.json");
        var auto = await CaptureAsync(() => TestCommand.RunAsync(["test", "run", "--mode", "auto", "--timeout-seconds", "5"]));
        var observedSettings = File.ReadAllText(Path.Combine(environment.ConfigDirectory, "fake-settings.xml"));
        Assert(auto.ExitCode == 0 && File.ReadAllText(Path.Combine(environment.ConfigDirectory, "fake-args.txt")).Contains("quicktest"), "auto mode launches with quicktest and validates result");
        Assert(observedSettings.Contains("demo.current") && observedSettings.Contains("AutoTestOutput"), "auto mode passes target package and output path to LogSync");
        Assert(XDocument.Parse(observedSettings).Descendants("testMuteAudio").Single().Value == "True", "auto mode enables transactional audio muting");
        Assert(Directory.EnumerateFiles(autoOutput, "*_Result.json").Count() == 2, "configured auto-test retention is enforced after a run");
        AssertConfigurationRestored(environment, "auto mode restores settings changed by the game");

        Environment.SetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE", "auto");
        var selected = await CaptureAsync(() => TestCommand.RunAsync(["test", "run", "--mode", "auto", "--scope", "turrets", "--scope", "threeviews", "--scope", "TURRETS", "--defname", "Demo_Coat", "--defname", "demo_coat", "--timeout-seconds", "5"]));
        var selectedSettings = XDocument.Parse(File.ReadAllText(Path.Combine(environment.ConfigDirectory, "fake-settings.xml"))).Descendants("ModSettings").Single();
        Assert(selected.ExitCode == 0, "selected auto run succeeds: " + selected.Output);
        using (var selectedJson = JsonDocument.Parse(selected.Output))
        {
            var selectedResult = selectedJson.RootElement.GetProperty("result");
            Assert(selected.ExitCode == 0
                && selectedResult.GetProperty("scopes").EnumerateArray().Select(x => x.GetString()).SequenceEqual(["Turrets", "ThreeViews"])
                && selectedResult.GetProperty("defNames").EnumerateArray().Select(x => x.GetString()).SequenceEqual(["Demo_Coat"]), "auto success JSON reports normalized scopes and DefNames");
        }
        Assert(selectedSettings.Element("autoTestScope")?.Value == "Turrets"
            && selectedSettings.Element("autoTestScopes")?.Elements("li").Select(x => x.Value).SequenceEqual(["Turrets", "ThreeViews"]) == true
            && selectedSettings.Element("autoTestTargetDefNames")?.Elements("li").Select(x => x.Value).SequenceEqual(["Demo_Coat"]) == true, "auto run writes multi-scope and precise target settings for the game");
        AssertConfigurationRestored(environment, "selected auto mode restores all new settings fields");

        Environment.SetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE", "auto-large-log");
        var largeAuto = await CaptureAsync(() => TestCommand.RunAsync(["test", "run", "--mode", "auto", "--timeout-seconds", "5"]));
        Assert(largeAuto.ExitCode == 0, "auto mode recognizes a completion signal before more than 32 KiB of trailing log output");

        Environment.SetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE", "auto-fail");
        var failed = await CaptureAsync(() => TestCommand.RunAsync(["test", "run", "--mode", "xml", "--timeout-seconds", "5"]));
        Assert(failed.ExitCode == ExitCodes.GameFailed && JsonErrorCode(failed.Output) == "AUTO_RESULT_FAILED", "xml mode rejects failed auto-test JSON");
        AssertConfigurationRestored(environment, "failed auto test restores configuration");

        Environment.SetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE", "manual");
        var manual = await CaptureAsync(() => TestCommand.RunAsync(["test", "run", "--mode", "manual", "--timeout-seconds", "5"]));
        var manualSettings = XDocument.Parse(File.ReadAllText(Path.Combine(environment.ConfigDirectory, "fake-settings.xml")));
        Assert(manual.ExitCode == 0 && manual.Output.Contains("\"isolatedDesktop\": false") && File.ReadAllText(Path.Combine(environment.ConfigDirectory, "fake-desktop.txt")) == callerDesktop, "manual mode defaults to the current desktop despite project configuration");
        Assert(manualSettings.Descendants("testMuteAudio").Single().Value == "False", "manual mode leaves test audio muting disabled");

        Environment.SetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE", "manual");
        var isolatedManual = await CaptureAsync(() => TestCommand.RunAsync(["test", "run", "--mode", "manual", "--isolated-desktop", "--timeout-seconds", "5"]));
        Assert(isolatedManual.ExitCode == 0 && isolatedManual.Output.Contains("\"isolatedDesktop\": true") && File.ReadAllText(Path.Combine(environment.ConfigDirectory, "fake-desktop.txt")).StartsWith("RimDevTools.", StringComparison.Ordinal), "manual mode supports an explicit isolated desktop override");

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

        var missingSettings = CreateProjectEnvironment("game-missing-settings", createGameExecutable: true, createSettings: false);
        using var missingScope = UseEnvironment(missingSettings);
        Environment.SetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE", "quick");
        var missingRun = await CaptureAsync(() => TestCommand.RunAsync(["test", "run", "--mode", "quick", "--timeout-seconds", "5"]));
        Assert(missingRun.ExitCode == 0 && !File.Exists(missingSettings.SettingsPath), "test run temporarily creates and then removes an originally missing settings file");
    }

    private ProjectEnvironment CreateProjectEnvironment(string name, bool createConfigDirectory = true, bool createGameExecutable = false, bool createSettings = true)
    {
        var root = NewDirectory(name);
        var game = NewDirectory(Path.Combine(name, "game"));
        var config = Path.Combine(_temp, name, "Config");
        if (createConfigDirectory) Directory.CreateDirectory(config);
        Directory.CreateDirectory(Path.Combine(root, "About"));
        File.WriteAllText(Path.Combine(root, "About", "About.xml"), "<ModMetaData><name>Current</name><packageId>demo.current</packageId></ModMetaData>");
        var projectDirectory = Path.Combine(root, "source", "TestProject");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "TestProject.csproj"), "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\"><PropertyGroup><OutputType>Library</OutputType><TargetFrameworkVersion>v4.8</TargetFrameworkVersion><AssemblyName>TestProject</AssemblyName><OutputPath>bin\\</OutputPath></PropertyGroup><Import Project=\"$(MSBuildToolsPath)\\Microsoft.CSharp.targets\" /></Project>");
        var harmony = CreateMod(game, "Harmony", "brrainz.harmony");
        Directory.CreateDirectory(Path.Combine(harmony, "Current", "Assemblies"));
        File.WriteAllText(Path.Combine(harmony, "Current", "Assemblies", "0Harmony.dll"), "harmony");
        File.WriteAllText(Path.Combine(harmony, "LoadFolders.xml"), "<loadFolders><v1.6><li>/</li><li>Current</li></v1.6></loadFolders>");
        CreateMod(game, "SmartSpeed", "sarg.smartspeed");
        CreateMod(game, "CameraPlus", "brrainz.cameraplus");
        CreateMod(game, "HumanoidAlienRaces", "erdelf.humanoidalienraces");
        CreateMod(game, "LogSync", "trigger.logsyncmod");

        var settings = Path.Combine(config, "Mod_LogSyncMod_LogSyncMod.xml");
        var mods = Path.Combine(config, "ModsConfig.xml");
        const string originalSettings = "<Root><ModSettings><logPath>old</logPath><keywords><li>Old</li></keywords><autoTestEnabled>False</autoTestEnabled></ModSettings></Root>";
        const string originalMods = "<ModsConfigData><activeMods><li>ludeon.rimworld</li></activeMods></ModsConfigData>";
        if (createConfigDirectory)
        {
            if (createSettings) File.WriteAllText(settings, originalSettings);
            File.WriteAllText(mods, originalMods);
        }

        var environment = new ProjectEnvironment(root, game, config, Path.Combine(root, "mod_config.jsonc"), settings, mods, originalSettings, originalMods);
        File.WriteAllText(environment.ConfigPath, Config(environment));
        if (createGameExecutable) InstallFakeGame(environment.Game);
        return environment;
    }

    private void InstallFakeGame(string gameDirectory)
    {
        CreateGameStructure(gameDirectory);
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

    private static void CreateGameStructure(string gameDirectory)
    {
        Directory.CreateDirectory(gameDirectory);
        Directory.CreateDirectory(Path.Combine(gameDirectory, "Mods"));
        Directory.CreateDirectory(Path.Combine(gameDirectory, "Data"));
        var managed = Path.Combine(gameDirectory, "RimWorldWin64_Data", "Managed");
        Directory.CreateDirectory(managed);
        if (!File.Exists(Path.Combine(gameDirectory, "RimWorldWin64.exe"))) File.WriteAllText(Path.Combine(gameDirectory, "RimWorldWin64.exe"), "game");
        foreach (var assembly in RimWorldEnvironmentResolver.RequiredManagedAssemblies)
            if (!File.Exists(Path.Combine(managed, assembly))) File.WriteAllText(Path.Combine(managed, assembly), "assembly");
    }

    private static void CopyTestDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var directory in Directory.EnumerateDirectories(source)) CopyTestDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private string Config(ProjectEnvironment environment, string modName = "Demo", string minMods = "[\"brrainz.harmony\",\"sarg.smartspeed\",\"brrainz.cameraplus\",\"erdelf.humanoidalienraces\",\"trigger.logsyncmod\"]", string? modsConfigPath = null, string? extra = null)
    {
        var members = new List<string>
        {
            "\"config_version\":2",
            $"\"mod_name\":\"{modName}\"",
            "\"game_version\":\"1.6\"",
            $"\"rimworld_dir\":\"{Escape(environment.Game)}\"",
            $"\"mods_config_path\":\"{Escape(modsConfigPath ?? environment.ModsConfigPath)}\"",
            "\"csproj_paths\":[\"source\\\\TestProject\\\\TestProject.csproj\"]",
            $"\"log_keywords\":[\"{modName}\"]",
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
        var oldSteamProbe = SteamSessionGuard.ProbeOverride;
        Environment.CurrentDirectory = environment.Root;
        Environment.SetEnvironmentVariable("RIMDEVTOOLS_CONFIG_DIR", environment.ConfigDirectory);
        SteamSessionGuard.ProbeOverride = () => new SteamSessionStatus(true, true, "test", null, "STEAM_LOGGED_IN", "测试 Steam 会话已登录");
        return new ActionScope(() =>
        {
            Environment.CurrentDirectory = oldDirectory;
            Environment.SetEnvironmentVariable("RIMDEVTOOLS_CONFIG_DIR", oldConfig);
            Environment.SetEnvironmentVariable("RIMDEVTOOLS_FAKE_MODE", null);
            SteamSessionGuard.ProbeOverride = oldSteamProbe;
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
