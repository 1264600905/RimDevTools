using System.Diagnostics;
using System.Text.Json;

namespace RimDevTools;

internal sealed record UpdateInstallation(string Root)
{
    internal string Executable => Path.Combine(Root, ".tools", "RimDevTools.exe");
    internal string Config => Path.Combine(Root, "mod_config.jsonc");
    internal string Data => ProjectDataPaths.DirectoryPath(Root);
    internal string StatePath => ProjectDataPaths.File(Root, "tool-update-state.json");
    internal string CachePath => ProjectDataPaths.File(Root, "tool-update-check.json");
    internal string LeaseDirectory => Path.Combine(Data, "tool-command-leases");
    internal string CurrentVersion => typeof(CliApplication).Assembly.GetName().Version!.ToString();
    internal static Func<UpdateInstallation?>? LocateOverride;

    internal static UpdateInstallation? Locate()
    {
        if (LocateOverride is not null) return LocateOverride();
        var executable = Environment.ProcessPath;
        if (executable is null || !Path.GetFileName(executable).Equals("RimDevTools.exe", StringComparison.OrdinalIgnoreCase)) return null;
        var tools = Path.GetDirectoryName(executable)!;
        if (!Path.GetFileName(tools).Equals(".tools", StringComparison.OrdinalIgnoreCase)) return null;
        var root = Path.GetDirectoryName(tools)!;
        if (!File.Exists(Path.Combine(root, ToolPackageManifest.RelativePath))) return null;
        // A formal tool package has a fixed layout. Development source trees are excluded independently of Git.
        if (File.Exists(Path.Combine(root, "RimDevTools", "RimDevTools.csproj")) || Directory.Exists(Path.Combine(root, "components", "ProjectTemplate"))) return null;
        PathSafety.EnsureNoReparsePoints(root, executable);
        return new(root);
    }

    internal UpdateState? ReadState()
        => File.Exists(StatePath) ? JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(StatePath), JsonDefaults.Storage)
            ?? throw new InvalidDataException("更新状态为空") : null;

    internal void WriteState(UpdateState state)
        => AtomicFile.WriteAllTextUsingMoveReplacement(StatePath, JsonSerializer.Serialize(state, JsonDefaults.Storage));

    internal IDisposable? EnterBusiness(out CliError? error)
    {
        using var gate = TryGate();
        if (gate is null) { error = new("TOOL_UPDATE_BUSY", "工具安装正在更新，请稍后重试。"); return null; }
        var state = ReadState();
        if (state?.BlocksBusiness == true)
        {
            error = new("TOOL_UPDATE_RECOVERY_REQUIRED", "工具安装正在更新或需要恢复，请运行 update status。", new { state.Status, state.TransactionDirectory, recovery = state.RecoveryCommand });
            return null;
        }
        PathSafety.EnsureNoReparsePoints(Root, LeaseDirectory);
        Directory.CreateDirectory(LeaseDirectory);
        var path = Path.Combine(LeaseDirectory, Guid.NewGuid().ToString("N") + ".lease");
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        error = null;
        return stream;
    }

    internal bool HasBusinessLeases()
    {
        if (!Directory.Exists(LeaseDirectory)) return false;
        PathSafety.EnsureNoReparsePoints(Root, LeaseDirectory);
        foreach (var path in Directory.EnumerateFiles(LeaseDirectory, "*.lease"))
        {
            PathSafety.EnsureNoReparsePoints(Root, path);
            try { using var lease = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            catch (FileNotFoundException) { }
            catch (IOException) { return true; }
        }
        return false;
    }

    internal FileStream? TryGate()
    {
        var path = ProjectDataPaths.File(Root, "tool-install.gate");
        Directory.CreateDirectory(Data);
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return null; }
    }

    internal static bool IsProcessAlive(int pid, long startTicks)
    {
        if (pid <= 0) return false;
        try { using var process = Process.GetProcessById(pid); return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == startTicks; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return true; }
    }
}

internal sealed class UpdateState
{
    public string Status { get; set; } = "none";
    public string TransactionId { get; set; } = "";
    public string TransactionDirectory { get; set; } = "";
    public string CurrentVersion { get; set; } = "";
    public string TargetVersion { get; set; } = "";
    public int ParentPid { get; set; }
    public long ParentStartedUtcTicks { get; set; }
    public int HelperPid { get; set; }
    public long HelperStartedUtcTicks { get; set; }
    public string? Error { get; set; }
    public string RecoveryCommand { get; set; } = "";
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool BlocksBusiness => Status is "scheduled" or "applying" or "recoveryRequired";
}
