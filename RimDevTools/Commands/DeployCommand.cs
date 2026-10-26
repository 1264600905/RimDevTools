using System.Diagnostics;
using System.Text.RegularExpressions;

namespace RimDevTools;

public sealed record DeployExecution(bool Ok, int ExitCode, object? Result = null, CliError? Error = null, IReadOnlyList<string>? Warnings = null);

public static class DeployCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var verbose = CliArgs.Has(args, "--verbose");
        var execution = await ExecuteAsync(args, cancellationToken, verbose: verbose);
        if (!execution.Ok)
            return CliResult.WriteFailure("deploy", execution.ExitCode, execution.Error!.Code, execution.Error.Message, execution.Result is DeployContextDetails details ? details.ProjectRoot : null, execution.Error.Details);

        var projectRoot = execution.Result is DeployResult result ? result.ProjectRoot : null;
        return CliResult.WriteSuccess("deploy", projectRoot, verbose ? execution.Result : SummarizeResult(execution.Result), execution.Warnings);
    }

    public static async Task<DeployExecution> ExecuteAsync(string[] args, CancellationToken cancellationToken = default, bool environmentPrepared = false, bool verbose = false)
    {
        verbose |= CliArgs.Has(args, "--verbose");
        var context = ProjectDiscovery.Find(Environment.CurrentDirectory, out var discoveryError);
        if (context is null)
        {
            var exitCode = discoveryError?.Code == "PROJECT_NOT_FOUND" ? ExitCodes.MissingDependency : ExitCodes.InvalidArguments;
            return Failure(exitCode, discoveryError ?? new CliError("PROJECT_NOT_FOUND", "未找到项目"));
        }

        var config = context.Config;
        var xmlHotRequested = CliArgs.Has(args, "--xml-hot");
        var templateError = ProjectTemplatePreflight.Validate(context);
        if (templateError is not null) return Failure(ExitCodes.InvalidArguments, templateError, context.Root);
        var modsConfigValidation = GameEnvironment.ValidateModsConfig(context);
        if (!modsConfigValidation.Ok)
            return Failure(ExitCodes.MissingDependency, new CliError(modsConfigValidation.Code!, modsConfigValidation.Message!, modsConfigValidation.Details), context.Root);
        var configDirectory = GameEnvironment.ConfigDirectory(context)!;
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

        var xmlHot = xmlHotRequested && ProcessUtil.IsRimWorldRunning();

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
        if (xmlHotRequested && !xmlHot)
            warnings.Add("未检测到运行中的 RimWorld，--xml-hot 将按普通构建和逐文件事务部署执行。");
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
        if (!xmlHot)
        {
            foreach (var project in config.CsprojPaths)
            {
                var path = Path.GetFullPath(project, context.Root);
                if (!PathSafety.IsWithin(path, context.Root))
                    return Failure(ExitCodes.InvalidArguments, new CliError("CSPROJ_OUTSIDE_PROJECT", $"工程路径越出项目目录: {project}"), context.Root);
                if (!File.Exists(path))
                    return Failure(ExitCodes.MissingDependency, new CliError("CSPROJ_NOT_FOUND", $"工程文件不存在: {project}"), context.Root);
                projectFiles.Add(path);
            }
        }

        var targetActions = Directory.Exists(target)
            ? Directory.EnumerateFileSystemEntries(target).Select(path => new DeployTargetAction(
                path,
                protectedRegex?.IsMatch(Path.GetFileName(path)) == true ? "protect" : xmlHot ? "preserve" : "replace")).ToArray()
            : [];
        var packageId = config.CurrentPackageId(context.Root);
        var artifactPlan = DeploymentArtifactRecovery.Plan(targetRoot, target, packageId);
        foreach (var artifact in artifactPlan.Where(x => x.Action == "retain")) warnings.Add($"部署残留需要人工确认: {artifact.Path}: {artifact.Reason}");
        var referencePlan = new BuildReferencePlan(environment.References.RimWorldDirectory, environment.References.ManagedDirectory, environment.References.HarmonyDllPath);
        var preview = new DeployPlan(
            context.Root,
            target,
            new ShaderPlan(!xmlHot && config.ShaderBundle.Enabled, config.ShaderBundle.UnityExePath, config.ShaderBundle.UnityProjectPath, config.ShaderBundle.BuildMethod),
            referencePlan,
            projectFiles,
            plan,
            targetActions,
            artifactPlan,
            config.ProtectedFilesRegex,
            xmlHot);

        if (CliArgs.Has(args, "--dry-run"))
            return new DeployExecution(true, ExitCodes.Success, new DeployResult(context.Root, true, null, target, [], preview, false, artifactPlan), null, warnings);

        var pendingFileTransactions = artifactPlan.Where(x => x.Kind == "transaction" && (x.Action is "recover" or "retain")).ToArray();
        if (xmlHot && pendingFileTransactions.Length > 0)
            return Failure(ExitCodes.Busy, new CliError("DEPLOY_RECOVERY_REQUIRED", "存在未完成的普通部署事务；请先停止 RimWorld 并恢复事务，再执行 XML 热同步", pendingFileTransactions), context.Root);

        if (!xmlHot && projectFiles.Count > 0)
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
        object? busyInfo = null;
        using var gate = environmentPrepared ? null : EnvironmentLock.TryAcquire(configDirectory, "deploy", context.Root, runId, out busyInfo);
        if (!environmentPrepared && gate is null)
            return Failure(ExitCodes.Busy, new CliError("ENVIRONMENT_BUSY", "RimWorld 环境正被另一个 RimDevTools 进程占用", new { busyInfo }), context.Root);
        var recovery = new TransactionRecoveryResult(true, false);
        if (!xmlHot && !environmentPrepared)
        {
            if (!await ProcessUtil.WaitForRimWorldExitAsync(config.Timeouts.ExistingGameExit, config.Timeouts.PostExitDelay, cancellationToken))
                return Failure(ExitCodes.Timeout, new CliError("GAME_EXIT_TIMEOUT", "等待 RimWorld 退出超时"), context.Root);

            recovery = await TransactionRecovery.RecoverAsync(configDirectory);
            if (!recovery.Ok)
                return Failure(ExitCodes.TaskFailed, recovery.Error!, context.Root);
        }

        var currentArtifactPlan = DeploymentArtifactRecovery.Plan(targetRoot, target, packageId);
        var artifactResults = xmlHot ? [] : DeploymentArtifactRecovery.Execute(targetRoot, target, currentArtifactPlan);
        foreach (var artifact in artifactResults.Where(x => x.Action is "failed" or "retain"))
            warnings.Add($"部署残留未清理: {artifact.Path}: {artifact.Reason}");
        var failedTransactions = artifactResults.Where(x => x.Kind == "transaction" && (x.Action is "failed" or "retain")).ToArray();
        if (failedTransactions.Length > 0)
            return Failure(ExitCodes.TaskFailed, new CliError("DEPLOY_RECOVERY_FAILED", "无法恢复此前中断的文件级部署事务；已保留备份，请先运行 test cleanup --dry-run 检查", failedTransactions), context.Root);

        if (!xmlHot && config.ShaderBundle.Enabled)
        {
            var shaderResult = await BuildShaderAsync(context, config, cancellationToken, verbose);
            if (!shaderResult.Ok)
                return Failure(ExitCodes.TaskFailed, new CliError("SHADER_BUILD_FAILED", shaderResult.Message), context.Root);
        }

        for (var index = 0; index < projectFiles.Count; index++)
        {
            var project = projectFiles[index];
            Console.Error.WriteLine(verbose ? $"构建 {project}" : $"构建项目 {index + 1}/{projectFiles.Count}");
            if (!await ProcessUtil.RunAsync("dotnet",
            [
                "build", project, "--configuration", "Release",
                $"-p:RimWorldDir={environment.References.RimWorldDirectory}",
                $"-p:RimWorldManagedDir={environment.References.ManagedDirectory}",
                $"-p:HarmonyDllPath={environment.References.HarmonyDllPath}"
            ], context.Root, cancellationToken, verbose))
                return Failure(ExitCodes.TaskFailed, new CliError("BUILD_FAILED", $"dotnet build 失败: {project}"), context.Root);
        }

        if (xmlHot)
        {
            var hotStaging = Path.Combine(targetRoot, $".rimdev-staging-{runId}");
            try
            {
                Directory.CreateDirectory(hotStaging);
                var reloadableXmlCopied = false;
                foreach (var item in plan.Where(x => x.Exists))
                    reloadableXmlCopied |= CopyHotSyncItem(context.Root, hotStaging, item.Item);

                var backup = Path.Combine(targetRoot, $".rimdev-backup-{runId}");
                var fileCommit = DeploymentFileTransaction.Commit(target, hotStaging, backup, packageId, null, removeStaleFiles: false);
                if (fileCommit.CleanupWarning is not null) warnings.Add(fileCommit.CleanupWarning);

                var xmlHotReload = await RequestXmlHotReloadAsync(reloadableXmlCopied, configDirectory, cancellationToken);
                if (xmlHotReload.Status is not ("queued" or "not_needed"))
                    warnings.Add($"XML 文件已部署，但游戏内热重载状态为 {xmlHotReload.Status}: {xmlHotReload.Message ?? xmlHotReload.ErrorCode ?? "未知原因"}");

                return new DeployExecution(true, ExitCodes.Success, new DeployResult(context.Root, false, runId, target, [], preview, false, [], xmlHotReload, fileCommit), null, warnings);
            }
            catch (OperationCanceledException) { throw; }
            catch (DeploymentTargetBusyException ex)
            {
                return Failure(ExitCodes.Busy, new CliError("DEPLOY_TARGET_BUSY", "部署目标中的文件正被其他程序使用；关闭占用程序后重试", ex.Details), context.Root);
            }
            catch (DeploymentRollbackFailedException ex)
            {
                return Failure(ExitCodes.TaskFailed, new CliError("DEPLOY_ROLLBACK_FAILED", ex.Message, new { ex.BackupPath, cause = ex.InnerException?.Message }), context.Root);
            }
            catch (Exception ex)
            {
                return Failure(ExitCodes.TaskFailed, new CliError("DEPLOY_FAILED", ex.Message, new { runId, hot = true }), context.Root);
            }
            finally
            {
                if (Directory.Exists(hotStaging))
                {
                    try { DeploymentFileTransaction.Remove(hotStaging); }
                    catch (Exception ex) { warnings.Add($"部署结束后无法清理暂存目录: {hotStaging}: {ex.Message}"); }
                }
            }
        }

        var staging = Path.Combine(targetRoot, $".rimdev-staging-{runId}");
        try
        {
            Directory.CreateDirectory(staging);
            foreach (var item in plan.Where(x => x.Exists)) CopySyncItem(context.Root, staging, item.Item);
            ValidateStaging(staging, plan, target, protectedRegex);
            var backup = Path.Combine(targetRoot, $".rimdev-backup-{runId}");
            var fileCommit = DeploymentFileTransaction.Commit(target, staging, backup, packageId, protectedRegex);
            if (fileCommit.CleanupWarning is not null) warnings.Add(fileCommit.CleanupWarning);
            return new DeployExecution(true, ExitCodes.Success, new DeployResult(context.Root, false, runId, target, projectFiles, preview, recovery.Found, artifactResults, FileCommit: fileCommit), null, warnings);
        }
        catch (OperationCanceledException) { throw; }
        catch (DeploymentTargetBusyException ex)
        {
            return Failure(ExitCodes.Busy, new CliError("DEPLOY_TARGET_BUSY", "部署目标中的文件正被其他程序使用；关闭占用程序后重试", ex.Details), context.Root);
        }
        catch (DeploymentRollbackFailedException ex)
        {
            return Failure(ExitCodes.TaskFailed, new CliError("DEPLOY_ROLLBACK_FAILED", ex.Message, new { ex.BackupPath, cause = ex.InnerException?.Message }), context.Root);
        }
        catch (Exception ex)
        {
            return Failure(ExitCodes.TaskFailed, new CliError("DEPLOY_FAILED", ex.Message, new { runId, staging }), context.Root);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                try { DeploymentFileTransaction.Remove(staging); }
                catch (Exception ex) { warnings.Add($"部署结束后无法清理暂存目录: {staging}: {ex.Message}"); }
            }
        }
    }

    private static DeployExecution Failure(int exitCode, CliError error, string? projectRoot = null)
        => new(false, exitCode, projectRoot is null ? null : new DeployContextDetails(projectRoot), error);

    private static object? SummarizeResult(object? result)
    {
        if (result is not DeployResult deploy) return result;

        return new
        {
            deploy.DryRun,
            deploy.RunId,
            deploy.Target,
            builtProjectCount = deploy.BuiltProjects.Count,
            syncItems = deploy.Plan.SyncItems.Select(item => new { item.Item, item.Exists, item.Severity }).ToArray(),
            targetActionCounts = deploy.Plan.TargetActions
                .GroupBy(action => action.Action, StringComparer.OrdinalIgnoreCase)
                .Select(group => new { action = group.Key, count = group.Count() })
                .ToArray(),
            artifactActionCounts = deploy.ArtifactActions
                .GroupBy(action => action.Action, StringComparer.OrdinalIgnoreCase)
                .Select(group => new { action = group.Key, count = group.Count() })
                .ToArray(),
            deploy.RecoveredTransaction,
            fileCommit = deploy.FileCommit,
            xmlHotReload = deploy.XmlHotReload,
            shaderBuildEnabled = deploy.Plan.Shader.Enabled
        };
    }

    private static async Task<XmlHotReloadRequestResult> RequestXmlHotReloadAsync(bool reloadableXmlCopied, string configDirectory, CancellationToken cancellationToken)
    {
        if (!reloadableXmlCopied)
            return new XmlHotReloadRequestResult("not_needed", Message: "本次没有同步 Def 或 Patches XML");

        var endpoints = XmlHotReloadHttpClient.FindLiveEndpoints(configDirectory);
        if (endpoints.Count == 0)
            return new XmlHotReloadRequestResult("unavailable", ErrorCode: "XML_RELOAD_ENDPOINT_NOT_FOUND", Message: "未找到运行中的 LogSyncMod HTTP endpoint");
        if (endpoints.Count > 1)
            return new XmlHotReloadRequestResult("ambiguous", ErrorCode: "RIMWORLD_INSTANCE_AMBIGUOUS", Message: "检测到多个运行中的 LogSyncMod 实例，无法确定目标");

        // Let the complete in-place copy settle before asking the game to read the Defs again.
        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return await XmlHotReloadHttpClient.RequestReloadAsync(endpoints[0], cancellationToken: cancellationToken);
    }

    private static bool IsReloadableXml(string path)
    {
        if (!Path.GetExtension(path).Equals(".xml", StringComparison.OrdinalIgnoreCase)) return false;
        return path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment.Equals("Defs", StringComparison.OrdinalIgnoreCase) || segment.Equals("Patches", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsOptionalBuildOutput(string item)
        => item.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Equals("AssetBundles", StringComparison.OrdinalIgnoreCase);

    private static async Task<(bool Ok, string Message)> BuildShaderAsync(ProjectContext context, ProjectConfig config, CancellationToken cancellationToken, bool verbose)
    {
        if (string.IsNullOrWhiteSpace(config.ShaderBundle.UnityExePath) || !File.Exists(config.ShaderBundle.UnityExePath)) return (false, "Unity 路径不存在");
        var projectPath = Path.GetFullPath(config.ShaderBundle.UnityProjectPath ?? "", context.Root);
        if (!PathSafety.IsWithin(projectPath, context.Root) || !Directory.Exists(projectPath)) return (false, "Unity 项目路径不存在或越出项目目录");
        if (string.IsNullOrWhiteSpace(config.ShaderBundle.BuildMethod)) return (false, "Unity build_method 未配置");
        var ok = await ProcessUtil.RunAsync(config.ShaderBundle.UnityExePath, ["-batchmode", "-quit", "-projectPath", projectPath, "-executeMethod", config.ShaderBundle.BuildMethod], context.Root, cancellationToken, verbose);
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

    private static bool CopyHotSyncItem(string root, string target, string item)
    {
        var source = Path.GetFullPath(item, root);
        var destination = Path.GetFullPath(Path.Combine(target, item));
        if (!PathSafety.IsWithin(source, root) || !PathSafety.IsWithin(destination, target)) throw new IOException($"同步路径越界: {item}");
        if (File.Exists(source))
        {
            if (IsDll(source)) return false;
            if (File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint)) throw new IOException($"拒绝跟随 Reparse Point: {source}");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, true);
            return IsReloadableXml(source);
        }
        return CopyHotDirectory(source, destination, item.Equals("About", StringComparison.OrdinalIgnoreCase));
    }

    private static bool CopyHotDirectory(string source, string destination, bool filterAbout = false)
    {
        var sourceInfo = new DirectoryInfo(source);
        if (sourceInfo.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException($"拒绝跟随 Reparse Point: {source}");
        Directory.CreateDirectory(destination);
        var reloadableXmlCopied = false;
        foreach (var file in sourceInfo.EnumerateFiles())
        {
            if (file.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException($"拒绝跟随 Reparse Point: {file.FullName}");
            if (IsDll(file.FullName) || filterAbout && (file.Extension.Equals(".svg", StringComparison.OrdinalIgnoreCase) || file.Extension.Equals(".html", StringComparison.OrdinalIgnoreCase))) continue;
            file.CopyTo(Path.Combine(destination, file.Name), true);
            reloadableXmlCopied |= IsReloadableXml(file.FullName);
        }
        foreach (var directory in sourceInfo.EnumerateDirectories())
            reloadableXmlCopied |= CopyHotDirectory(directory.FullName, Path.Combine(destination, directory.Name), filterAbout);
        return reloadableXmlCopied;
    }

    private static bool IsDll(string path) => Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase);

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

    private static void ValidateStaging(string staging, IReadOnlyList<DeploySyncPlan> plan, string target, Regex? protectedRegex)
    {
        var hasStagedItems = Directory.EnumerateFileSystemEntries(staging).Any();
        var hasProtectedTargetItems = protectedRegex is not null && Directory.Exists(target)
            && Directory.EnumerateFileSystemEntries(target).Any(path => protectedRegex.IsMatch(Path.GetFileName(path)));
        if (!hasStagedItems && !hasProtectedTargetItems) throw new InvalidDataException("部署暂存目录为空");
        if (plan.Any(x => x.Item.Equals("About", StringComparison.OrdinalIgnoreCase) && x.Exists)
            && !File.Exists(Path.Combine(staging, "About", "About.xml")))
            throw new InvalidDataException("部署暂存结果缺少 About/About.xml");
    }
}

public sealed record DeployContextDetails(string ProjectRoot);
public sealed record DeploySyncPlan(string Item, string Source, string Destination, bool Exists, string Severity);
public sealed record DeployTargetAction(string Path, string Action);
public sealed record ShaderPlan(bool Enabled, string? UnityPath, string? ProjectPath, string? BuildMethod);
public sealed record BuildReferencePlan(string? RimWorldDirectory, string? ManagedDirectory, string? HarmonyDllPath);
public sealed record DeployPlan(string ProjectRoot, string Target, ShaderPlan Shader, BuildReferencePlan References, IReadOnlyList<string> Projects, IReadOnlyList<DeploySyncPlan> SyncItems, IReadOnlyList<DeployTargetAction> TargetActions, IReadOnlyList<DeploymentArtifactAction> ArtifactActions, string? ProtectedPattern, bool XmlHot);
public sealed record DeployResult(string ProjectRoot, bool DryRun, string? RunId, string Target, IReadOnlyList<string> BuiltProjects, DeployPlan Plan, bool RecoveredTransaction, IReadOnlyList<DeploymentArtifactAction> ArtifactActions, XmlHotReloadRequestResult? XmlHotReload = null, DeploymentFileCommitResult? FileCommit = null);
public sealed record ProcessTerminationResult(bool Exited, string? Error = null);

internal static class ProcessUtil
{
    private const int FailureOutputLineLimit = 12;
    private const int FailureOutputLineLengthLimit = 500;
    private const int ImportantOutputLineLimit = 12;

    internal static Func<bool>? RimWorldRunningOverride { get; set; }

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

    internal static bool IsRimWorldRunning()
    {
        if (RimWorldRunningOverride is not null) return RimWorldRunningOverride();
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

    public static Task<bool> RunAsync(string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken = default, bool verbose = false)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        return RunAsync(startInfo, cancellationToken, verbose);
    }

    public static Task<bool> RunAsync(string fileName, IEnumerable<string> arguments, string workingDirectory, CancellationToken cancellationToken = default, bool verbose = false)
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
        return RunAsync(startInfo, cancellationToken, verbose);
    }

    private static async Task<bool> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken, bool verbose)
    {
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"无法启动进程: {startInfo.FileName}");
        var outputTail = new Queue<string>();
        var importantOutput = new Queue<string>();
        var outputGate = new object();
        void CaptureOutput(string? line)
        {
            if (line is null) return;
            if (verbose) Console.Error.WriteLine(line);
            lock (outputGate)
            {
                outputTail.Enqueue(line.Length > FailureOutputLineLengthLimit ? line[..FailureOutputLineLengthLimit] + "…" : line);
                while (outputTail.Count > FailureOutputLineLimit) outputTail.Dequeue();
                if (!verbose && (line.Contains("warning", StringComparison.OrdinalIgnoreCase) || line.Contains("error", StringComparison.OrdinalIgnoreCase)))
                {
                    importantOutput.Enqueue(line.Length > FailureOutputLineLengthLimit ? line[..FailureOutputLineLengthLimit] + "…" : line);
                    while (importantOutput.Count > ImportantOutputLineLimit) importantOutput.Dequeue();
                }
            }
        }

        process.OutputDataReceived += (_, eventArgs) => CaptureOutput(eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => CaptureOutput(eventArgs.Data);
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
        process.WaitForExit();
        if (!verbose)
        {
            string[] important;
            string[] tail;
            lock (outputGate)
            {
                important = importantOutput.ToArray();
                tail = outputTail.ToArray();
            }

            if (process.ExitCode == 0 && important.Length > 0)
            {
                Console.Error.WriteLine($"子进程 warning/error 摘要: {startInfo.FileName}");
                foreach (var line in important) Console.Error.WriteLine(line);
            }
            else if (process.ExitCode != 0)
            {
                Console.Error.WriteLine($"子进程失败，退出码 {process.ExitCode}: {startInfo.FileName}");
                foreach (var line in important.Concat(tail).Distinct(StringComparer.Ordinal).Take(FailureOutputLineLimit))
                    Console.Error.WriteLine(line);
            }
        }
        return process.ExitCode == 0;
    }
}
