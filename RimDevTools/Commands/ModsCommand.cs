namespace RimDevTools;

public static class ModsCommand
{
    public static async Task<int> InstallToolsAsync(string[] args, CancellationToken cancellationToken = default)
    {
        const string command = "mods install-tools";
        var verbose = CliArgs.Has(args, "--verbose");
        var explicitModsPath = CliArgs.Value(args, "--mods-path");
        ProjectContext? context;
        if (explicitModsPath is null)
        {
            context = CliApplication.RequireProject(command, out var projectError);
            if (context is null) return projectError;
        }
        else context = ProjectDiscovery.Find(Environment.CurrentDirectory, out _);

        CliError? pathError = null;
        var modsPath = explicitModsPath is null
            ? ResolveConfiguredModsPath(context!, out pathError)
            : Path.GetFullPath(explicitModsPath);
        if (modsPath is null)
            return CliResult.WriteFailure(command, ExitCodes.MissingDependency, pathError!.Code, pathError.Message, context?.Root);
        try
        {
            if (!Directory.Exists(modsPath))
                return CliResult.WriteFailure(command, ExitCodes.MissingDependency, "MODS_PATH_NOT_FOUND", $"Mods 目录不存在: {modsPath}", context?.Root);
            if (!Path.GetFileName(Path.TrimEndingDirectorySeparator(modsPath)).Equals("Mods", StringComparison.OrdinalIgnoreCase))
                return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "MODS_PATH_INVALID", $"目标目录必须是 RimWorld 的 Mods 目录: {modsPath}", context?.Root);
            PathSafety.EnsureNoReparsePoints(Path.GetDirectoryName(modsPath) ?? modsPath, modsPath);
        }
        catch (Exception ex)
        {
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "MODS_PATH_UNSAFE", ex.Message, context?.Root);
        }

        var dataRoot = ToolComponentLocator.ToolDataPath();
        ToolModDeploymentPlan plan;
        try { plan = AddWorkshopWarnings(ToolModInstaller.Plan(dataRoot, modsPath), context); }
        catch (ToolModDeploymentException ex)
        {
            return CliResult.WriteFailure(command, ExitCodes.TaskFailed, ex.Code, ex.Message, context?.Root);
        }
        catch (Exception ex)
        {
            return CliResult.WriteFailure(command, ExitCodes.TaskFailed, "TOOL_MOD_PLAN_FAILED", ex.Message, context?.Root);
        }

        object planResult = FormatPlan(plan, verbose);
        if (CliArgs.Has(args, "--dry-run"))
            return CliResult.WriteSuccess(command, context?.Root, planResult);
        if (HasBlockingConflict(plan))
            return CliResult.WriteFailure(command, ExitCodes.TaskFailed, "INSTALL_PLAN_CONFLICT", "工具 Mod 安装计划包含需要人工处理的冲突", context?.Root, planResult);

        var runId = Guid.NewGuid().ToString("N");
        using var gate = EnvironmentLock.TryAcquire(GameEnvironment.ConfigDirectory(), command, context?.Root ?? "", runId, out var busyInfo);
        if (gate is null)
            return CliResult.WriteFailure(command, ExitCodes.Busy, "ENVIRONMENT_BUSY", "RimWorld 环境正被另一个 RimDevTools 进程占用", context?.Root, new { busyInfo });
        var config = context?.Config ?? new ProjectConfig();
        try
        {
            if (!await ProcessUtil.WaitForRimWorldExitAsync(config.Timeouts.ExistingGameExit, config.Timeouts.PostExitDelay, cancellationToken))
                return CliResult.WriteFailure(command, ExitCodes.Timeout, "GAME_EXIT_TIMEOUT", "等待已有 RimWorld 退出超时", context?.Root);
            plan = AddWorkshopWarnings(ToolModInstaller.Plan(dataRoot, modsPath), context);
            if (HasBlockingConflict(plan))
                return CliResult.WriteFailure(command, ExitCodes.TaskFailed, "INSTALL_PLAN_CONFLICT", "取得环境锁后检测到工具 Mod 安装冲突", context?.Root, new { runId, plan = FormatPlan(plan, verbose) });
            var results = ToolModInstaller.Install(plan, runId);
            object componentResults = verbose
                ? results
                : results.Select(result => new
                {
                    result.PackageId,
                    result.SourceVersion,
                    result.TargetVersion,
                    result.Action,
                    result.Reason
                }).ToArray();
            return CliResult.WriteSuccess(command, context?.Root, new
            {
                runId,
                modsPath = plan.ModsPath,
                manifestPath = plan.ManifestPath,
                warnings = plan.Warnings,
                componentCount = results.Count,
                components = componentResults
            });
        }
        catch (OperationCanceledException) { throw; }
        catch (ToolModDeploymentException ex)
        {
            return CliResult.WriteFailure(command, ExitCodes.TaskFailed, ex.Code, ex.Message, context?.Root, new { runId, modsPath = plan.ModsPath });
        }
        catch (Exception ex)
        {
            return CliResult.WriteFailure(command, ExitCodes.TaskFailed, "TOOL_MOD_INSTALL_FAILED", ex.Message, context?.Root, new { runId, modsPath = plan.ModsPath });
        }
    }

    private static string? ResolveConfiguredModsPath(ProjectContext context, out CliError? error)
    {
        error = null;
        var environment = RimWorldEnvironmentResolver.Resolve(context);
        if (environment.References.RimWorldDirectory is null)
        {
            error = new CliError("RIMWORLD_DIR_NOT_FOUND", "无法从项目配置或 Steam 环境解析 RimWorld 安装目录");
            return null;
        }
        return Path.Combine(environment.References.RimWorldDirectory, "Mods");
    }

    private static bool HasBlockingConflict(ToolModDeploymentPlan plan)
        => plan.Components.Any(component => component.Action is "same_version_conflict" or "unknown_target_version");

    private static object FormatPlan(ToolModDeploymentPlan plan, bool verbose)
        => verbose
            ? new
            {
                modsPath = plan.ModsPath,
                manifestPath = plan.ManifestPath,
                warnings = plan.Warnings,
                components = plan.Components
            }
            : new
            {
                modsPath = plan.ModsPath,
                componentCount = plan.Components.Count,
                warnings = plan.Warnings,
                components = plan.Components.Select(component => new
                {
                    packageId = component.Source.PackageId,
                    version = component.Source.Version,
                    component.TargetVersion,
                    component.Action,
                    component.Reason
                }).ToArray()
            };

    private static ToolModDeploymentPlan AddWorkshopWarnings(ToolModDeploymentPlan plan, ProjectContext? context)
    {
        if (context is null) return plan;
        var environment = RimWorldEnvironmentResolver.Resolve(context);
        var warnings = plan.Warnings.ToList();
        foreach (var component in plan.Components)
        {
            var workshopMatches = environment.Mods.FindAll(component.Source.PackageId)
                .Where(match => match.Source.Equals("workshop", StringComparison.OrdinalIgnoreCase))
                .Select(match => match.Directory)
                .ToArray();
            if (workshopMatches.Length > 0)
                warnings.Add($"Workshop 中也存在 {component.Source.PackageId}，游戏可能报告重复 Mod: {string.Join(", ", workshopMatches)}");
        }
        return plan with { Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() };
    }
}
