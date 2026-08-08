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
    // Tests inject a deterministic probe; production always uses the real Steam process and VDF.
    internal static Func<SteamSessionStatus>? ProbeOverride { get; set; }

    public static SteamSessionStatus CheckForLaunch()
        => ProbeOverride?.Invoke() ?? Check();

    public static SteamSessionStatus Check(string? steamRoot = null, Func<bool>? isRunning = null)
    {
        steamRoot ??= SteamInstallationResolver.Resolve(null, []).SteamRoot;
        var running = isRunning?.Invoke() ?? IsSteamRunning();
        if (!running)
            return new(false, false, steamRoot, null, "STEAM_NOT_RUNNING", "Steam 未运行，拒绝启动 RimWorld");
        if (string.IsNullOrWhiteSpace(steamRoot))
            return new(true, false, null, null, "STEAM_INSTALLATION_UNKNOWN", "无法定位 Steam 安装目录，拒绝启动 RimWorld");

        var loginUsersPath = Path.Combine(steamRoot, "config", "loginusers.vdf");
        if (!File.Exists(loginUsersPath))
            return new(true, false, steamRoot, loginUsersPath, "STEAM_NOT_LOGGED_IN", "Steam 登录状态文件不存在，拒绝启动 RimWorld");
        try { return Evaluate(true, steamRoot, File.ReadAllText(loginUsersPath)) with { LoginUsersPath = loginUsersPath }; }
        catch (Exception ex) { return new(true, false, steamRoot, loginUsersPath, "STEAM_LOGIN_STATE_INVALID", $"无法读取 Steam 登录状态: {ex.Message}"); }
    }

    public static SteamSessionStatus Evaluate(bool isRunning, string? steamRoot, string? loginUsersVdf)
    {
        if (!isRunning) return new(false, false, steamRoot, null, "STEAM_NOT_RUNNING", "Steam 未运行，拒绝启动 RimWorld");
        if (string.IsNullOrWhiteSpace(loginUsersVdf)) return new(true, false, steamRoot, null, "STEAM_NOT_LOGGED_IN", "Steam 登录状态文件不存在，拒绝启动 RimWorld");
        try
        {
            var users = ValveKeyValues.Parse(loginUsersVdf).FirstOrDefault(value => value.Name.Equals("users", StringComparison.OrdinalIgnoreCase));
            var accounts = users?.Children
                .Where(user => user.IsObject && !string.IsNullOrWhiteSpace(user.Scalar("AccountName")))
                .ToArray() ?? [];
            var explicitlyRecent = accounts.Where(user => user.Scalar("MostRecent") == "1").ToArray();
            var candidates = explicitlyRecent.Length > 0
                ? explicitlyRecent
                : accounts
                    .Where(user => user.Scalar("RememberPassword") == "1" && ParseTimestamp(user.Scalar("Timestamp")) != long.MinValue)
                    .OrderByDescending(user => ParseTimestamp(user.Scalar("Timestamp")))
                    .Take(1)
                    .ToArray();
            var loggedIn = candidates.Any(user => user.Scalar("WantsOfflineMode") != "1");
            return loggedIn
                ? new(true, true, steamRoot, null, "STEAM_LOGGED_IN", "Steam 已运行且已登录")
                : new(true, false, steamRoot, null, "STEAM_NOT_LOGGED_IN", "Steam 未登录或处于离线模式，拒绝启动 RimWorld");
        }
        catch (Exception ex) { return new(true, false, steamRoot, null, "STEAM_LOGIN_STATE_INVALID", $"无法解析 Steam 登录状态: {ex.Message}"); }
    }

    private static long ParseTimestamp(string? value)
        => long.TryParse(value, out var timestamp) ? timestamp : long.MinValue;

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
