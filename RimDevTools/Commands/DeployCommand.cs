using System.Diagnostics;
using System.Text.RegularExpressions;

namespace RimDevTools;

public static class DeployCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var context = CliApplication.RequireProject("deploy", out var discoveryCode);
        if (context is null) return discoveryCode;
        var config = context.Config;
        var targetRoot = Path.GetFullPath(Path.Combine(config.RimWorldDir ?? "", "Mods"));
        var target = Path.Combine(targetRoot, config.ModName ?? Path.GetFileName(context.Root));
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(config.RimWorldDir) || string.IsNullOrWhiteSpace(config.ModName))
            return CliResult.WriteFailure("deploy", ExitCodes.InvalidArguments, "CONFIG_MISSING", "deploy 需要 rimworld_dir 和 mod_name", context.Root);
        if (!PathSafety.IsWithin(target, targetRoot))
            return CliResult.WriteFailure("deploy", ExitCodes.InvalidArguments, "DEPLOY_TARGET_OUTSIDE_MODS", "部署目标不在 RimWorld Mods 目录内", context.Root, new { target, targetRoot });

        var syncItems = config.SyncItems.ToList();
        if (CliArgs.Has(args, "--sync-source")) syncItems.Add("source");
        Regex? protectedRegex = null;
        try { protectedRegex = string.IsNullOrWhiteSpace(config.ProtectedFilesRegex) ? null : new Regex(config.ProtectedFilesRegex, RegexOptions.CultureInvariant); }
        catch (Exception ex) { return CliResult.WriteFailure("deploy", ExitCodes.InvalidArguments, "PROTECTED_REGEX_INVALID", ex.Message, context.Root); }
        var plan = new List<object>();
        foreach (var item in syncItems)
        {
            var source = Path.GetFullPath(Path.Combine(context.Root, item));
            var destination = Path.Combine(target, item);
            var exists = File.Exists(source) || Directory.Exists(source);
            plan.Add(new { source, destination, exists, severity = exists ? "info" : "warning" });
            if (!exists) warnings.Add($"同步项不存在: {item}");
        }
        var projectFiles = new List<string>();
        foreach (var project in config.CsprojPaths)
        {
            var path = Path.GetFullPath(Path.Combine(context.Root, project));
            if (!File.Exists(path)) return CliResult.WriteFailure("deploy", ExitCodes.MissingDependency, "CSPROJ_NOT_FOUND", $"工程文件不存在: {project}", context.Root);
            projectFiles.Add(path);
        }
        var runId = Guid.NewGuid().ToString("N");
        var preview = new { projectRoot = context.Root, target, shader = new { enabled = config.ShaderBundle.Enabled, unityPath = config.ShaderBundle.UnityExePath }, projects = projectFiles, syncItems = plan, protectedPattern = config.ProtectedFilesRegex };
        if (CliArgs.Has(args, "--dry-run")) return CliResult.WriteSuccess("deploy", context.Root, new { dryRun = true, plan = preview }, warnings);

        using var gate = EnvironmentLock.TryAcquire(GameEnvironment.ConfigDirectory(), "deploy", context.Root, runId, out var busyInfo);
        if (gate is null) return CliResult.WriteFailure("deploy", ExitCodes.Busy, "ENVIRONMENT_BUSY", "RimWorld 环境正被另一个 RimDevTools 进程占用", context.Root, new { busyInfo });
        if (!await ProcessUtil.WaitForRimWorldExitAsync(config.Timeouts.ExistingGameExit, config.Timeouts.PostExitDelay))
            return CliResult.WriteFailure("deploy", ExitCodes.Timeout, "GAME_EXIT_TIMEOUT", "等待 RimWorld 退出超时", context.Root);
        if (config.ShaderBundle.Enabled)
        {
            var shaderResult = await BuildShaderAsync(context, config);
            if (!shaderResult.ok) return CliResult.WriteFailure("deploy", ExitCodes.TaskFailed, "SHADER_BUILD_FAILED", shaderResult.message, context.Root);
        }
        foreach (var project in projectFiles)
        {
            Console.Error.WriteLine($"构建 {project}");
            if (!await ProcessUtil.RunAsync("dotnet", $"build \"{project}\" --configuration Release", context.Root))
                return CliResult.WriteFailure("deploy", ExitCodes.TaskFailed, "BUILD_FAILED", $"dotnet build 失败: {project}", context.Root);
        }
        var staging = Path.Combine(targetRoot, $".rimdev-staging-{runId}");
        try
        {
            Directory.CreateDirectory(staging);
            foreach (var item in syncItems) CopySyncItem(context.Root, staging, item, warnings);
            PreserveProtectedFiles(target, staging, protectedRegex);
            var missingRequired = syncItems.Where(item => !File.Exists(Path.Combine(context.Root, item)) && !Directory.Exists(Path.Combine(context.Root, item))).ToArray();
            if (missingRequired.Length > 0) Console.Error.WriteLine($"缺失同步项: {string.Join(", ", missingRequired)}");
            ReplaceDirectory(target, staging, targetRoot, runId);
            return CliResult.WriteSuccess("deploy", context.Root, new { runId, target, builtProjects = projectFiles, plan = preview }, warnings);
        }
        catch (Exception ex)
        {
            return CliResult.WriteFailure("deploy", ExitCodes.TaskFailed, "DEPLOY_FAILED", ex.Message, context.Root, new { runId, staging });
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    private static async Task<(bool ok, string message)> BuildShaderAsync(ProjectContext context, ProjectConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ShaderBundle.UnityExePath) || !File.Exists(config.ShaderBundle.UnityExePath)) return (false, "Unity 路径不存在");
        var projectPath = Path.GetFullPath(Path.Combine(context.Root, config.ShaderBundle.UnityProjectPath ?? ""));
        if (!Directory.Exists(projectPath)) return (false, "Unity 项目路径不存在");
        var args = $"-batchmode -quit -projectPath \"{projectPath}\" -executeMethod \"{config.ShaderBundle.BuildMethod}\"";
        return (await ProcessUtil.RunAsync(config.ShaderBundle.UnityExePath, args, context.Root), "Unity 构建失败");
    }

    private static void CopySyncItem(string root, string staging, string item, List<string> warnings)
    {
        var source = Path.GetFullPath(Path.Combine(root, item));
        var dest = Path.Combine(staging, item);
        if (!File.Exists(source) && !Directory.Exists(source)) { warnings.Add($"同步项不存在: {item}"); return; }
        if (File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint)) throw new IOException($"拒绝跟随 Reparse Point: {source}");
        if (File.Exists(source)) { Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(source, dest, true); return; }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (item.Equals("About", StringComparison.OrdinalIgnoreCase) && (file.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".html", StringComparison.OrdinalIgnoreCase))) continue;
            if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)) throw new IOException($"拒绝跟随 Reparse Point: {file}");
            var relative = Path.GetRelativePath(root, file);
            var target = Path.Combine(staging, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static void PreserveProtectedFiles(string target, string staging, Regex? regex)
    {
        if (regex is null || !Directory.Exists(target)) return;
        foreach (var item in Directory.EnumerateFileSystemEntries(target))
        {
            if (!regex.IsMatch(Path.GetFileName(item))) continue;
            var dest = Path.Combine(staging, Path.GetFileName(item));
            if (File.Exists(item)) { Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(item, dest, true); }
            else if (Directory.Exists(item)) CopyDirectory(item, dest);
        }
    }

    private static void ReplaceDirectory(string target, string staging, string parent, string runId)
    {
        var backup = Path.Combine(parent, $".rimdev-backup-{runId}");
        if (Directory.Exists(backup)) Directory.Delete(backup, true);
        if (Directory.Exists(target)) Directory.Move(target, backup);
        try { Directory.Move(staging, target); if (Directory.Exists(backup)) Directory.Delete(backup, true); }
        catch { if (!Directory.Exists(target) && Directory.Exists(backup)) Directory.Move(backup, target); throw; }
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
        foreach (var dir in Directory.EnumerateDirectories(source)) CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }
}

internal static class PathSafety
{
    public static bool IsWithin(string child, string parent)
    {
        var c = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class ProcessUtil
{
    public static async Task<bool> WaitForRimWorldExitAsync(int timeoutSeconds, int stableSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(0, timeoutSeconds));
        while (Process.GetProcessesByName("RimWorldWin64").Length > 0 || Process.GetProcessesByName("RimWorld").Length > 0)
        {
            if (DateTime.UtcNow >= deadline) return false;
            await Task.Delay(1000);
        }
        if (stableSeconds > 0) await Task.Delay(TimeSpan.FromSeconds(Math.Min(stableSeconds, Math.Max(0, (deadline - DateTime.UtcNow).TotalSeconds))));
        return true;
    }

    public static async Task<bool> RunAsync(string fileName, string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo(fileName, arguments) { WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"无法启动进程: {fileName}");
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.Error.WriteLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.Error.WriteLine(e.Data); };
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }
}
