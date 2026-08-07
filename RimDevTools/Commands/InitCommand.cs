using System.Text.Json;
using System.Text.Json.Serialization;

namespace RimDevTools;

public static class InitCommand
{
    private static readonly HashSet<string> AllowedVersions = new(StringComparer.Ordinal)
    {
        "1.6",
        "1.7",
        "1.8"
    };

    public static Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
        => RunAsync(args, DefaultTemplateRoot(), DefaultToolRoot(), cancellationToken);

    internal static string DefaultTemplateRoot()
        => Path.Combine(DefaultToolRoot(), "components", "ProjectTemplate");

    internal static string DefaultToolRoot()
        => Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    internal static Task<int> RunAsync(string[] args, string templateRoot, string toolRoot, CancellationToken cancellationToken = default)
    {
        const string command = "init";
        var version = args.Length > 1 ? args[1] : "";
        if (!AllowedVersions.Contains(version))
        {
            return Task.FromResult(CliResult.WriteFailure(
                command,
                ExitCodes.InvalidArguments,
                "VERSION_UNSUPPORTED",
                $"不支持 RimWorld 版本: {version}",
                details: new { supportedVersions = AllowedVersions.OrderBy(value => value, StringComparer.Ordinal).ToArray() }));
        }

        string output;
        try
        {
            output = Path.GetFullPath(CliArgs.RequiredValue(args, "--output"));
            EnsureTargetIsEmpty(output);
        }
        catch (InitFailureException ex)
        {
            return Task.FromResult(WriteFailure(command, ex));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Task.FromResult(CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "INVALID_ARGUMENT", ex.Message));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = LoadManifest(templateRoot);
            if (!manifest.SupportedVersions.Contains(version, StringComparer.Ordinal))
            {
                throw new InitFailureException(
                    ExitCodes.InvalidArguments,
                    "VERSION_UNSUPPORTED",
                    $"当前模板不支持 RimWorld {version}",
                    new { supportedVersions = manifest.SupportedVersions });
            }

            var versionTemplate = Path.Combine(templateRoot, version);
            if (!Directory.Exists(versionTemplate))
            {
                throw new InitFailureException(
                    ExitCodes.MissingDependency,
                    "INIT_TEMPLATE_MISSING",
                    $"缺少版本模板: {versionTemplate}");
            }
            if (!Directory.Exists(toolRoot))
                throw new InitFailureException(ExitCodes.MissingDependency, "INIT_TEMPLATE_MISSING", $"缺少共享工具目录: {toolRoot}");
            if (Directory.Exists(Path.Combine(versionTemplate, ".tools")) || File.Exists(Path.Combine(versionTemplate, ".tools")))
                throw InvalidTemplate($"版本模板不得重复包含 .tools: {versionTemplate}");

            ValidateTemplateTree(versionTemplate);
            var parent = Directory.GetParent(output)?.FullName
                ?? throw new InitFailureException(ExitCodes.InvalidArguments, "INVALID_ARGUMENT", $"输出目录没有可用父目录: {output}");
            Directory.CreateDirectory(parent);
            var staging = Path.Combine(parent, $".{Path.GetFileName(output)}.rimdevtools-init-{Guid.NewGuid():N}");

            try
            {
                CopyTemplate(versionTemplate, staging, cancellationToken);
                CopySharedTooling(toolRoot, Path.Combine(staging, ".tools"), cancellationToken);
                CreateRequiredDirectories(staging, manifest.RequiredDirectories, version);
                ValidateRequiredFiles(staging, manifest.RequiredFiles, version);
                ValidateTemplateTree(staging);
                var createdEntries = EnumerateEntries(staging);

                EnsureTargetIsEmpty(output);
                if (Directory.Exists(output)) Directory.Delete(output, recursive: false);
                Directory.Move(staging, output);

                return Task.FromResult(CliResult.WriteSuccess(command, null, new
                {
                    version,
                    output,
                    createdEntries
                }));
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }
        }
        catch (InitFailureException ex)
        {
            return Task.FromResult(WriteFailure(command, ex));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(CliResult.WriteFailure(command, ExitCodes.TaskFailed, "INIT_FAILED", ex.Message, details: new { output }));
        }
    }

    private static ProjectTemplateManifest LoadManifest(string templateRoot)
    {
        var manifestPath = Path.Combine(templateRoot, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new InitFailureException(ExitCodes.MissingDependency, "INIT_TEMPLATE_MISSING", $"缺少项目模板清单: {manifestPath}");
        }

        if (File.GetAttributes(manifestPath).HasFlag(FileAttributes.ReparsePoint))
        {
            throw InvalidTemplate($"模板清单不得是 Reparse Point: {manifestPath}");
        }

        ProjectTemplateManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ProjectTemplateManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            throw InvalidTemplate($"模板清单不是有效 JSON: {ex.Message}");
        }

        if (manifest is null || manifest.SchemaVersion != 1)
            throw InvalidTemplate($"模板清单 schemaVersion 必须为 1，当前为 {manifest?.SchemaVersion.ToString() ?? "null"}");
        if (manifest.SupportedVersions is null || manifest.RequiredFiles is null || manifest.RequiredDirectories is null)
            throw InvalidTemplate("模板清单缺少 supportedVersions、requiredFiles 或 requiredDirectories");
        if (manifest.SupportedVersions.Count == 0 || manifest.SupportedVersions.Any(version => !AllowedVersions.Contains(version)))
            throw InvalidTemplate("supportedVersions 只能包含 1.6、1.7、1.8");
        if (manifest.SupportedVersions.Count != manifest.SupportedVersions.Distinct(StringComparer.Ordinal).Count())
            throw InvalidTemplate("supportedVersions 不得包含重复版本");

        foreach (var path in manifest.RequiredFiles.Concat(manifest.RequiredDirectories))
            _ = ResolveManifestPath("manifest-validation", path, "1.6");
        return manifest;
    }

    private static void EnsureTargetIsEmpty(string output)
    {
        if (File.Exists(output))
            throw new InitFailureException(ExitCodes.InvalidArguments, "INIT_TARGET_NOT_EMPTY", $"输出路径已被文件占用: {output}");
        if (!Directory.Exists(output)) return;
        if (File.GetAttributes(output).HasFlag(FileAttributes.ReparsePoint) || Directory.EnumerateFileSystemEntries(output).Any())
            throw new InitFailureException(ExitCodes.InvalidArguments, "INIT_TARGET_NOT_EMPTY", $"输出目录必须为空: {output}");
    }

    private static void ValidateTemplateTree(string root)
    {
        if (File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
            throw InvalidTemplate($"模板目录不得是 Reparse Point: {root}");
        ValidateDirectory(root);

        static void ValidateDirectory(string directory)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw InvalidTemplate($"模板内不得包含 Reparse Point: {entry}");
                if (attributes.HasFlag(FileAttributes.Directory)) ValidateDirectory(entry);
            }
        }
    }

    private static void CopyTemplate(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        CopyDirectory(source, destination);

        void CopyDirectory(string currentSource, string currentDestination)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(currentSource))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw InvalidTemplate($"模板内不得包含 Reparse Point: {entry}");

                var target = Path.Combine(currentDestination, Path.GetFileName(entry));
                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    Directory.CreateDirectory(target);
                    CopyDirectory(entry, target);
                }
                else
                {
                    File.Copy(entry, target, overwrite: false);
                }
            }
        }
    }

    private static void CopySharedTooling(string source, string destination, CancellationToken cancellationToken)
    {
        if (File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint))
            throw InvalidTemplate($"共享工具目录不得是 Reparse Point: {source}");
        Directory.CreateDirectory(destination);
        CopyDirectory(source, destination, isRoot: true);

        void CopyDirectory(string currentSource, string currentDestination, bool isRoot = false)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(currentSource))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (isRoot && Path.GetFileName(entry).Equals("components", StringComparison.OrdinalIgnoreCase)) continue;
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw InvalidTemplate($"共享工具内不得包含 Reparse Point: {entry}");

                var target = Path.Combine(currentDestination, Path.GetFileName(entry));
                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    Directory.CreateDirectory(target);
                    CopyDirectory(entry, target);
                }
                else
                {
                    File.Copy(entry, target, overwrite: false);
                }
            }
        }
    }

    private static void CreateRequiredDirectories(string staging, IReadOnlyList<string> requiredDirectories, string version)
    {
        foreach (var relative in requiredDirectories)
        {
            var path = ResolveManifestPath(staging, relative, version);
            if (File.Exists(path)) throw InvalidTemplate($"必需目录路径被文件占用: {relative}");
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }
    }

    private static void ValidateRequiredFiles(string staging, IReadOnlyList<string> requiredFiles, string version)
    {
        foreach (var relative in requiredFiles)
        {
            var path = ResolveManifestPath(staging, relative, version);
            if (!File.Exists(path)) throw InvalidTemplate($"模板缺少必需文件: {relative}");
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                throw InvalidTemplate($"必需文件不得是 Reparse Point: {relative}");
        }
    }

    internal static string ResolveManifestPath(string root, string? relative, string version)
    {
        if (string.IsNullOrWhiteSpace(relative)) throw InvalidTemplate("manifest 路径不得为空");
        var expanded = relative.Replace("{version}", version, StringComparison.Ordinal);
        if (expanded.Contains('{') || expanded.Contains('}') || Path.IsPathRooted(expanded))
            throw InvalidTemplate($"manifest 路径无效: {relative}");

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Path.Combine(root, expanded));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw InvalidTemplate($"manifest 路径无效: {relative}");
        }
        if (!PathSafety.IsWithin(fullPath, root)) throw InvalidTemplate($"manifest 路径越出模板根目录: {relative}");
        return fullPath;
    }

    private static string[] EnumerateEntries(string root)
        => Directory.EnumerateFileSystemEntries(root, "*", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static InitFailureException InvalidTemplate(string message)
        => new(ExitCodes.TaskFailed, "INIT_TEMPLATE_INVALID", message);

    private static int WriteFailure(string command, InitFailureException exception)
        => CliResult.WriteFailure(command, exception.ExitCode, exception.Code, exception.Message, details: exception.Details);

    private sealed class InitFailureException(int exitCode, string code, string message, object? details = null) : Exception(message)
    {
        public int ExitCode { get; } = exitCode;
        public string Code { get; } = code;
        public object? Details { get; } = details;
    }

    private sealed class ProjectTemplateManifest
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; init; }

        [JsonPropertyName("supportedVersions")]
        public List<string> SupportedVersions { get; init; } = [];

        [JsonPropertyName("requiredFiles")]
        public List<string> RequiredFiles { get; init; } = [];

        [JsonPropertyName("requiredDirectories")]
        public List<string> RequiredDirectories { get; init; } = [];
    }
}
