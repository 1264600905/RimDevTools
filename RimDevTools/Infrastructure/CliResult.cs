using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public static int WriteSuccess(string command, string? projectRoot, object? result = null, IEnumerable<string>? warnings = null)
    {
        Write(new CliResult { Ok = true, Command = command, ProjectRoot = projectRoot, Result = result, Warnings = warnings?.ToArray() ?? [] });
        return ExitCodes.Success;
    }

    public static int WriteFailure(string command, int exitCode, string code, string message, string? projectRoot = null, object? details = null)
    {
        Write(new CliResult { Ok = false, Command = command, ProjectRoot = projectRoot, Error = new CliError(code, message, details) });
        return exitCode;
    }

    public static void Write(object value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
    }
}
