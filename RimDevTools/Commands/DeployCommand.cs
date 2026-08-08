using System.Diagnostics;
using System.Text.RegularExpressions;

namespace RimDevTools;

public sealed record DeployExecution(bool Ok, int ExitCode, object? Result = null, CliError? Error = null, IReadOnlyList<string>? Warnings = null);

public static class DeployCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var execution = await ExecuteAsync(args, cancellationToken);
        if (!execution.Ok)
            return CliResult.WriteFailure("deploy", execution.ExitCode, execution.Error!.Code, execution.Error.Message, execution.Result is DeployContextDetails details ? details.ProjectRoot : null, execution.Error.Details);

        var projectRoot = execution.Result is DeployResult result ? result.ProjectRoot : null;
        return CliResult.WriteSuccess("deploy", projectRoot, execution.Result, execution.Warnings);
    }

    public static async Task<DeployExecution> ExecuteAsync(string[] args, CancellationToken cancellationToken = default, bool environmentPrepared = false)
    {
        var context = ProjectDiscovery.Find(Environment.CurrentDirectory, out var discoveryError);
        if (context is null)
        {
            var exitCode = discoveryError?.Code == "PROJECT_NOT_FOUND" ? ExitCodes.MissingDependency : ExitCodes.InvalidArguments;
            return Failure(exitCode, discoveryError ?? new CliError("PROJECT_NOT_FOUND", "未找到项目"));
        }

        var config = context.Config;
        var templateError = ProjectTemplatePreflight.Validate(context);
        if (templateError is not null) return Failure(ExitCodes.InvalidArguments, templateError, context.Root);
        if (config.ConfigVersion != 2)
            return Failure(ExitCodes.InvalidArguments, new CliError("CONFIG_VERSION_UNSUPPORTED", $"需要 config_version=2，当前为 {config.ConfigVersion}"), context.Root);
        if (string.IsNullOrWhiteSpace(config.ModName))
            return Failure(ExitCodes.InvalidArguments, new CliError("CONFIG_MISSING", "deploy 需要 mod_name"), context.Root);
        var environment = RimWorldEnvironmentResolver.Resolve(context);
        var rimWorldDirectory = environment.References.RimWorldDirectory;
        if (rimWorldDirectory is null)
            return Failure(ExitCodes.MissingDependency, new CliError("RIMWORLD_DIR_NOT_FOUND", "未能从配置或 Steam 安装中解析结构完整的 RimWorld 目录", new { configured = config.RimWorldDir, diagnostics = environment.Steam.Diagnostics }), context.Root);
        var locationError = ProjectLocationGuard.CreateDeploymentError(context.Root, rimWorldDirectory, environment.Steam.WorkshopPaths);
        if (locationError is not null)
            return Failure(ExitCodes.InvalidArguments, locationError, context.Root);

        var targetRoot = Path.GetFullPath(Path.Combine(rimWorldDirectory, "Mods"));
        var target = Path.GetFullPath(Path.Combine(targetRoot, config.ModName));
        if (!PathSafety.IsWithin(target, targetRoot))
            return Failure(ExitCodes.InvalidArguments, new CliError("DEPLOY_TARGET_OUTSIDE_MODS", "部署目标不在 RimWorld Mods 目录内", new { target, targetRoot }), context.Root);
        try { PathSafety.EnsureNoReparsePoints(rimWorldDirectory, target); }
        catch (Exception ex) { return Failure(ExitCodes.InvalidArguments, new CliError("DEPLOY_TARGET_UNSAFE", ex.Message), context.Root); }

        Regex? protectedRegex;
        try
        {
            protectedRegex = string.IsNullOrWhiteSpace(config.ProtectedFilesRegex)
                ? null
                : new Regex(config.ProtectedFilesRegex, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        catch (Exception ex)
        {
            return Failure(ExitCodes.InvalidArguments, new CliError("PROTECTED_REGEX_INVALID", ex.Message), context.Root);
        }

        var syncItems = config.SyncItems.ToList();
        if ((config.SyncSource || CliArgs.Has(args, "--sync-source")) && !syncItems.Contains("source", StringComparer.OrdinalIgnoreCase)) syncItems.Add("source");
        if (syncItems.Count == 0)
            return Failure(ExitCodes.InvalidArguments, new CliError("SYNC_ITEMS_EMPTY", "deploy 至少需要一个 sync_items"), context.Root);

        var warnings = new List<string>();
        var plan = new List<DeploySyncPlan>();
        try
        {
            foreach (var item in syncItems)
            {
                var source = Path.GetFullPath(item, context.Root);
                if (!PathSafety.IsWithin(source, context.Root)) throw new InvalidDataException($"同步源越出项目目录: {item}");
                PathSafety.EnsureNoReparsePoints(context.Root, source);
                var destination = Path.GetFullPath(Path.Combine(target, item));
                if (!PathSafety.IsWithin(destination, target)) throw new InvalidDataException($"同步目标越出部署目录: {item}");
                var exists = File.Exists(source) || Directory.Exists(source);
                var severity = exists ? "info" : IsOptionalBuildOutput(item) ? "warning" : "error";
                plan.Add(new DeploySyncPlan(item, source, destination, exists, severity));
                if (!exists) warnings.Add($"同步项不存在: {item}");
            }
        }
        catch (Exception ex)
        {
            return Failure(ExitCodes.InvalidArguments, new CliError("SYNC_PATH_INVALID", ex.Message), context.Root);
        }

        var projectFiles = new List<string>();
        foreach (var project in config.CsprojPaths)
        {
            var path = Path.GetFullPath(project, context.Root);
            if (!PathSafety.IsWithin(path, context.Root))
                return Failure(ExitCodes.InvalidArguments, new CliError("CSPROJ_OUTSIDE_PROJECT", $"工程路径越出项目目录: {project}"), context.Root);
            if (!File.Exists(path))
                return Failure(ExitCodes.MissingDependency, new CliError("CSPROJ_NOT_FOUND", $"工程文件不存在: {project}"), context.Root);
            projectFiles.Add(path);
        }

        var targetActions = Directory.Exists(target)
            ? Directory.EnumerateFileSystemEntries(target).Select(path => new DeployTargetAction(
                path,
                protectedRegex?.IsMatch(Path.GetFileName(path)) == true ? "protect" : "replace")).ToArray()
            : [];
        var referencePlan = new BuildReferencePlan(environment.References.RimWorldDirectory, environment.References.ManagedDirectory, environment.References.HarmonyDllPath);
        var preview = new DeployPlan(
            context.Root,
            target,
            new ShaderPlan(config.ShaderBundle.Enabled, config.ShaderBundle.UnityExePath, config.ShaderBundle.UnityProjectPath, config.ShaderBundle.BuildMethod),
            referencePlan,
            projectFiles,
            plan,
            targetActions,
            config.ProtectedFilesRegex);

        if (CliArgs.Has(args, "--dry-run"))
            return new DeployExecution(true, ExitCodes.Success, new DeployResult(context.Root, true, null, target, [], preview, false), null, warnings);

        if (projectFiles.Count > 0)
        {
            var missingReferences = environment.References.RequiredGameAssemblies
                .Where(path => !File.Exists(path))
                .Concat(environment.References.HarmonyDllPath is null ? ["0Harmony.dll"] : File.Exists(environment.References.HarmonyDllPath) ? [] : [environment.References.HarmonyDllPath])
                .ToArray();
            if (environment.References.ManagedDirectory is null || missingReferences.Length > 0)
                return Failure(ExitCodes.MissingDependency, new CliError("BUILD_REFERENCES_NOT_FOUND", "构建所需的真实 RimWorld/Harmony 引用不完整", new { referencePlan, missingReferences }), context.Root);
        }

        var missingRequired = plan.Where(x => !x.Exists && x.Severity == "error").Select(x => x.Item).ToArray();
        if (missingRequired.Length > 0)
            return Failure(ExitCodes.MissingDependency, new CliError("SYNC_ITEM_NOT_FOUND", "必需同步项不存在", new { missing = missingRequired }), context.Root);

        var runId = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(targetRoot);
        string? busyInfo = null;
        using var gate = environmentPrepared ? null : EnvironmentLock.TryAcquire(GameEnvironment.ConfigDirectory(), "deploy", context.Root, runId, out busyInfo);
        if (!environmentPrepared && gate is null)
            return Failure(ExitCodes.Busy, new CliError("ENVIRONMENT_BUSY", "RimWorld 环境正被另一个 RimDevTools 进程占用", new { busyInfo }), context.Root);
        var recovery = new TransactionRecoveryResult(true, false);
        if (!environmentPrepared)
        {
            if (!await ProcessUtil.WaitForRimWorldExitAsync(config.Timeouts.ExistingGameExit, config.Timeouts.PostExitDelay, cancellationToken))
                return Failure(ExitCodes.Timeout, new CliError("GAME_EXIT_TIMEOUT", "等待 RimWorld 退出超时"), context.Root);

            recovery = await TransactionRecovery.RecoverAsync(GameEnvironment.ConfigDirectory());
            if (!recovery.Ok)
                return Failure(ExitCodes.TaskFailed, recovery.Error!, context.Root);
        }

        if (config.ShaderBundle.Enabled)
        {
            var shaderResult = await BuildShaderAsync(context, config, cancellationToken);
            if (!shaderResult.Ok)
                return Failure(ExitCodes.TaskFailed, new CliError("SHADER_BUILD_FAILED", shaderResult.Message), context.Root);
        }

        foreach (var project in projectFiles)
        {
            Console.Error.WriteLine($"构建 {project}");
            if (!await ProcessUtil.RunAsync("dotnet",
            [
                "build", project, "--configuration", "Release",
                $"-p:RimWorldDir={environment.References.RimWorldDirectory}",
                $"-p:RimWorldManagedDir={environment.References.ManagedDirectory}",
                $"-p:HarmonyDllPath={environment.References.HarmonyDllPath}"
            ], context.Root, cancellationToken))
                return Failure(ExitCodes.TaskFailed, new CliError("BUILD_FAILED", $"dotnet build 失败: {project}"), context.Root);
        }

        var staging = Path.Combine(targetRoot, $".rimdev-staging-{runId}");
        try
        {
            Directory.CreateDirectory(staging);
            foreach (var item in plan.Where(x => x.Exists)) CopySyncItem(context.Root, staging, item.Item);
            PreserveProtectedFiles(target, staging, protectedRegex);
            ValidateStaging(staging, plan);
            ReplaceDirectory(target, staging, targetRoot, runId);
            return new DeployExecution(true, ExitCodes.Success, new DeployResult(context.Root, false, runId, target, projectFiles, preview, recovery.Found), null, warnings);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return Failure(ExitCodes.TaskFailed, new CliError("DEPLOY_FAILED", ex.Message, new { runId, staging }), context.Root);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    private static DeployExecution Failure(int exitCode, CliError error, string? projectRoot = null)
        => new(false, exitCode, projectRoot is null ? null : new DeployContextDetails(projectRoot), error);

    private static bool IsOptionalBuildOutput(string item)
        => item.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Equals("AssetBundles", StringComparison.OrdinalIgnoreCase);

    private static async Task<(bool Ok, string Message)> BuildShaderAsync(ProjectContext context, ProjectConfig config, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.ShaderBundle.UnityExePath) || !File.Exists(config.ShaderBundle.UnityExePath)) return (false, "Unity 路径不存在");
        var projectPath = Path.GetFullPath(config.ShaderBundle.UnityProjectPath ?? "", context.Root);
        if (!PathSafety.IsWithin(projectPath, context.Root) || !Directory.Exists(projectPath)) return (false, "Unity 项目路径不存在或越出项目目录");
        if (string.IsNullOrWhiteSpace(config.ShaderBundle.BuildMethod)) return (false, "Unity build_method 未配置");
        var ok = await ProcessUtil.RunAsync(config.ShaderBundle.UnityExePath, ["-batchmode", "-quit", "-projectPath", projectPath, "-executeMethod", config.ShaderBundle.BuildMethod], context.Root, cancellationToken);
        return (ok, ok ? "" : "Unity 构建失败");
    }

    private static void CopySyncItem(string root, string staging, string item)
    {
        var source = Path.GetFullPath(item, root);
        var destination = Path.GetFullPath(Path.Combine(staging, item));
        if (!PathSafety.IsWithin(source, root) || !PathSafety.IsWithin(destination, staging)) throw new IOException($"同步路径越界: {item}");
        if (File.Exists(source))
        {
            if (File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint)) throw new IOException($"拒绝跟随 Reparse Point: {source}");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, true);
            return;
        }
        CopyDirectory(source, destination, item.Equals("About", StringComparison.OrdinalIgnoreCase));
    }

    private static void CopyDirectory(string source, string destination, bool filterAbout = false)
    {
        var sourceInfo = new DirectoryInfo(source);
        if (sourceInfo.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException($"拒绝跟随 Reparse Point: {source}");
        Directory.CreateDirectory(destination);
        foreach (var file in sourceInfo.EnumerateFiles())
        {
            if (file.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException($"拒绝跟随 Reparse Point: {file.FullName}");
            if (filterAbout && (file.Extension.Equals(".svg", StringComparison.OrdinalIgnoreCase) || file.Extension.Equals(".html", StringComparison.OrdinalIgnoreCase))) continue;
            file.CopyTo(Path.Combine(destination, file.Name), true);
        }
        foreach (var directory in sourceInfo.EnumerateDirectories())
            CopyDirectory(directory.FullName, Path.Combine(destination, directory.Name), filterAbout);
    }

    private static void PreserveProtectedFiles(string target, string staging, Regex? regex)
    {
        if (regex is null || !Directory.Exists(target)) return;
        foreach (var item in Directory.EnumerateFileSystemEntries(target))
        {
            if (!regex.IsMatch(Path.GetFileName(item))) continue;
            if (File.GetAttributes(item).HasFlag(FileAttributes.ReparsePoint)) throw new IOException($"拒绝保护 Reparse Point: {item}");
            var destination = Path.Combine(staging, Path.GetFileName(item));
            if (File.Exists(item)) File.Copy(item, destination, true);
            else CopyDirectory(item, destination);
        }
    }

    private static void ValidateStaging(string staging, IReadOnlyList<DeploySyncPlan> plan)
    {
        if (!Directory.EnumerateFileSystemEntries(staging).Any()) throw new InvalidDataException("部署暂存目录为空");
        if (plan.Any(x => x.Item.Equals("About", StringComparison.OrdinalIgnoreCase) && x.Exists)
            && !File.Exists(Path.Combine(staging, "About", "About.xml")))
            throw new InvalidDataException("部署暂存结果缺少 About/About.xml");
    }

    private static void ReplaceDirectory(string target, string staging, string parent, string runId)
    {
        var backup = Path.Combine(parent, $".rimdev-backup-{runId}");
        if (Directory.Exists(backup)) Directory.Delete(backup, true);
        if (Directory.Exists(target)) Directory.Move(target, backup);
        try
        {
            Directory.Move(staging, target);
        }
        catch
        {
            if (!Directory.Exists(target) && Directory.Exists(backup)) Directory.Move(backup, target);
            throw;
        }

        try { if (Directory.Exists(backup)) Directory.Delete(backup, true); }
        catch (Exception ex) { Console.Error.WriteLine($"清理部署备份失败: {backup}: {ex.Message}"); }
    }
}

public sealed record DeployContextDetails(string ProjectRoot);
public sealed record DeploySyncPlan(string Item, string Source, string Destination, bool Exists, string Severity);
public sealed record DeployTargetAction(string Path, string Action);
public sealed record ShaderPlan(bool Enabled, string? UnityPath, string? ProjectPath, string? BuildMethod);
public sealed record BuildReferencePlan(string? RimWorldDirectory, string? ManagedDirectory, string? HarmonyDllPath);
public sealed record DeployPlan(string ProjectRoot, string Target, ShaderPlan Shader, BuildReferencePlan References, IReadOnlyList<string> Projects, IReadOnlyList<DeploySyncPlan> SyncItems, IReadOnlyList<DeployTargetAction> TargetActions, string? ProtectedPattern);
public sealed record DeployResult(string ProjectRoot, bool DryRun, string? RunId, string Target, IReadOnlyList<string> BuiltProjects, DeployPlan Plan, bool RecoveredTransaction);
public sealed record ProcessTerminationResult(bool Exited, string? Error = null);

internal static class ProcessUtil
{
    public static async Task<bool> WaitForRimWorldExitAsync(int timeoutSeconds, int stableSeconds, CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(0, timeoutSeconds));
        while (IsRimWorldRunning())
        {
            if (DateTime.UtcNow >= deadline) return false;
            await Task.Delay(1000, cancellationToken);
        }
        if (stableSeconds > 0) await Task.Delay(TimeSpan.FromSeconds(stableSeconds), cancellationToken);
        return true;
    }

    private static bool IsRimWorldRunning()
    {
        var processes = Process.GetProcessesByName("RimWorldWin64").Concat(Process.GetProcessesByName("RimWorld")).ToArray();
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public static async Task<ProcessTerminationResult> TerminateAsync(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync(CancellationToken.None);
            return new ProcessTerminationResult(true);
        }
        catch (Exception ex)
        {
            try
            {
                process.Refresh();
                if (process.HasExited) return new ProcessTerminationResult(true);
            }
            catch { }
            return new ProcessTerminationResult(false, ex.Message);
        }
    }

    public static Task<bool> RunAsync(string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        return RunAsync(startInfo, cancellationToken);
    }

    public static Task<bool> RunAsync(string fileName, IEnumerable<string> arguments, string workingDirectory, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        return RunAsync(startInfo, cancellationToken);
    }

    private static async Task<bool> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"无法启动进程: {startInfo.FileName}");
        process.OutputDataReceived += (_, eventArgs) => { if (eventArgs.Data is not null) Console.Error.WriteLine(eventArgs.Data); };
        process.ErrorDataReceived += (_, eventArgs) => { if (eventArgs.Data is not null) Console.Error.WriteLine(eventArgs.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        return process.ExitCode == 0;
    }
}
