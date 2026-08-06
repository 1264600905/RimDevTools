using System.Diagnostics;
using System.Text.Json;

namespace RimDevTools;

public static class CliApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h"))
        {
            WriteHelp();
            return ExitCodes.Success;
        }

        if (args.Length == 1 && args[0].Equals("--version", StringComparison.OrdinalIgnoreCase))
        {
            CliResult.Write(new { ok = true, command = "version", rimDevToolsVersion = typeof(CliApplication).Assembly.GetName().Version?.ToString() ?? "1.0.0", rimSearcherVersion = FindSiblingVersion() });
            return ExitCodes.Success;
        }

        var command = CommandPath(args);
        try
        {
            return command switch
            {
                "doctor" => await DoctorCommand.RunAsync(args),
                "deploy" => await DeployCommand.RunAsync(args),
                "ref search" => await RefCommand.SearchAsync(args),
                "ref pull" => await RefCommand.PullAsync(args),
                "ref deps" => await RefCommand.DepsAsync(args),
                "ref related" => await RefCommand.RelatedAsync(args),
                "test run" => await TestCommand.RunAsync(args),
                "test settings" => await TestCommand.SettingsAsync(args),
                _ => CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "UNKNOWN_COMMAND", $"未知命令: {command}")
            };
        }
        catch (OperationCanceledException)
        {
            return CliResult.WriteFailure(command, ExitCodes.Cancelled, "CANCELLED", "操作已取消");
        }
        catch (ArgumentException ex)
        {
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "INVALID_ARGUMENT", ex.Message);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{command}] {ex}");
            return CliResult.WriteFailure(command, ExitCodes.TaskFailed, "UNEXPECTED_ERROR", ex.Message);
        }
    }

    public static string CommandPath(string[] args)
    {
        if (args.Length == 0) return "help";
        if (args[0] is "ref" or "test" or "data" && args.Length > 1 && !args[1].StartsWith('-')) return $"{args[0]} {args[1]}";
        return args[0];
    }

    public static ProjectContext? RequireProject(string command, out int errorCode)
    {
        var context = ProjectDiscovery.Find(Environment.CurrentDirectory, out var error);
        if (context is not null) { errorCode = ExitCodes.Success; return context; }
        errorCode = error?.Code == "PROJECT_NOT_FOUND" ? ExitCodes.MissingDependency : ExitCodes.InvalidArguments;
        CliResult.WriteFailure(command, errorCode, error?.Code ?? "PROJECT_NOT_FOUND", error?.Message ?? "未找到项目");
        return null;
    }

    private static string? FindSiblingVersion()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "rimsearcher.exe");
        return File.Exists(path) ? FileVersionInfo.GetVersionInfo(path).FileVersion : null;
    }

    private static void WriteHelp()
    {
        Console.WriteLine("RimDevTools.exe <command> [options]");
        Console.WriteLine("Commands: doctor, deploy, ref search|pull|deps|related, test run|settings, data refresh|status");
        Console.WriteLine("Project commands discover mod_config.jsonc in the current directory or its direct parent.");
    }
}

public static class CliArgs
{
    public static bool Has(string[] args, string name) => args.Any(x => x.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static string? Value(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++) if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    public static int IntValue(string[] args, string name, int fallback)
        => int.TryParse(Value(args, name), out var result) && result > 0 ? result : fallback;

    public static string RequiredValue(string[] args, string name)
        => Value(args, name) ?? throw new ArgumentException($"缺少参数 {name}");
}
