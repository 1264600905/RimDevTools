using System.Reflection;

namespace RimDevTools;

public static class HarmonyVersionInspector
{
    // Minimum AssemblyVersion required by the current project references.
    // It is stable and does not depend on a developer machine path.
    public static readonly Version MinimumSupportedVersion = new(2, 4, 1, 0);

    public static HarmonyVersionCheck Inspect(string? assemblyPath, Version? minimumVersion = null)
    {
        var required = minimumVersion ?? MinimumSupportedVersion;
        if (string.IsNullOrWhiteSpace(assemblyPath))
            return new("not_applicable", null, required, null, "未解析到 Harmony DLL");

        var fullPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(fullPath))
            return new("missing", null, required, fullPath, "未找到 0Harmony.dll");

        try
        {
            var installed = AssemblyName.GetAssemblyName(fullPath).Version;
            if (installed is null)
                return new("invalid", null, required, fullPath, "无法读取 0Harmony.dll 的 AssemblyVersion");

            if (installed < required)
                return new("stale", installed, required, fullPath, $"Harmony 版本过低（已安装 {installed}，要求至少 {required}），请尝试重新订阅 Harmony");

            return new("available", installed, required, fullPath);
        }
        catch (Exception ex)
        {
            return new("invalid", null, required, fullPath, $"无法读取 0Harmony.dll 版本: {ex.Message}");
        }
    }
}

public sealed record HarmonyVersionCheck(
    string Status,
    Version? InstalledVersion,
    Version RequiredVersion,
    string? Path,
    string? Message = null);
