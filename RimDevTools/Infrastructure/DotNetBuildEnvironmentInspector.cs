using System.Diagnostics;

namespace RimDevTools;

public sealed record DotNetBuildEnvironmentInput(string? DotNetPath, IReadOnlyList<string> InstalledSdks, IReadOnlyList<string> TargetingPackCandidates);
public sealed record DotNetBuildEnvironmentResult(string? DotNetPath, IReadOnlyList<string> InstalledSdks, string? TargetFrameworkVersion, string? TargetingPackPath)
{
    public bool HasDotNet => DotNetPath is not null;
    public bool HasSdk => InstalledSdks.Count > 0;
    public bool HasSupportedNetFrameworkTargetingPack => TargetingPackPath is not null;
}

public static class DotNetBuildEnvironmentInspector
{
    public static readonly IReadOnlyList<string> SupportedTargetFrameworkVersions = ["v4.8", "v4.7.2"];

    public static DotNetBuildEnvironmentResult Inspect(DotNetBuildEnvironmentInput input)
    {
        var targetingPack = SupportedTargetFrameworkVersions
            .Select(version => new
            {
                Version = version,
                Path = input.TargetingPackCandidates.FirstOrDefault(path =>
                    Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Equals(version, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(Path.Combine(path, "mscorlib.dll")))
            })
            .FirstOrDefault(candidate => candidate.Path is not null);

        return new(
            input.DotNetPath is not null && File.Exists(input.DotNetPath) ? Path.GetFullPath(input.DotNetPath) : null,
            input.InstalledSdks.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
            targetingPack?.Version,
            targetingPack?.Path);
    }

    public static DotNetBuildEnvironmentResult InspectCurrent()
    {
        var dotnet = FindOnPath(OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        var sdks = dotnet is null ? [] : ReadSdks(dotnet);
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Reference Assemblies", "Microsoft", "Framework", ".NETFramework", "v4.8"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Reference Assemblies", "Microsoft", "Framework", ".NETFramework", "v4.7.2"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Reference Assemblies", "Microsoft", "Framework", ".NETFramework", "v4.8"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Reference Assemblies", "Microsoft", "Framework", ".NETFramework", "v4.7.2")
        };
        return Inspect(new DotNetBuildEnvironmentInput(dotnet, sdks, candidates));
    }

    private static string[] ReadSdks(string dotnet)
    {
        try
        {
            var startInfo = new ProcessStartInfo(dotnet) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            startInfo.ArgumentList.Add("--list-sdks");
            using var process = Process.Start(startInfo);
            if (process is null) return [];
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return process.ExitCode == 0 ? output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
        }
        catch { return []; }
    }

    internal static string? FindOnPath(string fileName)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var path = Path.Combine(directory.Trim('"'), fileName);
                if (File.Exists(path)) return path;
            }
            catch { }
        }
        return null;
    }
}
