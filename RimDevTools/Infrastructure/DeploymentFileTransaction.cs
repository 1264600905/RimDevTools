using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace RimDevTools;

internal sealed record DeploymentTransactionManifest(
    int SchemaVersion,
    string Target,
    string? PackageId,
    string State,
    IReadOnlyList<string> OriginalFiles,
    IReadOnlyList<string> AddedFiles);

internal sealed record DeploymentFileProcess(int ProcessId, string Name);

internal sealed record DeploymentTargetBusyDetails(
    string Phase,
    IReadOnlyList<string> Paths,
    IReadOnlyList<DeploymentFileProcess> Processes);

public sealed record DeploymentFileCommitResult(
    int CheckedFileCount,
    int ReplacedFileCount,
    int RemovedFileCount,
    int ReplacedDirectoryCount,
    string? CleanupWarning);

internal sealed class DeploymentTargetBusyException(DeploymentTargetBusyDetails details, string message, Exception? inner = null)
    : IOException(message, inner)
{
    public DeploymentTargetBusyDetails Details { get; } = details;
}

internal sealed class DeploymentRollbackFailedException(string backupPath, Exception commitError, Exception rollbackError)
    : IOException($"部署失败且回滚未完成。备份已保留: {backupPath}", new AggregateException(commitError, rollbackError))
{
    public string BackupPath { get; } = backupPath;
}

internal static class DeploymentFileTransaction
{
    public const string ManifestFileName = ".rimdev-transaction.json";
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;

    public static DeploymentTransactionManifest? TryReadManifest(string directory, out string? error)
    {
        var path = Path.Combine(directory, ManifestFileName);
        error = null;
        if (!File.Exists(path)) return null;
        try
        {
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("部署事务记录是 Reparse Point");
            var manifest = JsonSerializer.Deserialize<DeploymentTransactionManifest>(File.ReadAllText(path), JsonDefaults.Storage)
                ?? throw new InvalidDataException("部署事务记录为空");
            if (manifest.SchemaVersion != 1 || manifest.State is not ("preparing" or "committing" or "committed"))
                throw new InvalidDataException("部署事务记录版本或状态无效");
            if (manifest.OriginalFiles is null || manifest.AddedFiles is null) throw new InvalidDataException("部署事务记录缺少文件清单");
            ValidateManifestPaths(manifest);
            return manifest;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    public static DeploymentTransactionManifest ReadManifest(string directory)
    {
        return TryReadManifest(directory, out var error) ?? throw new InvalidDataException(error ?? "部署事务记录不存在");
    }

    public static void EnsureNotBusyFiles(IEnumerable<string> paths, string phase)
    {
        var affected = paths.Where(File.Exists).Distinct(PathComparer()).ToArray();
        var busy = FindBusyResources(affected, []);
        if (busy.Count == 0) return;
        throw new DeploymentTargetBusyException(
            CreateBusyDetails(phase, busy),
            $"目标文件正在被占用: {string.Join(", ", busy)}");
    }

    public static DeploymentTargetBusyDetails CreateBusyDetails(string phase, IReadOnlyList<string> paths)
        => new(phase, paths, RestartManager.FindProcesses(paths));

    public static bool IsSharingViolation(Exception exception)
    {
        var code = exception.HResult & 0xFFFF;
        return code is ErrorSharingViolation or ErrorLockViolation;
    }

    public static DeploymentFileCommitResult Commit(string target, string staging, string backupDirectory, string? packageId, Regex? protectedRegex, bool removeStaleFiles = true)
    {
        var fullTarget = Path.GetFullPath(target);
        var fullStaging = Path.GetFullPath(staging);
        var fullBackup = Path.GetFullPath(backupDirectory);
        if (!Directory.Exists(fullTarget)) Directory.CreateDirectory(fullTarget);

        var targetFiles = EnumerateFiles(fullTarget, protectedRegex).ToHashSet(PathComparer());
        var targetDirectories = EnumerateDirectories(fullTarget, protectedRegex).ToHashSet(PathComparer());
        var stagedFiles = EnumerateFiles(fullStaging).ToHashSet(PathComparer());
        var stagedDirectories = EnumerateDirectories(fullStaging).ToHashSet(PathComparer());
        var replacements = stagedFiles
            .Where(relative => !IsProtected(relative, protectedRegex))
            .Where(relative => !targetFiles.Contains(relative) || !FilesEqual(FullPath(fullTarget, relative), FullPath(fullStaging, relative)))
            .OrderBy(x => x, PathComparer()).ToArray();
        var deletions = removeStaleFiles
            ? targetFiles.Where(relative => !stagedFiles.Contains(relative) || stagedDirectories.Contains(relative)).OrderBy(x => x, PathComparer()).ToArray()
            : [];
        var directoriesToReplace = removeStaleFiles ? targetDirectories.Where(stagedFiles.Contains)
            .OrderByDescending(x => x.Count(character => character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar))
            .ToArray() : [];
        var originals = replacements.Where(targetFiles.Contains).Concat(deletions).Distinct(PathComparer()).ToArray();
        var additions = replacements.Where(relative => !targetFiles.Contains(relative)).ToArray();

        if (replacements.Length == 0 && deletions.Length == 0 && directoriesToReplace.Length == 0)
            return new DeploymentFileCommitResult(0, 0, 0, 0, null);

        var absoluteAffected = originals.Select(relative => FullPath(fullTarget, relative)).ToArray();
        var absoluteDirectories = directoriesToReplace.Select(relative => FullPath(fullTarget, relative)).ToArray();
        var busyResources = FindBusyResources(absoluteAffected, absoluteDirectories);
        if (busyResources.Count > 0)
        {
            throw new DeploymentTargetBusyException(
                CreateBusyDetails("preflight", busyResources),
                $"目标文件或目录正在被占用: {string.Join(", ", busyResources)}");
        }

        Directory.CreateDirectory(fullBackup);
        var manifest = new DeploymentTransactionManifest(1, fullTarget, packageId, "preparing", originals, additions);
        string? activePath = null;
        try
        {
            WriteManifest(fullBackup, manifest);
            foreach (var relative in originals)
            {
                var source = FullPath(fullTarget, relative);
                activePath = source;
                var backup = FullPath(Path.Combine(fullBackup, "files"), relative);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(source, backup, false);
                File.SetAttributes(backup, File.GetAttributes(source));
            }

            manifest = manifest with { State = "committing" };
            WriteManifest(fullBackup, manifest);

            try
            {
                foreach (var relative in deletions)
                {
                    var destination = FullPath(fullTarget, relative);
                    activePath = destination;
                    PathSafety.EnsureNoReparsePoints(fullTarget, destination);
                    if (File.Exists(destination))
                    {
                        ClearReadOnly(destination);
                        File.Delete(destination);
                    }
                }

                foreach (var relative in directoriesToReplace)
                {
                    var destination = FullPath(fullTarget, relative);
                    activePath = destination;
                    PathSafety.EnsureNoReparsePoints(fullTarget, destination);
                    if (Directory.Exists(destination)) Directory.Delete(destination, true);
                }

                foreach (var relative in replacements)
                {
                    var source = FullPath(fullStaging, relative);
                    var destination = FullPath(fullTarget, relative);
                    activePath = destination;
                    PathSafety.EnsureNoReparsePoints(fullTarget, destination);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    if (File.Exists(destination))
                    {
                        ClearReadOnly(destination);
                        File.Replace(source, destination, null, true);
                    }
                    else File.Move(source, destination);
                }

                manifest = manifest with { State = "committed" };
                WriteManifest(fullBackup, manifest);
            }
            catch (Exception commitError)
            {
                try { Rollback(fullTarget, fullBackup, manifest); }
                catch (Exception rollbackError) { throw new DeploymentRollbackFailedException(fullBackup, commitError, rollbackError); }
                try { RemoveGeneratedDirectory(fullBackup); } catch { }

                if (IsSharingViolation(commitError))
                {
                    var paths = activePath is null ? [] : new[] { activePath };
                    var processes = RestartManager.FindProcesses(paths);
                    throw new DeploymentTargetBusyException(
                        new DeploymentTargetBusyDetails("commit", paths, processes),
                        $"提交期间目标文件被占用，已回滚: {activePath}", commitError);
                }

                throw;
            }
        }
        catch (DeploymentRollbackFailedException) { throw; }
        catch (IOException ex) when (manifest.State == "preparing" && IsSharingViolation(ex))
        {
            try { RemoveGeneratedDirectory(fullBackup); } catch { }
            var paths = activePath is null ? [] : new[] { activePath };
            var processes = RestartManager.FindProcesses(paths);
            throw new DeploymentTargetBusyException(
                new DeploymentTargetBusyDetails("backup", paths, processes),
                $"备份目标文件时检测到占用: {activePath}", ex);
        }
        catch
        {
            if (manifest.State == "preparing")
            {
                try { RemoveGeneratedDirectory(fullBackup); } catch { }
            }
            throw;
        }

        try
        {
            RemoveGeneratedDirectory(fullBackup);
            return new DeploymentFileCommitResult(absoluteAffected.Length + absoluteDirectories.Length, replacements.Length, deletions.Length, directoriesToReplace.Length, null);
        }
        catch (Exception ex)
        {
            return new DeploymentFileCommitResult(absoluteAffected.Length + absoluteDirectories.Length, replacements.Length, deletions.Length, directoriesToReplace.Length,
                $"部署已完成，但事务备份清理失败: {fullBackup}: {ex.Message}");
        }
    }

    public static void Recover(string backupDirectory, string target)
    {
        var fullBackup = Path.GetFullPath(backupDirectory);
        var manifest = ReadManifest(fullBackup);
        var fullTarget = Path.GetFullPath(target);
        if (!Path.GetFullPath(manifest.Target).Equals(fullTarget, PathComparison()))
            throw new InvalidDataException("事务记录中的目标目录与当前项目不匹配");

        if (manifest.State == "committing") Rollback(fullTarget, fullBackup, manifest);
        RemoveGeneratedDirectory(fullBackup);
    }

    private static void Rollback(string target, string backupDirectory, DeploymentTransactionManifest manifest)
    {
        var fullTarget = Path.GetFullPath(target);
        foreach (var relative in manifest.AddedFiles.Reverse())
        {
            var destination = FullPath(fullTarget, relative);
            PathSafety.EnsureNoReparsePoints(fullTarget, destination);
            if (File.Exists(destination))
            {
                ClearReadOnly(destination);
                File.Delete(destination);
            }
        }

        foreach (var relative in manifest.OriginalFiles.Reverse())
        {
            var backup = FullPath(Path.Combine(backupDirectory, "files"), relative);
            var destination = FullPath(fullTarget, relative);
            if (!File.Exists(backup)) throw new FileNotFoundException("事务备份文件缺失", backup);
            PathSafety.EnsureNoReparsePoints(fullTarget, destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = destination + $".rimdev-restore-{Guid.NewGuid():N}.tmp";
            try
            {
                File.Copy(backup, temporary, false);
                if (File.Exists(destination))
                {
                    ClearReadOnly(destination);
                    File.Replace(temporary, destination, null, true);
                }
                else File.Move(temporary, destination);
            }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
        }
    }

    private static void WriteManifest(string backupDirectory, DeploymentTransactionManifest manifest)
        => AtomicFile.WriteAllText(Path.Combine(backupDirectory, ManifestFileName), JsonSerializer.Serialize(manifest, JsonDefaults.Storage));

    public static void Remove(string generatedDirectory) => RemoveGeneratedDirectory(Path.GetFullPath(generatedDirectory));

    private static void RemoveGeneratedDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return;
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.TryPop(out var current))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException($"拒绝清理部署暂存目录中的 Reparse Point: {entry}");
                if (attributes.HasFlag(FileAttributes.Directory)) pending.Push(entry);
                else ClearReadOnly(entry);
            }
        }
        Directory.Delete(directory, true);
    }

    private static void ClearReadOnly(string path)
    {
        var attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }

    private static void ValidateManifestPaths(DeploymentTransactionManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Target)) throw new InvalidDataException("部署事务记录缺少目标目录");
        foreach (var relative in manifest.OriginalFiles.Concat(manifest.AddedFiles))
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || !PathSafety.IsWithin(FullPath(manifest.Target, relative), manifest.Target))
                throw new InvalidDataException($"部署事务记录包含无效相对路径: {relative}");
        }
    }

    private static IReadOnlyList<string> FindBusyResources(IEnumerable<string> files, IEnumerable<string> directories)
    {
        var busy = new List<string>();
        foreach (var path in files)
        {
            if (!CanAcquireDeleteAccess(path, isDirectory: false)) busy.Add(path);
        }
        foreach (var path in directories)
            if (!CanAcquireDeleteAccess(path, isDirectory: true)) busy.Add(path);
        return busy;
    }

    private static bool CanAcquireDeleteAccess(string path, bool isDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            using var handle = CreateFileW(path, 0x00010000, 0x00000007, IntPtr.Zero, 3,
                isDirectory ? 0x02000000u : 0u, IntPtr.Zero);
            if (!handle.IsInvalid) return true;
            var error = Marshal.GetLastWin32Error();
            if (error is ErrorSharingViolation or ErrorLockViolation) return false;
            throw new IOException($"无法检查部署目标占用状态: {path}", Marshal.GetHRForLastWin32Error());
        }

        if (isDirectory) return true;
        try { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); return true; }
        catch (IOException ex) when (IsSharingViolation(ex)) { return false; }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    private static IEnumerable<string> EnumerateFiles(string root, Regex? protectedRegex = null)
    {
        if (!Directory.Exists(root)) yield break;
        var fullRoot = Path.GetFullPath(root);
        var pending = new Stack<string>();
        pending.Push(fullRoot);
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (directory.Equals(fullRoot, PathComparison()) && IsProtected(Path.GetFileName(entry), protectedRegex)) continue;
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException($"拒绝遍历目标中的 Reparse Point: {entry}");
                if (attributes.HasFlag(FileAttributes.Directory)) pending.Push(entry);
                else yield return Path.GetRelativePath(fullRoot, entry);
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectories(string root, Regex? protectedRegex = null)
    {
        if (!Directory.Exists(root)) yield break;
        var fullRoot = Path.GetFullPath(root);
        var pending = new Stack<string>();
        pending.Push(fullRoot);
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in Directory.EnumerateDirectories(directory))
            {
                if (directory.Equals(fullRoot, PathComparison()) && IsProtected(Path.GetFileName(entry), protectedRegex)) continue;
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException($"拒绝遍历目标中的 Reparse Point: {entry}");
                yield return Path.GetRelativePath(fullRoot, entry);
                pending.Push(entry);
            }
        }
    }

    private static bool FilesEqual(string left, string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (leftInfo.Length != rightInfo.Length) return false;
        try
        {
            using var leftStream = new FileStream(left, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var rightStream = new FileStream(right, FileMode.Open, FileAccess.Read, FileShare.Read);
            return CryptographicOperations.FixedTimeEquals(SHA256.HashData(leftStream), SHA256.HashData(rightStream));
        }
        catch (IOException ex) when (IsSharingViolation(ex)) { return false; }
    }

    private static bool IsProtected(string relative, Regex? protectedRegex)
        => protectedRegex?.IsMatch(relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? relative) == true;

    private static string FullPath(string root, string relative) => Path.GetFullPath(relative, root);
    private static StringComparer PathComparer() => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static StringComparison PathComparison() => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static class RestartManager
    {
        private const int ErrorSuccess = 0;
        private const int ErrorMoreData = 234;

        public static IReadOnlyList<DeploymentFileProcess> FindProcesses(IReadOnlyList<string> paths)
        {
            if (!OperatingSystem.IsWindows() || paths.Count == 0) return [];
            uint session = 0;
            var key = new StringBuilder(33);
            try
            {
                if (RmStartSession(out session, 0, key) != ErrorSuccess) return [];
                if (RmRegisterResources(session, (uint)paths.Count, paths.ToArray(), 0, IntPtr.Zero, 0, IntPtr.Zero) != ErrorSuccess) return [];

                uint required = 0;
                uint count = 0;
                uint rebootReasons;
                var result = RmGetList(session, out required, ref count, null, out rebootReasons);
                if (result == ErrorSuccess || result != ErrorMoreData || required == 0) return [];

                if (required > int.MaxValue) return [];
                var items = new RmProcessInfo[checked((int)required)];
                count = required;
                result = RmGetList(session, out required, ref count, items, out rebootReasons);
                if (result != ErrorSuccess) return [];
                return items.Take((int)count)
                    .Select(item => new DeploymentFileProcess((int)item.Process.ProcessId, item.ApplicationName ?? item.ServiceShortName ?? "未知进程"))
                    .DistinctBy(item => item.ProcessId)
                    .ToArray();
            }
            catch { return []; }
            finally { if (session != 0) RmEndSession(session); }
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint sessionHandle, int sessionFlags, StringBuilder sessionKey);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(
            uint sessionHandle,
            uint fileCount,
            [In, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[]? fileNames,
            uint applicationCount,
            IntPtr applications,
            uint serviceCount,
            IntPtr serviceNames);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmGetList(uint sessionHandle, out uint processInfoNeeded, ref uint processInfo, [In, Out] RmProcessInfo[]? affectedApplications, out uint rebootReasons);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint sessionHandle);

        [StructLayout(LayoutKind.Sequential)]
        private struct RmUniqueProcess
        {
            public uint ProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RmProcessInfo
        {
            public RmUniqueProcess Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string? ApplicationName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string? ServiceShortName;
            public int ApplicationType;
            public uint ApplicationStatus;
            public uint TerminalServicesSessionId;
            [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
        }
    }
}
