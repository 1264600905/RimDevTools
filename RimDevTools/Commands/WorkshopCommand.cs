using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace RimDevTools;

public static class WorkshopCommand
{
    private const string ResultPrefix = "RIMDEVTOOLS_STEAMWORKS_PROBE_RESULT=";

    public static async Task<int> ProbeAsync(string[] args, CancellationToken cancellationToken = default)
    {
        const string command = "workshop probe";
        if (!CliArgs.TryPositiveInt(args, "--timeout-seconds", 15, out var seconds, out var argumentError) || seconds > 120)
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "INVALID_ARGUMENT", argumentError ?? "--timeout-seconds 必须在 1 至 120 之间");

        ulong? publishedFileId = null;
        if (CliArgs.Has(args, "--published-file-id"))
        {
            if (!ulong.TryParse(CliArgs.Value(args, "--published-file-id"), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0 || id == ulong.MaxValue)
                return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "INVALID_ARGUMENT", "--published-file-id 必须是有效的正整数条目 ID");
            publishedFileId = id;
        }

        var context = CliApplication.RequireProject(command, out var exitCode);
        if (context is null) return exitCode;
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            return CliResult.WriteFailure(command, ExitCodes.MissingDependency, "STEAMWORKS_PLATFORM_UNSUPPORTED", "当前验证仅支持 Windows x64", context.Root);

        var steam = SteamSessionGuard.CheckForLaunch();
        if (!steam.IsRunning)
            return CliResult.WriteFailure(command, ExitCodes.MissingDependency, steam.Code, steam.Message, context.Root);
        var installation = SteamInstallationResolver.Resolve(context.Config.RimWorldDir, context.Config.WorkshopPaths, expectedGameVersion: context.Config.GameVersion);
        if (installation.RimWorldDirectory is not { } gameDirectory)
            return CliResult.WriteFailure(command, ExitCodes.MissingDependency, "RIMWORLD_DIR_NOT_FOUND", "未找到与项目版本匹配的游戏安装目录", context.Root);
        var nativePath = new[]
        {
            Path.Combine(gameDirectory, "steam_api64.dll"),
            Path.Combine(gameDirectory, "RimWorldWin64_Data", "Plugins", "x86_64", "steam_api64.dll")
        }.FirstOrDefault(File.Exists);
        if (nativePath is null)
            return CliResult.WriteFailure(command, ExitCodes.MissingDependency, "STEAMWORKS_LIBRARY_NOT_FOUND", "游戏安装中没有找到 steam_api64.dll", context.Root);

        var before = GameProcessIds();
        var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("无法确定 CLI 可执行文件"))
        {
            WorkingDirectory = gameDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(CliApplication).Assembly.Location);
        start.ArgumentList.Add("--steamworks-probe-helper");
        start.ArgumentList.Add(nativePath);
        start.ArgumentList.Add((publishedFileId ?? 0).ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(seconds.ToString(CultureInfo.InvariantCulture));
        start.Environment["SteamAppId"] = SteamworksProbe.RimWorldAppId.ToString(CultureInfo.InvariantCulture);
        start.Environment["SteamGameId"] = start.Environment["SteamAppId"];

        using var helper = Process.Start(start) ?? throw new InvalidOperationException("无法启动 Steamworks 验证进程");
        var stdoutTask = helper.StandardOutput.ReadToEndAsync();
        var stderrTask = helper.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds + 5));
        var timedOut = false;
        try { await helper.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!helper.HasExited) helper.Kill(true);
            await helper.WaitForExitAsync(CancellationToken.None);
            timedOut = true;
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        cancellationToken.ThrowIfCancellationRequested();
        if (CliArgs.Has(args, "--verbose"))
        {
            foreach (var line in stdout.Split('\n').Where(line => !line.StartsWith(ResultPrefix, StringComparison.Ordinal)))
                if (!string.IsNullOrWhiteSpace(line)) Console.Error.WriteLine(line.TrimEnd());
            if (!string.IsNullOrWhiteSpace(stderr)) Console.Error.WriteLine(stderr.TrimEnd());
        }

        var resultLine = stdout.Split('\n').LastOrDefault(line => line.StartsWith(ResultPrefix, StringComparison.Ordinal));
        var probe = resultLine is null ? null : JsonSerializer.Deserialize<SteamworksProbeResult>(resultLine[ResultPrefix.Length..], JsonDefaults.Storage);
        var details = new
        {
            backend = "steamworks-standalone", readOnly = true, gameStarted = false, uploadAttempted = false,
            nativeLibraryPath = nativePath, helperExitCode = helper.ExitCode,
            gameProcessIdsBefore = before, gameProcessIdsAfter = GameProcessIds(), probe
        };
        if (timedOut)
            return CliResult.WriteFailure(command, ExitCodes.Timeout, "STEAMWORKS_HELPER_TIMEOUT", "独立验证进程超时，已终止本次 helper", context.Root, details);
        if (helper.ExitCode != 0 || probe is null)
            return CliResult.WriteFailure(command, ExitCodes.TaskFailed, "STEAMWORKS_HELPER_FAILED", "原生验证进程没有返回有效结果；使用 --verbose 查看原生输出", context.Root, details);
        if (!probe.Ready)
            return CliResult.WriteFailure(command, probe.ErrorCode == "WORKSHOP_QUERY_TIMEOUT" ? ExitCodes.Timeout : ExitCodes.TaskFailed,
                probe.ErrorCode ?? "STEAMWORKS_PROBE_FAILED", probe.Message ?? "Steamworks 验证未通过", context.Root, details);
        return CliResult.WriteSuccess(command, context.Root, details);
    }

    internal static int RunProbeHelper(string[] args)
    {
        if (args.Length != 4 || !ulong.TryParse(args[2], out var id) || !int.TryParse(args[3], out var seconds) || seconds is < 1 or > 120)
            return ExitCodes.InvalidArguments;
        var result = SteamworksProbe.Run(args[1], id == 0 ? null : id, TimeSpan.FromSeconds(seconds));
        Console.WriteLine(ResultPrefix + JsonSerializer.Serialize(result, JsonDefaults.Storage));
        return ExitCodes.Success;
    }

    private static int[] GameProcessIds()
    {
        var processes = Process.GetProcessesByName("RimWorldWin64").Concat(Process.GetProcessesByName("RimWorld")).ToArray();
        try { return processes.Select(process => process.Id).Order().ToArray(); }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}
