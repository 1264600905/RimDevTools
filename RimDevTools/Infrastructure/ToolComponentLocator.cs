namespace RimDevTools;

public static class ToolComponentLocator
{
    public static string ToolDataPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("RIMDEVTOOLS_DATA_PATH");
        return string.IsNullOrWhiteSpace(overridePath)
            ? Path.Combine(ExecutableDirectory(), "Data")
            : Path.GetFullPath(overridePath);
    }

    internal static string ExecutableDirectory()
    {
        var processPath = Environment.ProcessPath;
        return processPath is not null && Path.GetFileNameWithoutExtension(processPath).Equals("RimDevTools", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(processPath)!
            : AppContext.BaseDirectory;
    }
}
