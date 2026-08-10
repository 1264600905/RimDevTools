using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace RimDevTools;

public sealed record SteamResolverInput(IReadOnlyList<string> RegistryInstallPaths, IReadOnlyList<string> StandardInstallPaths);
public sealed record SteamDiagnostic(string Path, string Message);
public sealed record SteamInstallationResult(
    string? SteamRoot,
    string? SteamExecutable,
    IReadOnlyList<string> LibraryRoots,
    string? RimWorldDirectory,
    string? RimWorldSource,
    string? AppManifestPath,
    IReadOnlyList<string> WorkshopPaths,
    IReadOnlyList<SteamDiagnostic> Diagnostics);

public sealed record SteamSessionStatus(
    bool IsRunning,
    bool IsLoggedIn,
    string? SteamRoot,
    string? LoginUsersPath,
    string Code,
    string Message);

public static class SteamSessionGuard
{
    // Tests inject a deterministic probe; production only requires the Steam process to be running.
    internal static Func<SteamSessionStatus>? ProbeOverride { get; set; }

    public static SteamSessionStatus CheckForLaunch()
        => ProbeOverride?.Invoke() ?? Check();

    public static SteamSessionStatus Check(string? steamRoot = null, Func<bool>? isRunning = null)
    {
        steamRoot ??= SteamInstallationResolver.Resolve(null, []).SteamRoot;
        var running = isRunning?.Invoke() ?? IsSteamRunning();
        if (!running)
            return new(false, false, steamRoot, null, "STEAM_NOT_RUNNING", "Steam 未运行，拒绝启动 RimWorld");
        return new(true, true, steamRoot, null, "STEAM_RUNNING", "Steam 已运行");
    }

    public static SteamSessionStatus Evaluate(bool isRunning, string? steamRoot, string? loginUsersVdf)
    {
        if (!isRunning) return new(false, false, steamRoot, null, "STEAM_NOT_RUNNING", "Steam 未运行，拒绝启动 RimWorld");
        return new(true, true, steamRoot, null, "STEAM_RUNNING", "Steam 已运行");
    }

    private static bool IsSteamRunning()
    {
        try { return Process.GetProcessesByName("steam").Length > 0; }
        catch { return false; }
    }
}

public static class SteamInstallationResolver
{
    private const string RimWorldAppId = "294100";

    public static SteamInstallationResult Resolve(string? configuredRimWorldDirectory, IEnumerable<string> configuredWorkshopPaths, SteamResolverInput? input = null)
    {
        input ??= DefaultInput();
        var diagnostics = new List<SteamDiagnostic>();
        var steamRoots = input.RegistryInstallPaths.Concat(input.StandardInstallPaths)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePath)
            .Where(path => path is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var steamRoot = steamRoots.FirstOrDefault(path => File.Exists(Path.Combine(path, "steam.exe")) || File.Exists(Path.Combine(path, "steamapps", "libraryfolders.vdf")));
        var libraries = new List<string>();
        if (steamRoot is not null)
        {
            libraries.Add(steamRoot);
            var libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (File.Exists(libraryFile))
            {
                try
                {
                    var root = ValveKeyValues.Load(libraryFile).FirstOrDefault(value => value.Name.Equals("libraryfolders", StringComparison.OrdinalIgnoreCase));
                    if (root is null) throw new InvalidDataException("缺少 libraryfolders 根对象");
                    foreach (var entry in root.Children)
                    {
                        var path = entry.IsObject ? entry.Scalar("path") : entry.Value;
                        var normalized = NormalizePath(path);
                        if (normalized is not null) libraries.Add(normalized);
                    }
                }
                catch (Exception ex)
                {
                    diagnostics.Add(new SteamDiagnostic(libraryFile, ex.Message));
                }
            }
        }
        libraries = libraries.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        string? discoveredGame = null;
        string? manifestPath = null;
        foreach (var library in libraries)
        {
            var candidateManifest = Path.Combine(library, "steamapps", $"appmanifest_{RimWorldAppId}.acf");
            if (!File.Exists(candidateManifest)) continue;
            try
            {
                var appState = ValveKeyValues.Load(candidateManifest).FirstOrDefault(value => value.Name.Equals("AppState", StringComparison.OrdinalIgnoreCase));
                if (appState?.Scalar("appid") != RimWorldAppId) throw new InvalidDataException("appmanifest 的 appid 不是 294100");
                if (!long.TryParse(appState.Scalar("StateFlags"), out var stateFlags) || (stateFlags & 4) == 0) throw new InvalidDataException("appmanifest 未标记为完整安装");
                var installDir = appState.Scalar("installdir");
                if (string.IsNullOrWhiteSpace(installDir)) throw new InvalidDataException("appmanifest 缺少 installdir");
                var candidate = Path.Combine(library, "steamapps", "common", installDir);
                if (!HasGameStructure(candidate)) throw new InvalidDataException($"RimWorld 安装结构不完整: {candidate}");
                discoveredGame = Path.GetFullPath(candidate);
                manifestPath = candidateManifest;
                break;
            }
            catch (Exception ex)
            {
                diagnostics.Add(new SteamDiagnostic(candidateManifest, ex.Message));
            }
        }

        var configuredGame = NormalizePath(configuredRimWorldDirectory);
        var configuredGameValid = configuredGame is not null && HasGameStructure(configuredGame);
        if (configuredGame is not null && !configuredGameValid) diagnostics.Add(new SteamDiagnostic(configuredGame, "配置的 rimworld_dir 不存在或安装结构不完整"));
        var game = configuredGameValid ? configuredGame : discoveredGame;
        var source = configuredGameValid ? "configured" : discoveredGame is not null ? "steam" : null;

        var workshopPaths = configuredWorkshopPaths
            .Select(NormalizePath)
            .Where(path => path is not null && Directory.Exists(path))
            .Cast<string>()
            .Concat(libraries.Select(library => Path.Combine(library, "steamapps", "workshop", "content", RimWorldAppId)).Where(Directory.Exists))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SteamInstallationResult(steamRoot, steamRoot is null ? null : Path.Combine(steamRoot, "steam.exe"), libraries, game, source, manifestPath, workshopPaths, diagnostics);
    }

    public static bool HasGameStructure(string path)
        => Directory.Exists(path)
            && File.Exists(Path.Combine(path, "RimWorldWin64.exe"))
            && Directory.Exists(Path.Combine(path, "RimWorldWin64_Data", "Managed"));

    private static SteamResolverInput DefaultInput()
    {
        var registry = new List<string>();
        foreach (var (hive, view, keyPath) in new[]
        {
            (RegistryHive.CurrentUser, RegistryView.Default, @"Software\Valve\Steam"),
            (RegistryHive.LocalMachine, RegistryView.Registry32, @"Software\Valve\Steam"),
            (RegistryHive.LocalMachine, RegistryView.Registry64, @"Software\Valve\Steam")
        })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(keyPath);
                var value = key?.GetValue("SteamPath") as string ?? key?.GetValue("InstallPath") as string;
                if (!string.IsNullOrWhiteSpace(value)) registry.Add(value);
            }
            catch { }
        }
        var standard = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steam")
        };
        return new SteamResolverInput(registry, standard);
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.GetFullPath(path.Replace(@"\\", @"\")); }
        catch { return null; }
    }
}
