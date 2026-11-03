using System.Text.Json;

namespace RimDevTools;

internal sealed record TemplatePackageVersion(string GameVersion, int SchemaVersion);

internal static class TemplatePackageVersionReader
{
    private const int ManifestSchemaVersion = 1;
    private static readonly TemplatePackageVersion Unpackaged = new("unpackaged", 0);
    private static readonly TemplatePackageVersion Unavailable = new("unavailable", 0);

    public static TemplatePackageVersion Read()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "project-template.manifest.json");
        if (!File.Exists(path) && Environment.ProcessPath is { } processPath)
        {
            var executableDirectory = Path.GetDirectoryName(processPath);
            if (!string.IsNullOrWhiteSpace(executableDirectory))
                path = Path.Combine(executableDirectory, "Data", "project-template.manifest.json");
        }
        if (!File.Exists(path)) return Unpackaged;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != ManifestSchemaVersion) return Unavailable;

            var gameVersion = root.GetProperty("templateGameVersion").GetString();
            var schemaVersion = root.GetProperty("templateSchemaVersion").GetInt32();
            if (string.IsNullOrWhiteSpace(gameVersion) || schemaVersion <= 0) return Unavailable;

            return new TemplatePackageVersion(gameVersion, schemaVersion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return Unavailable;
        }
    }
}
