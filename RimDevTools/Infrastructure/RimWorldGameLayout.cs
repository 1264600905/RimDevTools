using System.Diagnostics;
using System.Reflection;

namespace RimDevTools;

public sealed record RimWorldGameLayout(
    string Version,
    string ExecutableName,
    string AssemblyDirectoryRelativePath,
    IReadOnlyList<string> RequiredAssemblies,
    string TargetFrameworkDescription)
{
    private static readonly RimWorldGameLayout Version16 = new(
        "1.6",
        "RimWorldWin64.exe",
        Path.Combine("RimWorldWin64_Data", "Managed"),
        [
            "Assembly-CSharp.dll",
            "UnityEngine.CoreModule.dll",
            "UnityEngine.ImageConversionModule.dll",
            "UnityEngine.IMGUIModule.dll",
            "UnityEngine.TextRenderingModule.dll"
        ],
        ".NET Framework 4.8 或 4.7.2");

    private static readonly RimWorldGameLayout Version17 = new(
        "1.7+",
        "RimWorld.exe",
        "",
        ["Assembly-CSharp.dll", "UnityStubs.dll", "Waylith.dll"],
        ".NET 10 (net10.0)");

    public static IReadOnlyList<string> SupportedLayouts { get; } = ["1.6 legacy", "1.7+ modern"];

    public const string SupportedVersionDescription = "1.6，或 1.7 及更高的精确主/次版本（例如 1.8、1.9）";

    public static IReadOnlyList<string> LegacyRequiredAssemblies => Version16.RequiredAssemblies;

    public string ExecutablePath(string gameDirectory)
        => Path.Combine(gameDirectory, ExecutableName);

    public string AssemblyDirectory(string gameDirectory)
        => string.IsNullOrWhiteSpace(AssemblyDirectoryRelativePath)
            ? gameDirectory
            : Path.Combine(gameDirectory, AssemblyDirectoryRelativePath);

    public bool UsesNetFramework => Version == "1.6";

    public bool UsesModernRuntime => Version != "1.6";

    public bool HasGameStructure(string gameDirectory)
    {
        if (!Directory.Exists(gameDirectory)
            || !File.Exists(ExecutablePath(gameDirectory))
            || !Directory.Exists(Path.Combine(gameDirectory, "Data")))
            return false;

        var assemblyDirectory = AssemblyDirectory(gameDirectory);
        return RequiredAssemblies.All(assembly => File.Exists(Path.Combine(assemblyDirectory, assembly)));
    }

    public static bool TryGet(string? version, out RimWorldGameLayout layout)
    {
        if (string.Equals(version, "1.6", StringComparison.OrdinalIgnoreCase))
        {
            layout = Version16;
            return true;
        }
        if (System.Version.TryParse(version, out var parsed)
            && parsed.Build < 0
            && parsed.Revision < 0
            && parsed >= new System.Version(1, 7))
        {
            layout = Version17;
            return true;
        }
        layout = null!;
        return false;
    }

    public static string? DetectVersion(string gameDirectory)
    {
        var layout = DetectLayout(gameDirectory);
        if (layout is null) return null;
        var assemblyPath = Path.Combine(layout.AssemblyDirectory(gameDirectory), "Assembly-CSharp.dll");
        var version = ReadAssemblyVersion(assemblyPath);
        return version is null ? null : $"{version.Major}.{version.Minor}";
    }

    private static RimWorldGameLayout? DetectLayout(string gameDirectory)
    {
        var matches = new[] { Version16, Version17 }
            .Where(layout => layout.HasGameStructure(gameDirectory))
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public static string? ValidateGameDirectory(string? gameDirectory, string? expectedVersion)
    {
        if (string.IsNullOrWhiteSpace(expectedVersion))
            return DetectLayout(gameDirectory ?? "") is null ? "无法识别游戏版本或安装结构" : null;
        if (!TryGet(expectedVersion, out var expected))
            return $"不支持 game_version={expectedVersion ?? "未配置"}；支持 {SupportedVersionDescription}";
        if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
            return "游戏目录不存在";

        var detected = DetectLayout(gameDirectory);
        if (detected is null)
            return "无法识别游戏布局；目录可能不完整或不属于支持的 RimWorld 版本";
        if (!detected.Version.Equals(expected.Version, StringComparison.OrdinalIgnoreCase))
            return $"配置目标为 RimWorld {expectedVersion}（{expected.Version} 布局），但目录结构识别为 {detected.Version} 布局";
        if (!expected.HasGameStructure(gameDirectory))
            return $"目录不符合 RimWorld {expectedVersion}（{expected.Version} 布局）的安装结构";
        var actualVersion = DetectVersion(gameDirectory);
        if (actualVersion is null)
        {
            if (expected.Version == "1.6") return null;
            return "无法从 Assembly-CSharp.dll 读取 RimWorld 版本";
        }
        if (!System.Version.TryParse(expectedVersion, out var expectedRelease)
            || !System.Version.TryParse(actualVersion, out var actualRelease)
            || expectedRelease.Major != actualRelease.Major
            || expectedRelease.Minor != actualRelease.Minor)
            return $"配置目标为 RimWorld {expectedVersion}，但游戏程序集版本为 {actualVersion}";
        return null;
    }

    private static Version? ReadAssemblyVersion(string assemblyPath)
    {
        try
        {
            var version = AssemblyName.GetAssemblyName(assemblyPath).Version;
            if (version is not null && version.Major > 0) return version;
        }
        catch { }

        try
        {
            var fileVersion = FileVersionInfo.GetVersionInfo(assemblyPath).FileVersion;
            return System.Version.TryParse(fileVersion, out var version) ? version : null;
        }
        catch { return null; }
    }
}
