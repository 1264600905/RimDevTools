using System.Security.Cryptography;
using System.Text.Json;

namespace RimDevTools;

public sealed class ToolPackageManifest
{
    public const string RelativePath = ".tools/Data/tool-package.manifest.json";
    public int SchemaVersion { get; set; }
    public string ToolVersion { get; set; } = "";
    public string Platform { get; set; } = "";
    public int RuntimeMajor { get; set; }
    public string[] SupportedGameVersions { get; set; } = [];
    public int ConfigVersion { get; set; }
    public Dictionary<string, JsonElement> ConfigDefaults { get; set; } = [];
    public List<ToolPackageFile> Files { get; set; } = [];

    public static ToolPackageManifest Load(string root)
    {
        var manifest = JsonSerializer.Deserialize<ToolPackageManifest>(File.ReadAllText(Path.Combine(root, RelativePath)), JsonDefaults.Storage)
            ?? throw new InvalidDataException("工具发行清单为空");
        manifest.Validate();
        return manifest;
    }

    public void Validate()
    {
        if (SchemaVersion != 1 || !Version.TryParse(ToolVersion, out _) || Platform != "win-x64" || RuntimeMajor < 10 || ConfigVersion != 2
            || SupportedGameVersions is null || SupportedGameVersions.Length == 0 || SupportedGameVersions.Any(v => !RimWorldGameLayout.TryGet(v, out _))
            || Files is null || Files.Count == 0 || ConfigDefaults is null)
            throw new InvalidDataException("工具发行清单版本或兼容信息无效");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Files)
        {
            if (file is null || !IsManagedPath(file.Path) || !paths.Add(file.Path) || file.Size < 0 || file.Sha256 is null
                || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit) || file.UpdatePolicy != "replace"
                || file.Role is not ("executable" or "component" or "license" or "preset" or "skill"))
                throw new InvalidDataException("工具发行清单含无效或重复文件路径/属性");
        }
        var required = new List<string> { ".tools/RimDevTools.exe", ".tools/Data/tool-mods.manifest.json", ".tools/Ilspy/ilspycmd.exe" };
        foreach (var client in new[] { ".agents", ".claude" })
        {
            required.Add($"{client}/skills/rimdevtools/SKILL.md");
            required.Add($"{client}/skills/rimdevtools/references/commands-and-config.md");
        }
        foreach (var version in SupportedGameVersions)
        foreach (var assembly in new[] { "LogSyncMod.dll", "AutoTestRunner.dll" })
            required.Add($".tools/Data/LogSyncMod/{version}/Assemblies/{assembly}");
        if (required.Any(path => !paths.Contains(path))) throw new InvalidDataException("工具发行清单缺少必要载荷");
        if (ConfigDefaults.Count != 1 || !ConfigDefaults.TryGetValue("existing_project_mode", out var mode) || mode.ValueKind != JsonValueKind.False)
            throw new InvalidDataException("配置迁移规则必须为缺失的 existing_project_mode 补充 false");
    }

    public void VerifyFiles(string root)
    {
        Validate();
        foreach (var file in Files)
        {
            var path = Path.GetFullPath(file.Path, root);
            PathSafety.EnsureNoReparsePoints(root, path);
            if (!File.Exists(path) || new FileInfo(path).Length != file.Size || !Hash(path).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"工具文件与发行清单不一致: {file.Path}");
        }
    }

    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static bool IsManagedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.Contains(':') || Path.IsPathRooted(path)) return false;
        if (path.Split('/').Any(part => part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || IsDeviceName(part))) return false;
        return path == ".tools/RimDevTools.exe" || path == ".tools/Data/tool-mods.manifest.json"
            || path.StartsWith(".tools/Data/LogSyncMod/", StringComparison.Ordinal)
            || path.StartsWith(".tools/Data/licenses/", StringComparison.Ordinal)
            || path.StartsWith(".tools/Ilspy/", StringComparison.Ordinal)
            || path.StartsWith(".tools/modlist/", StringComparison.Ordinal)
            || path.StartsWith(".agents/skills/rimdevtools/", StringComparison.Ordinal)
            || path.StartsWith(".claude/skills/rimdevtools/", StringComparison.Ordinal);
    }

    private static bool IsDeviceName(string part)
    {
        var stem = part.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" || stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9';
    }
}

public sealed record ToolPackageFile(string Path, long Size, string Sha256, string Role, string UpdatePolicy);
