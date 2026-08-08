using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace RimDevTools;

public sealed record ToolModManifestComponent(
    string Folder,
    string PackageId,
    string Version,
    string PrimaryAssembly,
    string ContentHash);

public sealed record ToolModManifest(int SchemaVersion, IReadOnlyList<ToolModManifestComponent> Components);

public sealed record ToolModInstallationPlan(
    ToolModManifestComponent Source,
    string SourceDirectory,
    string TargetDirectory,
    string? TargetVersion,
    string SourceHash,
    string? TargetHash,
    string Action,
    string? Reason);

public sealed record ToolModInstallationResult(
    string PackageId,
    string SourceDirectory,
    string TargetDirectory,
    string SourceVersion,
    string? TargetVersion,
    string SourceHash,
    string? TargetHash,
    string Action,
    string? Reason);

public sealed record ToolModDeploymentPlan(
    string ModsPath,
    string ManifestPath,
    IReadOnlyList<ToolModInstallationPlan> Components,
    IReadOnlyList<string> Warnings);

internal sealed record ExistingToolMod(string PackageId, string Directory);

public static class ToolModManifestReader
{
    private static readonly HashSet<string> ExpectedPackageIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "trigger.logsyncmod"
    };
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static ToolModManifest Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("工具 Mod 清单不存在", path);
        var manifest = JsonSerializer.Deserialize<ToolModManifest>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("工具 Mod 清单为空");
        if (manifest.SchemaVersion != 1 || manifest.Components is null || manifest.Components.Count == 0)
            throw new InvalidDataException("工具 Mod 清单 schemaVersion 必须为 1 且至少包含一个组件");

        var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in manifest.Components)
        {
            if (string.IsNullOrWhiteSpace(component.Folder)
                || string.IsNullOrWhiteSpace(component.PackageId)
                || string.IsNullOrWhiteSpace(component.Version)
                || string.IsNullOrWhiteSpace(component.PrimaryAssembly)
                || !component.ContentHash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                || !packageIds.Add(component.PackageId))
                throw new InvalidDataException("工具 Mod 清单包含无效或重复组件");
            if (Path.IsPathRooted(component.Folder) || component.Folder.Contains("..", StringComparison.Ordinal)
                || component.Folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException($"工具 Mod 文件夹名无效: {component.Folder}");
            if (Path.IsPathRooted(component.PrimaryAssembly) || component.PrimaryAssembly.Contains("..", StringComparison.Ordinal))
                throw new InvalidDataException($"工具 Mod 主程序集路径无效: {component.PrimaryAssembly}");
        }
        if (packageIds.Count != ExpectedPackageIds.Count || !packageIds.SetEquals(ExpectedPackageIds))
            throw new InvalidDataException("工具 Mod 清单必须且只能包含 LogSyncMod");
        return manifest;
    }
}

public static class DirectoryHasher
{
    public static string Compute(string root, IEnumerable<string>? excludedRelativePaths = null)
    {
        var fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot)) throw new DirectoryNotFoundException(fullRoot);
        PathSafety.EnsureNoReparsePoints(fullRoot, fullRoot);
        var excluded = new HashSet<string>(
            (excludedRelativePaths ?? Array.Empty<string>()).Select(NormalizeRelative),
            StringComparer.OrdinalIgnoreCase);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in EnumerateFiles(fullRoot).OrderBy(path => Path.GetRelativePath(fullRoot, path), StringComparer.OrdinalIgnoreCase))
        {
            var relative = NormalizeRelative(Path.GetRelativePath(fullRoot, file));
            if (excluded.Contains(relative)) continue;
            PathSafety.EnsureNoReparsePoints(fullRoot, file);
            var pathBytes = Encoding.UTF8.GetBytes(relative);
            var info = new FileInfo(file);
            var lengthBytes = BitConverter.GetBytes(info.Length);
            hash.AppendData(pathBytes);
            hash.AppendData(new byte[] { 0 });
            hash.AppendData(lengthBytes);
            hash.AppendData(new byte[] { 0 });
            using var stream = File.OpenRead(file);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) hash.AppendData(buffer, 0, read);
            hash.AppendData(new byte[] { 0 });
        }
        return "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            var attributes = File.GetAttributes(entry);
            if (attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException($"拒绝使用 Reparse Point: {entry}");
            if (attributes.HasFlag(FileAttributes.Directory))
            {
                foreach (var nested in EnumerateFiles(entry)) yield return nested;
            }
            else yield return entry;
        }
    }

    public static string NormalizeRelative(string path)
        => path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Replace(Path.DirectorySeparatorChar, '/');
}

public static class ToolModVersion
{
    public static string? Read(string modDirectory, string primaryAssemblyRelative)
    {
        return ReadAbout(modDirectory) ?? ReadAssembly(modDirectory, primaryAssemblyRelative);
    }

    public static string? ReadAbout(string modDirectory)
    {
        var about = Path.Combine(modDirectory, "About", "About.xml");
        if (File.Exists(about))
        {
            try
            {
                var value = XDocument.Load(about).Root?.Elements()
                    .FirstOrDefault(x => x.Name.LocalName.Equals("modVersion", StringComparison.OrdinalIgnoreCase))?.Value.Trim();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            catch { }
        }
        return null;
    }

    public static string? ReadAssembly(string modDirectory, string primaryAssemblyRelative)
    {
        var assembly = Path.Combine(modDirectory, primaryAssemblyRelative.Replace('/', Path.DirectorySeparatorChar));
        try { return File.Exists(assembly) ? System.Reflection.AssemblyName.GetAssemblyName(assembly).Version?.ToString() : null; }
        catch { return null; }
    }

    public static int Compare(string left, string right)
    {
        if (!Version.TryParse(left, out var leftVersion) || !Version.TryParse(right, out var rightVersion))
            throw new InvalidDataException($"无法比较工具 Mod 版本: {left}, {right}");
        var comparison = leftVersion.Major.CompareTo(rightVersion.Major);
        if (comparison != 0) return comparison;
        comparison = leftVersion.Minor.CompareTo(rightVersion.Minor);
        return comparison != 0 ? comparison : Math.Max(leftVersion.Build, 0).CompareTo(Math.Max(rightVersion.Build, 0));
    }
}

public static class ToolModInstaller
{
    private static readonly string[] ExcludedTargetFiles = ["PublishedFileId.txt"];

    public static ToolModDeploymentPlan Plan(string dataRoot, string modsPath)
    {
        var manifestPath = Path.Combine(dataRoot, "tool-mods.manifest.json");
        var manifest = ToolModManifestReader.Load(manifestPath);
        var fullDataRoot = Path.GetFullPath(dataRoot);
        var fullModsPath = Path.GetFullPath(modsPath);
        if (!Directory.Exists(fullDataRoot)) throw new DirectoryNotFoundException(fullDataRoot);
        if (!Directory.Exists(fullModsPath)) throw new DirectoryNotFoundException(fullModsPath);
        PathSafety.EnsureNoReparsePoints(fullDataRoot, fullDataRoot);
        PathSafety.EnsureNoReparsePoints(fullModsPath, fullModsPath);

        var existing = ScanInstalled(fullModsPath);
        var warnings = new List<string>();
        var plans = new List<ToolModInstallationPlan>();
        foreach (var source in manifest.Components)
        {
            var sourceDirectory = Path.Combine(fullDataRoot, source.Folder);
            if (!Directory.Exists(sourceDirectory)) throw new ToolModDeploymentException("SOURCE_COMPONENT_MISSING", $"发布包缺少工具 Mod: {source.Folder}");
            var aboutPackage = ReadPackageId(sourceDirectory);
            if (!string.Equals(aboutPackage, source.PackageId, StringComparison.OrdinalIgnoreCase))
                throw new ToolModDeploymentException("SOURCE_PACKAGE_ID_MISMATCH", $"工具 Mod packageId 不匹配: {source.Folder}");
            var sourceAboutVersion = ToolModVersion.ReadAbout(sourceDirectory);
            var sourceAssemblyVersion = ToolModVersion.ReadAssembly(sourceDirectory, source.PrimaryAssembly);
            if (sourceAboutVersion is null || sourceAssemblyVersion is null
                || ToolModVersion.Compare(source.Version, sourceAboutVersion) != 0
                || ToolModVersion.Compare(source.Version, sourceAssemblyVersion) != 0)
                throw new ToolModDeploymentException("SOURCE_VERSION_MISMATCH", $"工具 Mod 清单、About 或主程序集版本不匹配: {source.Folder}");
            var sourceHash = DirectoryHasher.Compute(sourceDirectory);
            if (!string.Equals(sourceHash, source.ContentHash, StringComparison.OrdinalIgnoreCase))
                throw new ToolModDeploymentException("SOURCE_HASH_MISMATCH", $"工具 Mod 清单哈希不匹配: {source.Folder}");

            var matches = existing.Where(x => x.PackageId.Equals(source.PackageId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length > 1) throw new ToolModDeploymentException("DUPLICATE_PACKAGE_ID", $"目标 Mods 中存在重复 packageId: {source.PackageId}");
            var installed = matches.SingleOrDefault();
            var targetDirectory = installed?.Directory ?? Path.Combine(fullModsPath, source.Folder);
            if (installed is null && Directory.Exists(targetDirectory))
                throw new ToolModDeploymentException("TARGET_FOLDER_OCCUPIED", $"目标文件夹已存在但 packageId 不匹配: {targetDirectory}");
            string? targetHash = null;
            if (installed is not null) targetHash = DirectoryHasher.Compute(installed.Directory, ExcludedTargetFiles);
            var targetVersion = installed is null ? null : ToolModVersion.Read(installed.Directory, source.PrimaryAssembly);
            var action = "installed";
            string? reason = null;
            if (installed is not null && string.Equals(sourceHash, targetHash, StringComparison.OrdinalIgnoreCase))
            {
                action = "unchanged";
                reason = "内容哈希相同";
            }
            else if (installed is not null && targetVersion is null)
            {
                action = "unknown_target_version";
                reason = "目标版本不可识别，拒绝覆盖";
            }
            else if (installed is not null)
            {
                var comparison = ToolModVersion.Compare(source.Version, targetVersion!);
                if (comparison < 0)
                {
                    action = "target_newer";
                    reason = "目标版本更新，跳过降级";
                }
                else if (comparison == 0)
                {
                    action = "same_version_conflict";
                    reason = "版本相同但内容哈希不同，拒绝覆盖";
                }
                else
                {
                    action = "updated";
                    reason = "源版本更新";
                }
            }
            plans.Add(new ToolModInstallationPlan(source, sourceDirectory, targetDirectory, targetVersion, sourceHash, targetHash, action, reason));
        }
        return new ToolModDeploymentPlan(fullModsPath, manifestPath, plans, warnings);
    }

    public static IReadOnlyList<ToolModInstallationResult> Install(ToolModDeploymentPlan plan, string runId)
    {
        var actionable = plan.Components.Where(x => x.Action is "installed" or "updated").ToArray();
        if (plan.Components.Any(x => x.Action is "same_version_conflict" or "unknown_target_version" or "duplicate_package_id"))
            throw new ToolModDeploymentException("INSTALL_PLAN_CONFLICT", "工具 Mod 安装计划包含需要人工处理的冲突");
        if (actionable.Length == 0) return plan.Components.Select(ToResult).ToArray();

        var backups = new List<(string Target, string Backup)>();
        var installedTargets = new List<string>();
        var stagingRoot = Path.Combine(plan.ModsPath, $".rimdev-tools-staging-{runId}");
        var backupRoot = Path.Combine(plan.ModsPath, $".rimdev-tools-backup-{runId}");
        var committed = false;
        try
        {
            foreach (var item in actionable)
            {
                var staging = Path.Combine(stagingRoot, item.Source.Folder);
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                CopyDirectory(item.SourceDirectory, staging);
                var stagedHash = DirectoryHasher.Compute(staging);
                if (!stagedHash.Equals(item.SourceHash, StringComparison.OrdinalIgnoreCase)) throw new IOException($"暂存哈希校验失败: {item.Source.Folder}");
                PreserveTargetFiles(item.TargetDirectory, staging);
            }

            foreach (var item in actionable)
            {
                var staging = Path.Combine(stagingRoot, item.Source.Folder);
                var backup = Path.Combine(backupRoot, item.Source.Folder);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                if (Directory.Exists(item.TargetDirectory))
                {
                    Directory.Move(item.TargetDirectory, backup);
                    backups.Add((item.TargetDirectory, backup));
                }
                Directory.Move(staging, item.TargetDirectory);
                installedTargets.Add(item.TargetDirectory);
            }
            committed = true;
            if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, true);
            if (Directory.Exists(backupRoot)) Directory.Delete(backupRoot, true);
            return plan.Components.Select(ToResult).ToArray();
        }
        catch
        {
            if (committed) throw;
            foreach (var target in installedTargets) if (Directory.Exists(target)) Directory.Delete(target, true);
            foreach (var (target, backup) in backups.AsEnumerable().Reverse()) if (Directory.Exists(backup)) Directory.Move(backup, target);
            if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, true);
            if (Directory.Exists(backupRoot)) Directory.Delete(backupRoot, true);
            throw;
        }
    }

    private static ToolModInstallationResult ToResult(ToolModInstallationPlan item)
        => new(item.Source.PackageId, item.SourceDirectory, item.TargetDirectory, item.Source.Version, item.TargetVersion, item.SourceHash, item.TargetHash, item.Action, item.Reason);

    private static List<ExistingToolMod> ScanInstalled(string modsPath)
    {
        var result = new List<ExistingToolMod>();
        foreach (var directory in Directory.EnumerateDirectories(modsPath))
        {
            PathSafety.EnsureNoReparsePoints(modsPath, directory);
            var packageId = ReadPackageId(directory);
            if (string.IsNullOrWhiteSpace(packageId)) continue;
            result.Add(new ExistingToolMod(packageId, Path.GetFullPath(directory)));
        }
        return result;
    }

    private static string? ReadPackageId(string directory)
    {
        var about = Path.Combine(directory, "About", "About.xml");
        if (!File.Exists(about)) return null;
        try { return XDocument.Load(about).Root?.Elements().FirstOrDefault(x => x.Name.LocalName.Equals("packageId", StringComparison.OrdinalIgnoreCase))?.Value.Trim(); }
        catch { return null; }
    }

    private static void CopyDirectory(string source, string destination)
    {
        PathSafety.EnsureNoReparsePoints(source, source);
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            PathSafety.EnsureNoReparsePoints(source, entry);
            var target = Path.Combine(destination, Path.GetFileName(entry));
            if (Directory.Exists(entry)) CopyDirectory(entry, target);
            else File.Copy(entry, target, false);
        }
    }

    private static void PreserveTargetFiles(string targetDirectory, string stagingDirectory)
    {
        if (!Directory.Exists(targetDirectory)) return;
        foreach (var relative in ExcludedTargetFiles)
        {
            var source = Path.Combine(targetDirectory, relative);
            if (!File.Exists(source)) continue;
            PathSafety.EnsureNoReparsePoints(targetDirectory, source);
            File.Copy(source, Path.Combine(stagingDirectory, relative), true);
        }
    }
}

public sealed class ToolModDeploymentException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
