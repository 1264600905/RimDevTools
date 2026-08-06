using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace RimDevTools;

public sealed record ExportMetadata(
    int SchemaVersion,
    string RunId,
    string ExporterVersion,
    int SqliteUserVersion,
    string GameVersion,
    string Language,
    string ExportedUtc,
    string InputModsHash,
    string RequestedModsHash,
    string EffectiveModsHash,
    string ProjectPackageId);

public sealed record LoadedModInfo(int LoadOrder, string PackageId, string Name, string Version, string RootPath, bool IsOfficial, bool IsTooling);

public sealed record DatabaseStatus(
    string DatabasePath,
    string PathSource,
    bool Exists,
    long? Size,
    DateTimeOffset? LastWriteUtc,
    bool CanOpen,
    string? Integrity,
    int? UserVersion,
    string? RimSearcherVersion,
    int? ExpectedUserVersion,
    bool VersionMatches,
    ExportMetadata? Metadata,
    IReadOnlyList<LoadedModInfo> LoadedMods,
    bool RefreshRecommended,
    IReadOnlyList<string> ReasonCodes);

public static class ComponentLocator
{
    public static string RimSearcherPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("RIMDEVTOOLS_RIMSEARCHER_PATH");
        return string.IsNullOrWhiteSpace(overridePath)
            ? Path.Combine(AppContext.BaseDirectory, "rimsearcher.exe")
            : Path.GetFullPath(overridePath);
    }

    public static string? FileVersion(string path)
        => File.Exists(path) ? FileVersionInfo.GetVersionInfo(path).FileVersion : null;

    public static int? EncodedVersion(string? value)
    {
        if (!Version.TryParse(value, out var version) || version.Build is < 0 or > 99) return null;
        return version.Major * 10000 + version.Minor * 100 + version.Build;
    }
}

public static class DatabaseInspector
{
    public static DatabaseStatus Inspect(
        string databasePath,
        string pathSource,
        string? expectedGameVersion = null,
        string? expectedProjectPackageId = null,
        string? expectedRequestedModsHash = null)
    {
        var fullPath = Path.GetFullPath(databasePath);
        var reasons = new List<string>();
        var rimSearcherPath = ComponentLocator.RimSearcherPath();
        var rimSearcherVersion = ComponentLocator.FileVersion(rimSearcherPath);
        var expectedVersion = ComponentLocator.EncodedVersion(rimSearcherVersion);
        if (!File.Exists(rimSearcherPath)) reasons.Add("RIMSEARCHER_CLI_MISSING");
        else if (expectedVersion is null) reasons.Add("RIMSEARCHER_VERSION_INVALID");

        if (!File.Exists(fullPath))
        {
            reasons.Add("DATABASE_MISSING");
            return new DatabaseStatus(fullPath, pathSource, false, null, null, false, null, null, rimSearcherVersion, expectedVersion, false, null, [], true, reasons);
        }

        var info = new FileInfo(fullPath);
        if (info.Length == 0) reasons.Add("DATABASE_EMPTY");
        var canOpen = false;
        string? integrity = null;
        int? userVersion = null;
        ExportMetadata? metadata = null;
        var loadedMods = new List<LoadedModInfo>();
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = fullPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            connection.Open();
            canOpen = true;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA integrity_check";
                integrity = Convert.ToString(command.ExecuteScalar());
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA user_version";
                userVersion = Convert.ToInt32(command.ExecuteScalar());
            }

            if (TableExists(connection, "export_metadata")) metadata = ReadMetadata(connection);
            else reasons.Add("EXPORT_METADATA_MISSING");
            if (TableExists(connection, "loaded_mods")) loadedMods.AddRange(ReadLoadedMods(connection));
            else reasons.Add("LOADED_MODS_MISSING");
        }
        catch (Exception ex)
        {
            reasons.Add("DATABASE_OPEN_FAILED");
            Console.Error.WriteLine($"数据库检查失败: {ex.Message}");
        }

        if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase)) reasons.Add("DATABASE_INTEGRITY_FAILED");
        var versionMatches = userVersion is not null && expectedVersion is not null && userVersion == expectedVersion;
        if (!versionMatches) reasons.Add("VERSION_MISMATCH");
        if (metadata is not null && metadata.SqliteUserVersion != userVersion) reasons.Add("METADATA_VERSION_MISMATCH");
        if (metadata is not null && metadata.SchemaVersion != 1) reasons.Add("METADATA_SCHEMA_UNSUPPORTED");
        if (metadata is not null && !string.IsNullOrWhiteSpace(expectedGameVersion) && !metadata.GameVersion.StartsWith(expectedGameVersion, StringComparison.OrdinalIgnoreCase)) reasons.Add("GAME_VERSION_MISMATCH");
        if (metadata is not null && !string.IsNullOrWhiteSpace(expectedProjectPackageId) && !metadata.ProjectPackageId.Equals(expectedProjectPackageId, StringComparison.OrdinalIgnoreCase)) reasons.Add("PROJECT_PACKAGE_ID_MISMATCH");
        if (metadata is not null && !string.IsNullOrWhiteSpace(expectedRequestedModsHash) && metadata.RequestedModsHash != expectedRequestedModsHash) reasons.Add("REQUESTED_MODS_HASH_MISMATCH");
        if (metadata is not null && loadedMods.Count == 0) reasons.Add("LOADED_MODS_EMPTY");
        if (metadata is not null && loadedMods.Count > 0 && metadata.EffectiveModsHash != HashPackages(loadedMods.Select(x => x.PackageId))) reasons.Add("EFFECTIVE_MODS_HASH_MISMATCH");
        return new DatabaseStatus(
            fullPath,
            pathSource,
            true,
            info.Length,
            info.LastWriteTimeUtc,
            canOpen,
            integrity,
            userVersion,
            rimSearcherVersion,
            expectedVersion,
            versionMatches,
            metadata,
            loadedMods,
            reasons.Count > 0,
            reasons.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static bool TableExists(SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name";
        command.Parameters.AddWithValue("$name", name);
        return command.ExecuteScalar() is not null;
    }

    private static ExportMetadata ReadMetadata(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT schema_version, run_id, exporter_version, sqlite_user_version, game_version, language, exported_utc, input_mods_hash, requested_mods_hash, effective_mods_hash, project_package_id FROM export_metadata LIMIT 1";
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidDataException("export_metadata 表为空");
        return new ExportMetadata(
            reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
            reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetString(10));
    }

    private static IEnumerable<LoadedModInfo> ReadLoadedMods(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT load_order, package_id, name, version, root_path, is_official, is_tooling FROM loaded_mods ORDER BY load_order";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            yield return new LoadedModInfo(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt32(5) != 0, reader.GetInt32(6) != 0);
    }

    private static string HashPackages(IEnumerable<string> packageIds)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join("\n", packageIds));
        return "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
