using System.Xml.Linq;

namespace RimDevTools;

public sealed record ModCandidate(
    string Name,
    string PackageId,
    string FolderName,
    string SourceDirectory,
    bool IsOfficial,
    string? Author,
    List<string> Dependencies);

public static class ModCommand
{
    public static Task<int> SearchAsync(string[] args)
    {
        const string command = "mod search";
        var context = CliApplication.RequireProject(command, out var code);
        if (context is null) return Task.FromResult(code);

        var keyword = QueryArgument(args, "search");
        var fuzzy = CliArgs.Has(args, "--fuzzy");
        var candidates = FindCandidates(context, keyword, fuzzy, context.Config.GameVersion);
        return Task.FromResult(CliResult.WriteSuccess(command, context.Root, new { keyword, fuzzy, candidates }));
    }

    public static Task<int> DependenciesAsync(string[] args)
    {
        const string command = "mod dependencies";
        var context = CliApplication.RequireProject(command, out var code);
        if (context is null) return Task.FromResult(code);

        var query = QueryArgument(args, "dependencies");
        var version = CliArgs.Value(args, "--version") ?? context.Config.GameVersion;
        var resolved = ResolveUnique(context, query, CliArgs.Has(args, "--fuzzy"), version, out var error);
        if (resolved is null)
            return Task.FromResult(CliResult.WriteFailure(command, ExitCodes.InvalidArguments, error!.Code, error.Message, context.Root, error.Details));

        return Task.FromResult(CliResult.WriteSuccess(command, context.Root, new { target = resolved, version, dependencies = resolved.Dependencies }));
    }

    public static Task<int> DependentsAsync(string[] args)
    {
        const string command = "mod dependents";
        var context = CliApplication.RequireProject(command, out var code);
        if (context is null) return Task.FromResult(code);

        var query = QueryArgument(args, "dependents");
        var version = CliArgs.Value(args, "--version") ?? context.Config.GameVersion;
        var resolved = ResolveUnique(context, query, CliArgs.Has(args, "--fuzzy"), version, out var error);
        if (resolved is null)
            return Task.FromResult(CliResult.WriteFailure(command, ExitCodes.InvalidArguments, error!.Code, error.Message, context.Root, error.Details));

        var related = FindCandidates(context, "", false, version)
            .Where(x => x.Dependencies.Contains(resolved.PackageId, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        return Task.FromResult(CliResult.WriteSuccess(command, context.Root, new { target = resolved, version, dependents = related }));
    }

    public static async Task<int> CopyAsync(string[] args, CancellationToken cancellationToken = default)
    {
        const string command = "mod copy";
        var context = CliApplication.RequireProject(command, out var code);
        if (context is null) return code;

        var query = QueryArgument(args, "copy");
        var version = CliArgs.Value(args, "--version") ?? context.Config.GameVersion;
        if (string.IsNullOrWhiteSpace(version))
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "VERSION_MISSING", "mod copy 需要 --version 或 game_version", context.Root);

        var selected = ResolveUnique(context, query, CliArgs.Has(args, "--fuzzy"), version, out var error);
        if (selected is null)
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, error!.Code, error.Message, context.Root, error.Details);

        var closure = ResolveDependencyClosure(context, selected, CliArgs.Has(args, "--deps"), version, out var skippedDependencies, out error);
        if (closure is null)
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, error!.Code, error.Message, context.Root, error.Details);

        string targetRoot;
        try
        {
            targetRoot = context.Config.ResolveReferenceDocsPath(context.Root);
        }
        catch (Exception ex)
        {
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "PROJECT_PATH_INVALID", ex.Message, context.Root);
        }
        try
        {
            PathSafety.EnsureNoReparsePoints(context.Root, targetRoot);
        }
        catch (Exception ex)
        {
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "REFERENCE_TARGET_UNSAFE", ex.Message, context.Root, new { targetRoot });
        }
        var runId = Guid.NewGuid().ToString("N");
        var stagingRoot = Path.Combine(targetRoot, ".rimdev-staging", runId);
        var backupRoot = Path.Combine(targetRoot, ".rimdev-backup", runId);

        CopyTarget[] targets;
        try
        {
            targets = closure.Select(mod => CreateTarget(targetRoot, stagingRoot, backupRoot, mod)).ToArray();
            var collisions = targets.GroupBy(x => x.Target, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1).ToArray();
            if (collisions.Length > 0)
                return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "TARGET_COLLISION", "多个 packageId 映射到同一目标目录", context.Root, collisions.Select(x => new { target = x.Key, packageIds = x.Select(y => y.Mod.PackageId).ToArray() }).ToArray());
        }
        catch (Exception ex)
        {
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "MOD_NAME_INVALID", ex.Message, context.Root);
        }

        var includeIgnored = CliArgs.Has(args, "--include-ignored");
        var ignoredFolders = includeIgnored ? [] : context.Config.PullIgnoreFolders.ToArray();
        var conflicts = targets.Where(x => Directory.Exists(x.Target) || File.Exists(x.Target)).Select(x => x.Target).ToArray();
        var plan = targets.Select(entry => new
        {
            name = entry.Mod.Name,
            packageId = entry.Mod.PackageId,
            sourceDirectory = entry.Mod.SourceDirectory,
            targetDirectory = entry.Target,
            conflict = Directory.Exists(entry.Target) || File.Exists(entry.Target),
            assemblies = PlannedAssemblies(entry.Mod.SourceDirectory, version)
        }).ToArray();
        if (CliArgs.Has(args, "--dry-run"))
            return CliResult.WriteSuccess(command, context.Root, new
            {
                dryRun = true,
                version,
                selected,
                dependencyClosure = closure.Select(mod => new { mod.Name, mod.PackageId, mod.SourceDirectory }).ToArray(),
                skippedDependencies,
                ignoredFolders,
                conflicts,
                targets = plan
            });

        try
        {
            Directory.CreateDirectory(targetRoot);
            PathSafety.EnsureNoReparsePoints(context.Root, targetRoot);
        }
        catch (Exception ex)
        {
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "REFERENCE_TARGET_UNSAFE", ex.Message, context.Root, new { targetRoot });
        }

        using var targetLock = TargetDirectoryLock.Create(targetRoot, out var lockError);
        if (targetLock is null)
            return CliResult.WriteFailure(command, ExitCodes.Busy, "TARGET_BUSY", lockError!, context.Root);

        if (conflicts.Length > 0 && !CliArgs.Has(args, "--overwrite-existing"))
            return CliResult.WriteFailure(command, ExitCodes.TaskFailed, "TARGET_CONFLICT", "目标目录已存在，请使用 --overwrite-existing", context.Root, new { conflicts });

        var results = new List<object>();
        try
        {
            foreach (var entry in targets)
            {
                Directory.CreateDirectory(entry.Staging);
                CopyReference(entry.Mod.SourceDirectory, entry.Staging, ignoredFolders, cancellationToken);
                var decompiled = await DecompileAssembliesAsync(context, entry.Staging, version, cancellationToken);
                results.Add(new
                {
                    name = entry.Mod.Name,
                    packageId = entry.Mod.PackageId,
                    sourceDirectory = entry.Mod.SourceDirectory,
                    targetDirectory = entry.Target,
                    decompiledAssemblies = decompiled,
                    ok = true
                });
            }

            InstallBatch(targets);
            return CliResult.WriteSuccess(command, context.Root, new { runId, version, skippedDependencies, results });
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return CliResult.WriteFailure(command, ExitCodes.TaskFailed, "COPY_FAILED", ex.Message, context.Root, new { runId, version, results });
        }
        finally
        {
            DeleteDirectoryBestEffort(stagingRoot);
            DeleteDirectoryIfEmpty(backupRoot);
        }
    }

    internal static string SafeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("模组 name 为空");

        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(name.Trim().Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) || char.IsControl(c) ? '_' : c).ToArray()).TrimEnd(' ', '.');
        if (sanitized is "" or "." or "..") throw new InvalidDataException($"模组 name 不能映射为安全目录名: {name}");

        var stem = sanitized.Split('.')[0];
        var reserved = stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || Enumerable.Range(1, 9).Any(i => stem.Equals($"COM{i}", StringComparison.OrdinalIgnoreCase) || stem.Equals($"LPT{i}", StringComparison.OrdinalIgnoreCase));
        return reserved ? $"_{sanitized}" : sanitized;
    }

    private static CopyTarget CreateTarget(string targetRoot, string stagingRoot, string backupRoot, ModCandidate mod)
    {
        var folder = SafeFolderName(mod.Name);
        var target = Path.GetFullPath(Path.Combine(targetRoot, folder));
        if (!PathSafety.IsWithin(target, targetRoot)) throw new InvalidDataException($"参考模组目标越界: {target}");
        var staging = Path.Combine(stagingRoot, folder);
        var backup = Path.Combine(backupRoot, folder);
        PathSafety.EnsureNoReparsePoints(targetRoot, target);
        PathSafety.EnsureNoReparsePoints(targetRoot, staging);
        PathSafety.EnsureNoReparsePoints(targetRoot, backup);
        return new CopyTarget(mod, target, staging, backup);
    }

    private static string[] PlannedAssemblies(string sourceDirectory, string version)
    {
        var versioned = Path.Combine(sourceDirectory, version, "Assemblies");
        var fallback = Path.Combine(sourceDirectory, "Assemblies");
        var assemblyRoot = Directory.Exists(versioned) ? versioned : Directory.Exists(fallback) ? fallback : null;
        return assemblyRoot is null
            ? []
            : Directory.EnumerateFiles(assemblyRoot, "*.dll", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    private static List<ModCandidate>? ResolveDependencyClosure(
        ProjectContext context,
        ModCandidate selected,
        bool includeDependencies,
        string version,
        out IReadOnlyList<SkippedDependency> skippedDependencies,
        out CliError? error)
    {
        error = null;
        var skipped = new List<SkippedDependency>();
        skippedDependencies = skipped;
        if (!includeDependencies) return [selected];

        var ordered = new List<ModCandidate>();
        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new List<string>();
        CliError? resolutionError = null;

        bool Visit(ModCandidate current)
        {
            var cycleAt = visiting.FindIndex(x => x.Equals(current.PackageId, StringComparison.OrdinalIgnoreCase));
            if (cycleAt >= 0)
            {
                var cycle = visiting.Skip(cycleAt).Append(current.PackageId).ToArray();
                resolutionError = new CliError("DEPENDENCY_CYCLE", "检测到参考模组依赖循环", new { cycle });
                return false;
            }
            if (completed.Contains(current.PackageId)) return true;

            visiting.Add(current.PackageId);
            foreach (var dependency in current.Dependencies)
            {
                var exclusionReason = DependencyExclusionReason(dependency);
                if (exclusionReason is not null)
                {
                    var existing = skipped.FirstOrDefault(item => item.PackageId.Equals(dependency, StringComparison.OrdinalIgnoreCase));
                    if (existing is null)
                        skipped.Add(new SkippedDependency(dependency, exclusionReason, [current.PackageId]));
                    else if (!existing.RequiredBy.Contains(current.PackageId, StringComparer.OrdinalIgnoreCase))
                        existing.RequiredBy.Add(current.PackageId);
                    continue;
                }
                var resolved = ResolveUnique(context, dependency, false, version, out var dependencyError);
                if (resolved is null)
                {
                    resolutionError = new CliError(dependencyError!.Code, $"依赖 {dependency}: {dependencyError.Message}", dependencyError.Details);
                    return false;
                }
                if (!Visit(resolved)) return false;
            }
            visiting.RemoveAt(visiting.Count - 1);
            completed.Add(current.PackageId);
            ordered.Add(current);
            return true;
        }

        var succeeded = Visit(selected);
        error = resolutionError;
        return succeeded ? ordered : null;
    }

    private static string? DependencyExclusionReason(string packageId)
    {
        if (packageId.Equals("ludeon.rimworld", StringComparison.OrdinalIgnoreCase)
            || packageId.StartsWith("ludeon.rimworld.", StringComparison.OrdinalIgnoreCase))
            return "official_content";
        if (packageId.Equals("brrainz.harmony", StringComparison.OrdinalIgnoreCase))
            return "shared_harmony";
        return null;
    }

    private static async Task<int> DecompileAssembliesAsync(ProjectContext context, string staging, string version, CancellationToken cancellationToken)
    {
        var versioned = Path.Combine(staging, version, "Assemblies");
        var fallback = Path.Combine(staging, "Assemblies");
        var assemblyRoot = Directory.Exists(versioned) ? versioned : Directory.Exists(fallback) ? fallback : null;
        if (assemblyRoot is null) return 0;

        var assemblies = Directory.EnumerateFiles(assemblyRoot, "*.dll", SearchOption.TopDirectoryOnly).ToArray();
        if (assemblies.Length == 0) return 0;

        var ilspy = DoctorCommand.ResolveIlspyPath(context);
        if (ilspy is null || !File.Exists(ilspy)) throw new FileNotFoundException("找不到 ilspycmd，请运行 doctor", ilspy);

        var decompiled = 0;
        foreach (var assembly in assemblies)
        {
            var output = Path.Combine(Path.GetDirectoryName(assembly)!, "Decompiled", Path.GetFileNameWithoutExtension(assembly));
            Directory.CreateDirectory(output);
            if (!await ProcessUtil.RunAsync(ilspy, ["-p", "-o", output, assembly], context.Root, cancellationToken))
                throw new InvalidOperationException($"反编译失败: {assembly}");
            decompiled++;
        }
        return decompiled;
    }

    internal static void InstallBatch(IReadOnlyList<CopyTarget> targets)
    {
        var installed = new List<CopyTarget>();
        try
        {
            foreach (var entry in targets)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(entry.Backup)!);
                if (Directory.Exists(entry.Target)) Directory.Move(entry.Target, entry.Backup);
                else if (File.Exists(entry.Target)) File.Move(entry.Target, entry.Backup);

                Directory.Move(entry.Staging, entry.Target);
                installed.Add(entry);
            }
        }
        catch (Exception installError)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var entry in installed.AsEnumerable().Reverse())
            {
                try { RestoreTarget(entry); }
                catch (Exception ex) { rollbackErrors.Add(ex); }
            }
            var current = targets.FirstOrDefault(x => !installed.Contains(x) && (Directory.Exists(x.Backup) || File.Exists(x.Backup)));
            if (current is not null)
            {
                try { RestoreTarget(current); }
                catch (Exception ex) { rollbackErrors.Add(ex); }
            }
            if (rollbackErrors.Count > 0) throw new AggregateException("参考模组替换失败且回滚不完整；备份已保留", [installError, .. rollbackErrors]);
            throw;
        }

        foreach (var entry in installed)
        {
            try
            {
                if (Directory.Exists(entry.Backup)) Directory.Delete(entry.Backup, true);
                else if (File.Exists(entry.Backup)) File.Delete(entry.Backup);
            }
            catch (Exception ex) { Console.Error.WriteLine($"清理参考模组备份失败: {entry.Backup}: {ex.Message}"); }
        }
    }

    private static void RestoreTarget(CopyTarget entry)
    {
        if (Directory.Exists(entry.Target)) Directory.Delete(entry.Target, true);
        else if (File.Exists(entry.Target)) File.Delete(entry.Target);
        if (Directory.Exists(entry.Backup)) Directory.Move(entry.Backup, entry.Target);
        else if (File.Exists(entry.Backup)) File.Move(entry.Backup, entry.Target);
    }

    private static string QueryArgument(string[] args, string command)
    {
        var commandIndex = Array.FindIndex(args, x => x.Equals(command, StringComparison.OrdinalIgnoreCase));
        for (var index = commandIndex + 1; commandIndex >= 0 && index < args.Length; index++)
        {
            if (args[index].Equals("--version", StringComparison.OrdinalIgnoreCase)) { index++; continue; }
            if (!args[index].StartsWith('-')) return args[index];
        }
        throw new ArgumentException($"缺少查询参数: mod {command} <query>");
    }

    private static List<ModCandidate> FindCandidates(ProjectContext context, string keyword, bool fuzzy, string? version)
    {
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(context.Config.RimWorldDir)) roots.Add(Path.Combine(context.Config.RimWorldDir, "Mods"));
        roots.AddRange(context.Config.WorkshopPaths);

        var candidates = new List<ModCandidate>();
        foreach (var root in roots.Where(Directory.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var modDirectory in Directory.EnumerateDirectories(root))
            {
                var candidate = ParseCandidate(Path.Combine(modDirectory, "About", "About.xml"), version);
                if (candidate is not null && (string.IsNullOrWhiteSpace(keyword) || Matches(candidate, keyword, fuzzy))) candidates.Add(candidate);
            }
        }

        return candidates
            .GroupBy(x => x.SourceDirectory, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SourceDirectory, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool Matches(ModCandidate candidate, string keyword, bool fuzzy)
    {
        var values = new[] { candidate.Name, candidate.PackageId, candidate.FolderName, candidate.Author ?? "" };
        if (values.Any(x => x.Equals(keyword, StringComparison.OrdinalIgnoreCase))) return true;
        if (values.Any(x => x.Contains(keyword, StringComparison.OrdinalIgnoreCase))) return true;
        return fuzzy && values.Any(x => IsSubsequence(x, keyword));
    }

    private static bool IsSubsequence(string source, string query)
    {
        var cursor = 0;
        foreach (var character in query)
        {
            var index = source.IndexOf(character.ToString(), cursor, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return false;
            cursor = index + 1;
        }
        return query.Length > 0;
    }

    private static ModCandidate? ResolveUnique(ProjectContext context, string query, bool fuzzy, string? version, out CliError? error)
    {
        error = null;
        if (Path.IsPathRooted(query) && Directory.Exists(query))
        {
            var direct = ParseCandidate(Path.Combine(query, "About", "About.xml"), version);
            if (direct is not null) return direct;
            error = new CliError("MOD_INVALID", "绝对目录中没有有效 About.xml");
            return null;
        }

        var all = FindCandidates(context, "", false, version);
        var levels = new Func<ModCandidate, bool>[]
        {
            x => x.PackageId.Equals(query, StringComparison.OrdinalIgnoreCase),
            x => x.Name.Equals(query, StringComparison.OrdinalIgnoreCase),
            x => x.FolderName.Equals(query, StringComparison.OrdinalIgnoreCase),
            x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || x.PackageId.Contains(query, StringComparison.OrdinalIgnoreCase)
                || x.FolderName.Contains(query, StringComparison.OrdinalIgnoreCase)
        };

        ModCandidate[] matches = [];
        foreach (var level in levels)
        {
            matches = all.Where(level).ToArray();
            if (matches.Length > 0) break;
        }
        if (matches.Length == 0 && fuzzy) matches = all.Where(x => Matches(x, query, true)).ToArray();
        if (matches.Length == 1) return matches[0];

        error = matches.Length == 0
            ? new CliError("MOD_NOT_FOUND", $"未找到参考模组: {query}")
            : new CliError("MOD_AMBIGUOUS", $"查询存在多个候选: {query}", matches);
        return null;
    }

    private static ModCandidate? ParseCandidate(string aboutPath, string? version)
    {
        try
        {
            if (!File.Exists(aboutPath)) return null;
            var root = XDocument.Load(aboutPath).Root;
            if (root is null) return null;
            var package = ElementValue(root, "packageId");
            if (string.IsNullOrWhiteSpace(package)) return null;

            var modRoot = Directory.GetParent(aboutPath)?.Parent?.FullName ?? "";
            var dependencies = ParseDependencies(root, version);
            return new ModCandidate(
                ElementValue(root, "name") ?? Path.GetFileName(modRoot),
                package,
                Path.GetFileName(modRoot),
                modRoot,
                package.StartsWith("ludeon.rimworld", StringComparison.OrdinalIgnoreCase),
                ElementValue(root, "author"),
                dependencies);
        }
        catch
        {
            return null;
        }
    }

    private static List<string> ParseDependencies(XElement root, string? version)
    {
        var dependencies = new List<string>();
        AddDependencies(root.Elements().FirstOrDefault(x => x.Name.LocalName.Equals("modDependencies", StringComparison.OrdinalIgnoreCase)), dependencies);

        var byVersion = root.Elements().FirstOrDefault(x => x.Name.LocalName.Equals("modDependenciesByVersion", StringComparison.OrdinalIgnoreCase));
        if (byVersion is not null)
        {
            var versionNode = !string.IsNullOrWhiteSpace(version)
                ? byVersion.Elements().FirstOrDefault(x => x.Name.LocalName.Equals($"v{version}", StringComparison.OrdinalIgnoreCase))
                : null;
            versionNode ??= byVersion.Elements().LastOrDefault();
            AddDependencies(versionNode, dependencies);
        }
        return dependencies.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddDependencies(XElement? container, List<string> dependencies)
    {
        if (container is null) return;
        foreach (var item in container.Elements().Where(x => x.Name.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase)))
        {
            var package = ElementValue(item, "packageId");
            if (!string.IsNullOrWhiteSpace(package)) dependencies.Add(package);
        }
    }

    private static string? ElementValue(XElement root, string name)
        => root.Elements().FirstOrDefault(x => x.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Trim();

    private static void CopyReference(string source, string destination, IReadOnlyCollection<string> ignored, CancellationToken cancellationToken)
    {
        CopyDirectoryRecursive(Path.GetFullPath(source), Path.GetFullPath(destination), ignored, cancellationToken);
    }

    private static void CopyDirectoryRecursive(string source, string destination, IReadOnlyCollection<string> ignored, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = new DirectoryInfo(source);
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException($"拒绝跟随 Reparse Point: {source}");
        Directory.CreateDirectory(destination);

        foreach (var file in info.EnumerateFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException($"拒绝跟随 Reparse Point: {file.FullName}");
            file.CopyTo(Path.Combine(destination, file.Name), true);
        }
        foreach (var directory in info.EnumerateDirectories())
        {
            if (ignored.Contains(directory.Name, StringComparer.OrdinalIgnoreCase)) continue;
            CopyDirectoryRecursive(directory.FullName, Path.Combine(destination, directory.Name), ignored, cancellationToken);
        }
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (Exception ex) { Console.Error.WriteLine($"清理目录失败: {path}: {ex.Message}"); }
    }

    private static void DeleteDirectoryIfEmpty(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
        }
        catch (Exception ex) { Console.Error.WriteLine($"清理空目录失败: {path}: {ex.Message}"); }
    }

    internal sealed record CopyTarget(ModCandidate Mod, string Target, string Staging, string Backup);
    internal sealed record SkippedDependency(string PackageId, string Reason, List<string> RequiredBy);
}

internal sealed class TargetDirectoryLock : IDisposable
{
    private readonly FileStream _stream;
    private TargetDirectoryLock(FileStream stream) => _stream = stream;

    public static TargetDirectoryLock? Create(string root, out string? error)
    {
        error = null;
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, ".rimdev-target.lock");
        try { return new TargetDirectoryLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)); }
        catch (IOException) { error = $"目标目录正被占用: {root}"; return null; }
    }

    public void Dispose() => _stream.Dispose();
}
