using System.IO.Compression;
using System.Text.Json;

namespace RimDevTools;

internal sealed class ToolUpdatePlan
{
    public string Root { get; set; } = "";
    public string TransactionId { get; set; } = "";
    public string CurrentVersion { get; set; } = "";
    public string TargetVersion { get; set; } = "";
    public string ConfigHash { get; set; } = "";
    public List<ToolUpdateChange> Changes { get; set; } = [];
    public string? Outcome { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string DirectoryPath => Path.Combine(new UpdateInstallation(Root).Data, "tool-update-" + TransactionId);
    [System.Text.Json.Serialization.JsonIgnore]
    public string JournalPath => Path.Combine(DirectoryPath, "journal.json");
    internal string Staged(string relative) => Path.Combine(DirectoryPath, "stage", relative);
    internal string Backup(string relative) => Path.Combine(DirectoryPath, "backup", relative);
    internal string Destination(string relative) => Path.Combine(Root, relative);
    internal void Save() => AtomicFile.WriteAllTextUsingMoveReplacement(JournalPath, JsonSerializer.Serialize(this, JsonDefaults.Storage));

    internal static ToolUpdatePlan Prepare(UpdateInstallation installation, ToolRelease release, string transactionId)
    {
        var plan = new ToolUpdatePlan { Root = installation.Root, TransactionId = transactionId, CurrentVersion = installation.CurrentVersion, TargetVersion = release.Version };
        var archive = Path.Combine(plan.DirectoryPath, "package.zip");
        Extract(archive, plan.Staged(""));
        var target = ToolPackageManifest.Load(plan.Staged(""));
        if (ToolReleaseSource.ParseVersion(target.ToolVersion) != ToolReleaseSource.ParseVersion(release.Version)) throw new InvalidDataException("包内版本与目标发行不一致");
        if (target.RuntimeMajor > Environment.Version.Major) throw new InvalidDataException($"需要 .NET {target.RuntimeMajor} Runtime，请先安装再更新");
        var config = ProjectConfig.Load(installation.Config);
        if (!target.SupportedGameVersions.Contains(config.GameVersion)) throw new InvalidDataException("工具包不支持项目的 game_version");
        var old = ToolPackageManifest.Load(installation.Root);
        if (ToolReleaseSource.ParseVersion(old.ToolVersion) != ToolReleaseSource.ParseVersion(installation.CurrentVersion)) throw new InvalidDataException("当前安装版本与发行基线不一致");
        var conflicts = new List<string>();
        foreach (var file in old.Files)
        {
            var path = plan.Destination(file.Path);
            PathSafety.EnsureNoReparsePoints(plan.Root, path);
            if (!File.Exists(path) || new FileInfo(path).Length != file.Size || !ToolPackageManifest.Hash(path).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) conflicts.Add(file.Path);
        }
        var oldFiles = old.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var file in target.Files)
        {
            var path = plan.Destination(file.Path);
            PathSafety.EnsureNoReparsePoints(plan.Root, path);
            if (!oldFiles.ContainsKey(file.Path) && (File.Exists(path) || Directory.Exists(path))) conflicts.Add(file.Path);
            if (!oldFiles.TryGetValue(file.Path, out var prior) || !prior.Sha256.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                plan.Changes.Add(new(file.Path, prior?.Sha256, file.Sha256));
        }
        foreach (var removed in old.Files.Where(f => !target.Files.Any(n => n.Path.Equals(f.Path, StringComparison.OrdinalIgnoreCase))))
            plan.Changes.Add(new(removed.Path, removed.Sha256, null));
        if (conflicts.Count > 0) throw new ToolUpdateConflictException(conflicts);
        var migration = JsoncConfigMigration.Prepare(File.ReadAllBytes(installation.Config), target);
        plan.ConfigHash = migration.OriginalHash;
        if (migration.Changed)
        {
            AtomicFile.WriteAllBytes(plan.Staged("mod_config.jsonc"), migration.Content);
            plan.Changes.Add(new("mod_config.jsonc", migration.OriginalHash, ToolPackageManifest.Hash(plan.Staged("mod_config.jsonc"))));
        }
        plan.Changes.Add(new(ToolPackageManifest.RelativePath, ToolPackageManifest.Hash(plan.Destination(ToolPackageManifest.RelativePath)), ToolPackageManifest.Hash(plan.Staged(ToolPackageManifest.RelativePath))));
        return plan;
    }

    internal static void Extract(string archive, string destination)
    {
        using var zip = ZipFile.OpenRead(archive);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
            if (!names.Add(entry.FullName) || entry.FullName.Contains('\\') || ((entry.ExternalAttributes >> 16) & 0xF000) is 0xA000 || (entry.ExternalAttributes & 0x400) != 0)
                throw new InvalidDataException("工具包含重复路径或不允许的链接");
        var manifestEntry = zip.GetEntry(ToolPackageManifest.RelativePath) ?? throw new InvalidDataException("工具包缺少发行清单");
        if (manifestEntry.Length > 2_000_000) throw new InvalidDataException("工具清单过大");
        ToolPackageManifest manifest;
        using (var input = manifestEntry.Open()) manifest = JsonSerializer.Deserialize<ToolPackageManifest>(input, JsonDefaults.Storage) ?? throw new InvalidDataException("工具清单为空");
        manifest.Validate();
        var allowed = manifest.Files.Select(f => f.Path).Append(ToolPackageManifest.RelativePath).Append("mod_config.jsonc").ToHashSet(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/') && (allowed.Any(f => f.StartsWith(entry.FullName, StringComparison.Ordinal))
                || ToolPackageManifest.IsBundledSkillPath(entry.FullName + "SKILL.md"))) continue;
            var bundledSkill = ToolPackageManifest.IsBundledSkillPath(entry.FullName);
            if ((!allowed.Contains(entry.FullName) && !bundledSkill) || entry.Length > 512L * 1024 * 1024 || (total += entry.Length) > 1024L * 1024 * 1024)
                throw new InvalidDataException($"工具包含不允许的内容或大小: {entry.FullName}");
            if (bundledSkill) continue;
            var path = Path.GetFullPath(entry.FullName, destination);
            PathSafety.EnsureNoReparsePoints(destination, path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var source = entry.Open();
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[65536];
            long written = 0;
            int read;
            while ((read = source.Read(buffer)) > 0)
            {
                written += read;
                if (written > entry.Length) throw new InvalidDataException("ZIP 解压内容超过声明大小");
                output.Write(buffer, 0, read);
            }
            if (written != entry.Length) throw new InvalidDataException("ZIP 解压内容不完整");
        }
        manifest.VerifyFiles(destination);
    }

    internal void Validate()
    {
        if (!Guid.TryParseExact(TransactionId, "N", out _) || !Path.IsPathFullyQualified(Root) || !ToolReleaseSource.ValidHash(ConfigHash)) throw new InvalidDataException("更新事务身份无效");
        _ = ToolReleaseSource.ParseVersion(CurrentVersion);
        _ = ToolReleaseSource.ParseVersion(TargetVersion);
        PathSafety.EnsureNoReparsePoints(Root, DirectoryPath);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var change in Changes)
        {
            if ((!ToolPackageManifest.IsManagedPath(change.Path) && change.Path != ToolPackageManifest.RelativePath && change.Path != "mod_config.jsonc")
                || !paths.Add(change.Path) || (change.OldHash is not null && !ToolReleaseSource.ValidHash(change.OldHash))
                || (change.NewHash is not null && !ToolReleaseSource.ValidHash(change.NewHash)) || change.OldHash is null && change.NewHash is null)
                throw new InvalidDataException("更新事务含无效文件变更");
            PathSafety.EnsureNoReparsePoints(Root, Destination(change.Path));
            PathSafety.EnsureNoReparsePoints(DirectoryPath, Backup(change.Path));
            PathSafety.EnsureNoReparsePoints(DirectoryPath, Staged(change.Path));
        }
    }
}

internal sealed record ToolUpdateChange(string Path, string? OldHash, string? NewHash)
{
    public bool Applied { get; set; }
}

internal sealed class ToolUpdateConflictException(IReadOnlyList<string> files) : IOException("本地受管理文件已修改或缺少可信基线，请处理冲突后重试")
{
    internal IReadOnlyList<string> Files => files;
}
