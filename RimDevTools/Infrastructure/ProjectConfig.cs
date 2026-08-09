using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace RimDevTools;

public sealed class ProjectConfig
{
    [JsonPropertyName("config_version")] public int ConfigVersion { get; set; }
    [JsonPropertyName("mod_name")] public string? ModName { get; set; }
    [JsonPropertyName("game_version")] public string? GameVersion { get; set; }
    [JsonPropertyName("rimworld_dir")] public string? RimWorldDir { get; set; }
    [JsonPropertyName("mods_config_path")] public string? ModsConfigPath { get; set; }
    [JsonPropertyName("workshop_paths")] public List<string> WorkshopPaths { get; set; } = [];
    [JsonPropertyName("csproj_paths")] public List<string> CsprojPaths { get; set; } = [];
    [JsonPropertyName("sync_items")] public List<string> SyncItems { get; set; } = [];
    [JsonPropertyName("sync_source")] public bool SyncSource { get; set; }
    [JsonPropertyName("protected_files_regex")] public string? ProtectedFilesRegex { get; set; }
    [JsonPropertyName("pull_ignore_folders")] public List<string> PullIgnoreFolders { get; set; } = [];
    [JsonPropertyName("ilspycmd_path")] public string? IlspycmdPath { get; set; }
    [JsonPropertyName("log_keywords")] public List<string> LogKeywords { get; set; } = [];
    [JsonPropertyName("min_active_mods")] public List<string> MinActiveMods { get; set; } = [];
    [JsonPropertyName("timeouts")] public TimeoutsConfig Timeouts { get; set; } = new();
    [JsonPropertyName("retention")] public RetentionConfig Retention { get; set; } = new();
    [JsonPropertyName("shader_bundle")] public ShaderBundleConfig ShaderBundle { get; set; } = new();
    [JsonPropertyName("isolated_desktop")] public bool IsolatedDesktop { get; set; }

    public static ProjectConfig Load(string path)
    {
        var options = new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, PropertyNameCaseInsensitive = true };
        var config = JsonSerializer.Deserialize<ProjectConfig>(File.ReadAllText(path), options)
            ?? throw new InvalidDataException("配置文件为空");
        config.Normalize();
        return config;
    }

    public void Normalize()
    {
        WorkshopPaths ??= []; CsprojPaths ??= []; SyncItems ??= []; PullIgnoreFolders ??= [];
        LogKeywords ??= []; MinActiveMods ??= []; Timeouts ??= new(); Retention ??= new(); ShaderBundle ??= new();
    }

    public string? CurrentPackageId(string projectRoot)
    {
        var about = Path.Combine(projectRoot, "About", "About.xml");
        if (!File.Exists(about)) return null;
        try { return XDocument.Load(about).Root?.Element("packageId")?.Value.Trim(); }
        catch { return null; }
    }

    public string? ResolveModsConfigPath(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(ModsConfigPath)) return null;
        return Path.GetFullPath(ModsConfigPath, projectRoot);
    }
}

public sealed class TimeoutsConfig
{
    [JsonPropertyName("steam_launch_detect")] public int SteamLaunchDetect { get; set; } = 120;
    [JsonPropertyName("max_wait")] public int MaxWait { get; set; } = 600;
    [JsonPropertyName("existing_game_exit")] public int ExistingGameExit { get; set; } = 600;
    [JsonPropertyName("post_exit_delay")] public int PostExitDelay { get; set; } = 10;
}

public sealed class RetentionConfig
{
    [JsonPropertyName("max_log_files")] public int MaxLogFiles { get; set; } = 3;
    [JsonPropertyName("max_auto_test_runs")] public int MaxAutoTestRuns { get; set; } = 3;
}

public sealed class ShaderBundleConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("unity_project_path")] public string? UnityProjectPath { get; set; }
    [JsonPropertyName("unity_exe_path")] public string? UnityExePath { get; set; }
    [JsonPropertyName("build_method")] public string? BuildMethod { get; set; }
    [JsonPropertyName("bundle_name")] public string? BundleName { get; set; }
}
