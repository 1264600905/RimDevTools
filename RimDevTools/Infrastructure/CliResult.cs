using System.Text.Json;

namespace RimDevTools;

public sealed record CliError(string Code, string Message, object? Details = null);

public sealed class CliResult
{
    public bool Ok { get; init; }
    public string Command { get; init; } = "";
    public string? ProjectRoot { get; init; }
    public object? Result { get; init; }
    public CliError? Error { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public static int WriteSuccess(string command, string? projectRoot, object? result = null, IEnumerable<string>? warnings = null)
    {
        Write(new CliResult { Ok = true, Command = command, ProjectRoot = projectRoot, Result = result, Warnings = warnings?.ToArray() ?? [] });
        return ExitCodes.Success;
    }

    public static int WriteFailure(string command, int exitCode, string code, string message, string? projectRoot = null, object? details = null, IEnumerable<string>? warnings = null)
    {
        Write(new CliResult { Ok = false, Command = command, ProjectRoot = projectRoot, Error = new CliError(code, message, details), Warnings = warnings?.ToArray() ?? [] });
        return exitCode;
    }

    public static void Write(object value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, JsonDefaults.CliOutput));
    }
}
