using System.Diagnostics;
using System.Text.Json;
using RimDevTools;

internal static partial class ToolUpdateTests
{
    internal static async Task<int> RunLiveAsync(string temporary)
    {
        _passed = 0;
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var package = Path.Combine(repository, "artifacts", "RimDevTools-" + CurrentVersion + "-Tools");
        if (!File.Exists(Path.Combine(package, ToolPackageManifest.RelativePath))) throw new InvalidOperationException("请先运行默认三包发布，再执行 --tool-update-live");
        var future = Path.Combine(temporary, "future-publish");
        var build = await RunProcess("dotnet", repository, "publish", Path.Combine(repository, "RimDevTools", "RimDevTools.csproj"), "-c", "Release", "-r", "win-x64", "--self-contained", "false", "-p:PublishSingleFile=true", "-p:Version=1.3.1", "-o", future);
        Assert(build.ExitCode == 0, "future-version single-file EXE publishes for real update verification: " + (build.ExitCode == 0 ? "passed" : build.Output));
        var targetRoot = Path.Combine(temporary, "future-package");
        CopyPackage(package, targetRoot);
        File.Copy(Path.Combine(future, "RimDevTools.exe"), Path.Combine(targetRoot, ".tools", "RimDevTools.exe"), true);
        File.AppendAllText(Path.Combine(targetRoot, ".agents/skills/rimdevtools/SKILL.md"), "\nFuture release skill.\n");
        File.WriteAllText(Path.Combine(targetRoot, ".agents/skills/rimdevtools/new-managed.md"), "new managed file");
        RefreshManifest(targetRoot, FutureVersion);
        var zip = CreateArchive(targetRoot, Path.Combine(temporary, "future.zip"));
        var release = Release(zip);
        foreach (var mode in new[] { "true", "false", "missing" })
        {
            var installation = LiveInstallation(package, Path.Combine(temporary, "success-" + mode), mode);
            var plan = Prepare(installation, zip, release);
            var ignored = Path.Combine(installation.Root, ".gitignore");
            var note = Path.Combine(installation.Root, ".agents/skills/rimdevtools/local-note.md");
            File.WriteAllText(ignored, "user ignored content");
            File.WriteAllText(note, "user added note");
            var projectFiles = new[] { "README.md", "source/Legacy.cs", "About/About.xml", "About/PublishedFileId.txt", "Defs/Legacy.xml", "Languages/English/Keyed/Legacy.xml", ".mcp.json", ".agents/skills/custom/SKILL.md" };
            foreach (var path in projectFiles)
            {
                var destination = Path.Combine(installation.Root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllText(destination, "user project content: " + path);
            }
            File.WriteAllText(Path.Combine(installation.Root, ".tools/Data/project-template.manifest.json"), "original template identity");
            var oldExeHash = ToolPackageManifest.Hash(installation.Executable);
            using var oldProcess = HoldOldExecutable(installation);
            var oldOutput = oldProcess.StandardOutput.ReadToEndAsync();
            var oldError = oldProcess.StandardError.ReadToEndAsync();
            var state = await ToolUpdateTransaction.ScheduleAsync(installation, plan, CancellationToken.None, oldProcess);
            Assert(state.Status == "scheduled" && ToolPackageManifest.Hash(installation.Executable) == oldExeHash, "helper confirms scheduled without replacing a still-running mapped EXE (mode " + mode + ")");
            using (var business = installation.EnterBusiness(out var error))
                Assert(business is null && error?.Code == "TOOL_UPDATE_RECOVERY_REQUIRED", "persistent handoff blocks new commands while parent is alive");
            var blocked = await RunProcess(installation.Executable, temporary, "deploy", "--dry-run");
            Assert(blocked.ExitCode == ExitCodes.Busy && blocked.Output.Contains("TOOL_UPDATE_RECOVERY_REQUIRED"), "a separate real CLI process observes the persistent handoff gate");
            var stateBeforeRecovery = File.ReadAllBytes(installation.StatePath);
            var earlyRecovery = await RunProcess(Path.Combine(plan.DirectoryPath, "helper", "RimDevTools.UpdateHelper.exe"), temporary, "--internal-update-recover", plan.JournalPath);
            Assert(earlyRecovery.ExitCode == ExitCodes.Busy && File.ReadAllBytes(installation.StatePath).SequenceEqual(stateBeforeRecovery), "recovery refuses a live parent without altering the scheduled transaction");
            await oldProcess.StandardInput.WriteLineAsync("{}");
            oldProcess.StandardInput.Close();
            using (var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15))) await oldProcess.WaitForExitAsync(exitTimeout.Token);
            _ = await oldOutput;
            _ = await oldError;
            var final = await WaitTerminal(installation);
            Assert(final.Status == "succeeded", "independent helper updates and verifies the real Windows EXE: " + (final.Error ?? mode));
            Assert(await ToolUpdateTransaction.ReadVersionAsync(installation.Executable, installation.Root) == FutureVersion, "updated EXE actually runs at the target assembly version");
            Assert(File.ReadAllText(ignored) == "user ignored content" && File.ReadAllText(note) == "user added note"
                && projectFiles.All(path => File.ReadAllText(Path.Combine(installation.Root, path)) == "user project content: " + path)
                && File.ReadAllText(Path.Combine(installation.Root, ".tools/Data/project-template.manifest.json")) == "original template identity", "update preserves project code, resources, Workshop identity, other skills, user files and original template metadata");
            Assert(!File.Exists(Path.Combine(installation.Root, ".agents/skills/rimdevtools/retired-managed.md")) && File.Exists(Path.Combine(installation.Root, ".agents/skills/rimdevtools/new-managed.md")), "update adds new managed files and removes retired baseline files");
            var config = ProjectConfig.Load(installation.Config);
            Assert(config.GameVersion == (mode == "false" ? "1.7" : "1.6") && config.ExistingProjectMode == (mode == "true") && File.ReadAllText(installation.Config).Contains("retained comment"), "update preserves explicit modes, both game targets or supplies false only when missing");
            var query = await RunProcess(installation.Executable, temporary, "update", "status");
            using var json = JsonDocument.Parse(query.Output.TrimStart('\uFEFF'));
            Assert(query.ExitCode == 0 && json.RootElement.GetProperty("result").GetProperty("status").GetString() == "succeeded", "final status works independently of the caller working directory");
        }

        // A valid package with an unusable executable must roll back after installation verification.
        var brokenTarget = Path.Combine(temporary, "broken-target");
        CopyPackage(targetRoot, brokenTarget);
        File.WriteAllText(Path.Combine(brokenTarget, ".tools/RimDevTools.exe"), "not an executable");
        RefreshManifest(brokenTarget, FutureVersion);
        var brokenZip = CreateArchive(brokenTarget, Path.Combine(temporary, "broken.zip"));
        var brokenRelease = Release(brokenZip);
        var brokenInstallation = LiveInstallation(package, Path.Combine(temporary, "broken-exe"), "true");
        var brokenPlan = Prepare(brokenInstallation, brokenZip, brokenRelease);
        await ToolUpdateTransaction.ApplyAsync(brokenInstallation, brokenPlan, new UpdateState());
        Assert(brokenInstallation.ReadState()?.Status == "rolledBack" && await ToolUpdateTransaction.ReadVersionAsync(brokenInstallation.Executable, brokenInstallation.Root) == CurrentVersion, "unlaunchable replacement EXE rolls back to a working original");

        // Terminate a real worker after a persisted write intent; the retained standalone helper recovers.
        foreach (var point in new[] { "first", "exe", "last" })
        {
            var installation = LiveInstallation(package, Path.Combine(temporary, "crash-" + point), "missing");
            var plan = Prepare(installation, brokenZip, brokenRelease);
            plan.Save();
            var helper = Path.Combine(plan.DirectoryPath, "helper", "RimDevTools.UpdateHelper.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(helper)!);
            File.Copy(installation.Executable, helper);
            installation.WriteState(new UpdateState { Status = "scheduled", TransactionId = plan.TransactionId, TransactionDirectory = plan.DirectoryPath,
                CurrentVersion = CurrentVersion, TargetVersion = FutureVersion, RecoveryCommand = $"\"{helper}\" --internal-update-recover \"{plan.JournalPath}\"" });
            var failAt = point == "first" ? 1 : point == "exe" ? plan.Changes.FindIndex(c => c.Path == ".tools/RimDevTools.exe") + 1 : plan.Changes.Count;
            var worker = await RunProcess("dotnet", installation.Root, Path.Combine(AppContext.BaseDirectory, "RimDevTools.Tests.dll"), "--tool-update-crash", plan.JournalPath, failAt.ToString());
            Assert(worker.ExitCode == 101 && installation.ReadState()?.Status == "applying", "hard termination leaves a durable applying transaction at " + point);
            var pending = JsonSerializer.Deserialize<ToolUpdatePlan>(File.ReadAllText(plan.JournalPath), JsonDefaults.Storage)!;
            Assert(pending.Changes.Count(c => c.Applied) == failAt, "write intents survive process termination");
            var recovered = await RunProcess(helper, temporary, "--internal-update-recover", plan.JournalPath);
            Assert(recovered.ExitCode == 0 && installation.ReadState()?.Status == "rolledBack"
                && await ToolUpdateTransaction.ReadVersionAsync(installation.Executable, installation.Root) == CurrentVersion, "retained helper restores a complete working installation after crash at " + point);
            ToolPackageManifest.Load(installation.Root).VerifyFiles(installation.Root);
        }
        Console.WriteLine($"PASS {_passed} Windows update integration checks");
        return _passed;
    }

    internal static async Task RunCrashWorkerAsync(string journalPath, int failAt)
    {
        var plan = JsonSerializer.Deserialize<ToolUpdatePlan>(File.ReadAllText(journalPath), JsonDefaults.Storage)!;
        var installation = new UpdateInstallation(plan.Root);
        var state = installation.ReadState()!;
        using var process = Process.GetCurrentProcess();
        state.HelperPid = process.Id;
        state.HelperStartedUtcTicks = process.StartTime.ToUniversalTime().Ticks;
        installation.WriteState(state);
        ToolUpdateTransaction.AfterWriteOverride = index => { if (index == failAt) Environment.Exit(101); };
        await ToolUpdateTransaction.ApplyAsync(installation, plan, state);
        throw new InvalidOperationException("worker did not reach the requested interruption point");
    }

    private static UpdateInstallation LiveInstallation(string package, string root, string mode)
    {
        CopyPackage(package, root);
        File.WriteAllText(Path.Combine(root, ".agents/skills/rimdevtools/retired-managed.md"), "retired official file");
        RefreshManifest(root, CurrentVersion);
        var field = mode == "missing" ? "" : "\"existing_project_mode\":" + mode + ",";
        var game = mode == "false" ? "1.7" : "1.6";
        File.WriteAllText(Path.Combine(root, "mod_config.jsonc"), "{\r\n // retained comment\r\n \"config_version\":2," + field + "\"game_version\":\"" + game + "\",\"unknown\":{\"zero\":0,\"null\":null}}\r\n");
        return new(root);
    }

    private static void CopyPackage(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static void RefreshManifest(string root, string version)
    {
        var manifest = ToolPackageManifest.Load(root);
        manifest.ToolVersion = version;
        manifest.Files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(ToolPackageManifest.IsManagedPath).OrderBy(p => p, StringComparer.Ordinal).Select(path => new ToolPackageFile(path, new FileInfo(Path.Combine(root, path)).Length, ToolPackageManifest.Hash(Path.Combine(root, path)), "component", "replace")).ToList();
        AtomicFile.WriteAllText(Path.Combine(root, ToolPackageManifest.RelativePath), JsonSerializer.Serialize(manifest, JsonDefaults.Storage));
    }

    private static Process HoldOldExecutable(UpdateInstallation installation)
    {
        var start = new ProcessStartInfo(installation.Executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = installation.Root, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--steamworkshops-helper");
        return Process.Start(start)!;
    }

    private static async Task<UpdateState> WaitTerminal(UpdateInstallation installation)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(30))
        {
            var state = installation.ReadState();
            if (state is not null && !state.BlocksBusiness) return state;
            if (state?.Status == "recoveryRequired") return state;
            await Task.Delay(50);
        }
        throw new TimeoutException("update helper did not reach a terminal state: " + File.ReadAllText(installation.StatePath));
    }

    private static async Task<(int ExitCode, string Output)> RunProcess(string executable, string directory, params string[] args)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in args) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        var stdout = await output;
        var stderr = await error;
        return (process.ExitCode, process.ExitCode == 0 ? stdout : stdout + "\n" + stderr);
    }
}
