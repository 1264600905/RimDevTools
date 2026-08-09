namespace RimDevTools;

public static class CliApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            CliHelp.TryWrite("help");
            return ExitCodes.Success;
        }

        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h"))
        {
            var helpPath = CliHelp.Path(args);
            if (CliHelp.TryWrite(helpPath)) return ExitCodes.Success;
        }

        if (args.Length == 1 && args[0].Equals("--version", StringComparison.OrdinalIgnoreCase))
        {
            CliResult.Write(new
            {
                ok = true,
                command = "version",
                rimDevToolsVersion = typeof(CliApplication).Assembly.GetName().Version?.ToString() ?? "1.0.0"
            });
            return ExitCodes.Success;
        }

        var command = CommandPath(args);
        var argumentError = CliArgs.Validate(command, args);
        if (argumentError is not null)
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "INVALID_ARGUMENT", argumentError);

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            return command switch
            {
                "doctor" => await DoctorCommand.RunAsync(args),
                "deploy" => await DeployCommand.RunAsync(args, cancellation.Token),
                "ref search" => await RefCommand.SearchAsync(args),
                "ref pull" => await RefCommand.PullAsync(args, cancellation.Token),
                "ref deps" => await RefCommand.DepsAsync(args),
                "ref related" => await RefCommand.RelatedAsync(args),
                "test run" => await TestCommand.RunAsync(args, cancellation.Token),
                "test settings" => await TestCommand.SettingsAsync(args, cancellation.Token),
                "test cleanup" => await TestCommand.CleanupAsync(args, cancellation.Token),
                "mods install-tools" => await ModsCommand.InstallToolsAsync(args, cancellation.Token),
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
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    public static string CommandPath(string[] args)
    {
        if (args.Length == 0) return "help";
        if (args[0] is "ref" or "test" or "data" or "mods" && args.Length > 1 && !args[1].StartsWith('-')) return $"{args[0]} {args[1]}";
        return args[0];
    }

    public static ProjectContext? RequireProject(string command, out int errorCode)
    {
        var context = ProjectDiscovery.Find(Environment.CurrentDirectory, out var error);
        if (context is not null)
        {
            if (context.Config.ConfigVersion == 2) { errorCode = ExitCodes.Success; return context; }
            errorCode = ExitCodes.InvalidArguments;
            CliResult.WriteFailure(command, errorCode, "CONFIG_VERSION_UNSUPPORTED", $"需要 config_version=2，当前为 {context.Config.ConfigVersion}", context.Root);
            return null;
        }
        errorCode = error?.Code == "PROJECT_NOT_FOUND" ? ExitCodes.MissingDependency : ExitCodes.InvalidArguments;
        CliResult.WriteFailure(command, errorCode, error?.Code ?? "PROJECT_NOT_FOUND", error?.Message ?? "未找到项目");
        return null;
    }

}

public static class CliArgs
{
    private sealed record Option(string Name, bool TakesValue, bool AllowMultiple = false);

    private static readonly IReadOnlyDictionary<string, (int PrefixLength, int Positionals, Option[] Options)> Contracts
        = new Dictionary<string, (int, int, Option[])>(StringComparer.OrdinalIgnoreCase)
        {
            ["doctor"] = (1, 0, [new("--verbose", false)]),
            ["deploy"] = (1, 0, [new("--sync-source", false), new("--dry-run", false)]),
            ["ref search"] = (2, 1, [new("--fuzzy", false)]),
            ["ref pull"] = (2, 1, [new("--version", true), new("--deps", false), new("--all", false), new("--fuzzy", false), new("--force", false)]),
            ["ref deps"] = (2, 1, [new("--version", true), new("--fuzzy", false)]),
            ["ref related"] = (2, 1, [new("--version", true), new("--fuzzy", false)]),
            ["test run"] = (2, 0, [new("--deploy", false), new("--sync-source", false), new("--mode", true), new("--mods-file", true), new("--timeout-seconds", true), new("--target-package-id", true), new("--scope", true, true), new("--auto-test-scope", true), new("--defname", true, true), new("--auto-test-defname", true), new("--capture-screenshots", false), new("--isolated-desktop", false), new("--no-isolated-desktop", false)]),
            ["test settings"] = (2, 0, []),
            ["test cleanup"] = (2, 0, [new("--dry-run", false)]),
            ["mods install-tools"] = (2, 0, [new("--mods-path", true), new("--dry-run", false)])
        };

    public static string? Validate(string command, string[] args)
    {
        if (!Contracts.TryGetValue(command, out var contract)) return null;

        var allowed = contract.Options.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var positionals = 0;
        for (var index = contract.PrefixLength; index < args.Length; index++)
        {
            var argument = args[index];
            if (!argument.StartsWith('-'))
            {
                positionals++;
                continue;
            }

            if (!allowed.TryGetValue(argument, out var option)) return $"未知参数: {argument}";
            if (!option.AllowMultiple && !seen.Add(option.Name)) return $"参数重复: {option.Name}";
            seen.Add(option.Name);
            if (!option.TakesValue) continue;
            if (++index >= args.Length || args[index].StartsWith('-')) return $"缺少参数值: {option.Name}";
        }

        return positionals == contract.Positionals
            ? null
            : $"命令 {command} 需要 {contract.Positionals} 个位置参数，实际为 {positionals}";
    }

    internal static IReadOnlyList<string> OptionNames(string command)
        => Contracts.TryGetValue(command, out var contract)
            ? contract.Options.Select(option => option.Name).ToArray()
            : [];

    public static bool Has(string[] args, string name) => args.Any(x => x.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static string? Value(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase) && !args[i + 1].StartsWith('-')) return args[i + 1];
        return null;
    }

    public static IReadOnlyList<string> Values(string[] args, string name)
    {
        var values = new List<string>();
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase) && !args[i + 1].StartsWith('-')) values.Add(args[i + 1]);
        return values;
    }

    public static string RequiredValue(string[] args, string name)
        => Value(args, name) ?? throw new ArgumentException($"缺少参数 {name}");

    public static bool TryPositiveInt(string[] args, string name, int fallback, out int value, out string? error)
    {
        error = null;
        value = fallback;
        if (!Has(args, name)) return value > 0;
        var raw = Value(args, name);
        if (raw is null || !int.TryParse(raw, out value) || value <= 0)
        {
            error = $"{name} 必须是正整数";
            return false;
        }
        return true;
    }
}
