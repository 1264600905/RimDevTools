using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RimDevTools;

public sealed record TransactionRecoveryResult(bool Ok, bool Found, CliError? Error = null);

public sealed class ConfigTransaction : IAsyncDisposable
{
    private const int SchemaVersion = 1;
    private readonly string _directory;
    private readonly string _manifestPath;
    private readonly TransactionManifest _manifest;
    private bool _restored;

    private ConfigTransaction(string directory, TransactionManifest manifest)
    {
        _directory = directory;
        _manifestPath = Path.Combine(directory, "transaction.json");
        _manifest = manifest;
    }

    public static Task<ConfigTransaction> CreateAsync(string configDirectory, string runId, IEnumerable<string> paths)
    {
        var root = Path.Combine(configDirectory, "RimDevToolsTransactions");
        var directory = Path.Combine(root, runId);
        var creatingDirectory = Path.Combine(root, $".creating-{runId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(creatingDirectory);
        var entries = new List<TransactionEntry>();
        var index = 0;
        foreach (var rawPath in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.GetFullPath(rawPath);
            var exists = File.Exists(path);
            var original = exists ? File.ReadAllBytes(path) : [];
            var backupName = $"{index++}-{Path.GetFileName(path)}.original";
            var backup = Path.Combine(directory, backupName);
            if (exists) AtomicFile.WriteAllBytes(Path.Combine(creatingDirectory, backupName), original);
            entries.Add(new TransactionEntry
            {
                Path = path,
                Backup = backup,
                Existed = exists,
                OriginalHash = exists ? Hash(original) : ""
            });
        }

        var manifest = new TransactionManifest
        {
            SchemaVersion = SchemaVersion,
            RunId = runId,
            CreatedUtc = DateTimeOffset.UtcNow,
            Entries = entries
        };
        AtomicFile.WriteAllText(Path.Combine(creatingDirectory, "transaction.json"), JsonSerializer.Serialize(manifest, TransactionJson.Options));
        Directory.Move(creatingDirectory, directory);
        var transaction = new ConfigTransaction(directory, manifest);
        return Task.FromResult(transaction);
    }

    public Task WriteAllTextAsync(string path, string content)
    {
        var bytes = new UTF8Encoding(false).GetBytes(content);
        var entry = FindEntry(path);
        entry.AppliedHash = Hash(bytes);
        SaveManifest();
        AtomicFile.WriteAllBytes(entry.Path, bytes);
        return Task.CompletedTask;
    }

    public async Task RestoreAsync(bool force = false)
    {
        if (_restored) return;
        var result = await TransactionRecovery.RestoreDirectoryAsync(_directory, force);
        if (!result.Ok) throw new IOException(result.Error?.Message ?? "事务恢复失败");
        _restored = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_restored) await RestoreAsync();
    }

    private TransactionEntry FindEntry(string path)
    {
        var full = Path.GetFullPath(path);
        return _manifest.Entries.SingleOrDefault(x => x.Path.Equals(full, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"路径不属于当前事务: {full}");
    }

    private void SaveManifest()
        => AtomicFile.WriteAllText(_manifestPath, JsonSerializer.Serialize(_manifest, TransactionJson.Options));

    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

public static class TransactionRecovery
{
    public static async Task<TransactionRecoveryResult> RecoverAsync(string configDirectory)
    {
        var root = Path.Combine(configDirectory, "RimDevToolsTransactions");
        if (!Directory.Exists(root)) return new(true, false);

        var found = false;
        foreach (var directory in Directory.EnumerateDirectories(root).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (Path.GetFileName(directory).StartsWith(".creating-", StringComparison.OrdinalIgnoreCase))
            {
                try { Directory.Delete(directory, true); }
                catch (Exception ex) { return Failure("TRANSACTION_CREATE_CLEANUP_FAILED", $"无法清理未完成的事务创建目录: {ex.Message}"); }
                continue;
            }
            found = true;
            var result = await RestoreDirectoryAsync(directory, false);
            if (!result.Ok) return result with { Found = true };
        }
        return new(true, found);
    }

    internal static Task<TransactionRecoveryResult> RestoreDirectoryAsync(string directory, bool force)
    {
        var manifestPath = Path.Combine(directory, "transaction.json");
        if (!File.Exists(manifestPath))
            return Task.FromResult(Failure("TRANSACTION_MANIFEST_MISSING", $"事务清单不存在: {manifestPath}"));

        TransactionManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<TransactionManifest>(File.ReadAllText(manifestPath), TransactionJson.Options)
                ?? throw new InvalidDataException("事务清单为空");
            if (manifest.SchemaVersion != 1) throw new InvalidDataException($"不支持事务版本: {manifest.SchemaVersion}");
            var configDirectory = Directory.GetParent(Directory.GetParent(directory)?.FullName ?? "")?.FullName
                ?? throw new InvalidDataException("事务目录结构无效");
            foreach (var entry in manifest.Entries)
            {
                if (!PathSafety.IsWithin(entry.Path, configDirectory)) throw new InvalidDataException($"事务目标越出 RimWorld 配置目录: {entry.Path}");
                if (!PathSafety.IsWithin(entry.Backup, directory)) throw new InvalidDataException($"事务备份越出事务目录: {entry.Backup}");
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(Failure("TRANSACTION_MANIFEST_INVALID", $"事务清单无效: {ex.Message}"));
        }

        foreach (var entry in manifest.Entries)
        {
            var currentHash = File.Exists(entry.Path) ? ConfigTransaction.Hash(File.ReadAllBytes(entry.Path)) : "";
            var isOriginal = currentHash.Equals(entry.OriginalHash, StringComparison.OrdinalIgnoreCase);
            var isApplied = entry.AppliedHash is not null && currentHash.Equals(entry.AppliedHash, StringComparison.OrdinalIgnoreCase);
            if (!force && !isOriginal && !isApplied)
            {
                return Task.FromResult(Failure(
                    "TRANSACTION_CONFLICT",
                    $"检测到用户修改，拒绝自动恢复: {entry.Path}",
                    new { manifest.RunId, entry.Path, currentHash, entry.OriginalHash, entry.AppliedHash }));
            }
        }

        try
        {
            foreach (var entry in manifest.Entries)
            {
                if (entry.Existed)
                {
                    var currentHash = File.Exists(entry.Path) ? ConfigTransaction.Hash(File.ReadAllBytes(entry.Path)) : "";
                    if (!force && currentHash.Equals(entry.OriginalHash, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!File.Exists(entry.Backup)) throw new FileNotFoundException("事务备份不存在", entry.Backup);
                    AtomicFile.WriteAllBytes(entry.Path, File.ReadAllBytes(entry.Backup));
                }
                else if (File.Exists(entry.Path))
                {
                    File.Delete(entry.Path);
                }
            }
            Directory.Delete(directory, true);
            return Task.FromResult(new TransactionRecoveryResult(true, true));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Failure("TRANSACTION_RESTORE_FAILED", $"事务恢复失败: {ex.Message}", new { manifest.RunId }));
        }
    }

    public static IReadOnlyList<string> PendingDirectories(string configDirectory)
    {
        var root = Path.Combine(configDirectory, "RimDevToolsTransactions");
        return Directory.Exists(root) ? Directory.EnumerateDirectories(root).ToArray() : [];
    }

    private static TransactionRecoveryResult Failure(string code, string message, object? details = null)
        => new(false, true, new CliError(code, message, details));
}

internal sealed class TransactionManifest
{
    public int SchemaVersion { get; set; }
    public string RunId { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public List<TransactionEntry> Entries { get; set; } = [];
}

internal sealed class TransactionEntry
{
    public string Path { get; set; } = "";
    public string Backup { get; set; } = "";
    public bool Existed { get; set; }
    public string OriginalHash { get; set; } = "";
    public string? AppliedHash { get; set; }
}

internal static class TransactionJson
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}
