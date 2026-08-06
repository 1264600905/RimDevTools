using System.Xml.Linq;

namespace RimDevTools;

public sealed record ModCandidate(string Name, string PackageId, string FolderName, string SourceDirectory, bool IsOfficial, List<string> Dependencies);

public static class RefCommand
{
    public static Task<int> SearchAsync(string[] args)
    {
        var context = CliApplication.RequireProject("ref search", out var code); if (context is null) return Task.FromResult(code);
        var keyword = args.FirstOrDefault(x => !x.StartsWith('-') && x is not "ref" and not "search") ?? "";
        var candidates = FindCandidates(context, keyword, CliArgs.Has(args, "--fuzzy"));
        return Task.FromResult(CliResult.WriteSuccess("ref search", context.Root, new { keyword, fuzzy = CliArgs.Has(args, "--fuzzy"), candidates }));
    }

    public static Task<int> DepsAsync(string[] args)
    {
        var context = CliApplication.RequireProject("ref deps", out var code); if (context is null) return Task.FromResult(code);
        var query = QueryArgument(args, "deps");
        var resolved = ResolveUnique(context, query, CliArgs.Has(args, "--fuzzy"), out var error);
        if (resolved is null) return Task.FromResult(CliResult.WriteFailure("ref deps", ExitCodes.InvalidArguments, error!.Code, error.Message, context.Root, error.Details));
        return Task.FromResult(CliResult.WriteSuccess("ref deps", context.Root, new { target = resolved, dependencies = resolved.Dependencies }));
    }

    public static Task<int> RelatedAsync(string[] args)
    {
        var context = CliApplication.RequireProject("ref related", out var code); if (context is null) return Task.FromResult(code);
        var query = QueryArgument(args, "related");
        var resolved = ResolveUnique(context, query, CliArgs.Has(args, "--fuzzy"), out var error);
        if (resolved is null) return Task.FromResult(CliResult.WriteFailure("ref related", ExitCodes.InvalidArguments, error!.Code, error.Message, context.Root, error.Details));
        var related = FindCandidates(context, "", false).Where(x => x.Dependencies.Contains(resolved.PackageId, StringComparer.OrdinalIgnoreCase)).ToArray();
        return Task.FromResult(CliResult.WriteSuccess("ref related", context.Root, new { target = resolved, related }));
    }

    public static async Task<int> PullAsync(string[] args)
    {
        var context = CliApplication.RequireProject("ref pull", out var code); if (context is null) return code;
        var query = QueryArgument(args, "pull");
        var selected = ResolveUnique(context, query, CliArgs.Has(args, "--fuzzy"), out var error);
        if (selected is null) return CliResult.WriteFailure("ref pull", ExitCodes.InvalidArguments, error!.Code, error.Message, context.Root, error.Details);
        var all = CliArgs.Has(args, "--all");
        var closure = new List<ModCandidate>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<ModCandidate>([selected]);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!seen.Add(current.PackageId)) continue;
            closure.Add(current);
            if (!CliArgs.Has(args, "--deps")) continue;
            foreach (var dependency in current.Dependencies)
            {
                var dep = ResolveUnique(context, dependency, false, out var depError);
                if (dep is null) return CliResult.WriteFailure("ref pull", ExitCodes.InvalidArguments, depError!.Code, $"依赖 {dependency}: {depError.Message}", context.Root, depError.Details);
                pending.Enqueue(dep);
            }
        }
        var targetRoot = Path.GetFullPath(Path.Combine(context.Root, "参考文档")); Directory.CreateDirectory(targetRoot);
        var targets = closure.Select(x => new { mod = x, target = Path.Combine(targetRoot, SafeFolderName(x.PackageId)) }).ToArray();
        var conflicts = targets.Where(x => Directory.Exists(x.target) || File.Exists(x.target)).Select(x => x.target).ToArray();
        if (conflicts.Length > 0 && !CliArgs.Has(args, "--force")) return CliResult.WriteFailure("ref pull", ExitCodes.TaskFailed, "TARGET_CONFLICT", "目标目录已存在，请使用 --force", context.Root, new { conflicts });
        var runId = Guid.NewGuid().ToString("N"); var stagingRoot = Path.Combine(targetRoot, ".rimdev-staging", runId);
        using var targetLock = TargetDirectoryLock.Create(targetRoot, out var lockError);
        if (targetLock is null) return CliResult.WriteFailure("ref pull", ExitCodes.Busy, "TARGET_BUSY", lockError!, context.Root);
        var results = new List<object>();
        try
        {
            foreach (var entry in targets)
            {
                var staging = Path.Combine(stagingRoot, SafeFolderName(entry.mod.PackageId));
                Directory.CreateDirectory(staging);
                try
                {
                    CopyReference(entry.mod.SourceDirectory, staging, all ? [] : context.Config.PullIgnoreFolders);
                    var assemblies = Directory.EnumerateFiles(staging, "*.dll", SearchOption.AllDirectories).ToArray();
                    var decompiled = 0;
                    if (assemblies.Length > 0)
                    {
                        var ilspy = DoctorCommand.ResolveIlspyPath(context);
                        if (ilspy is null || !File.Exists(ilspy)) throw new FileNotFoundException("找不到 ilspycmd，请运行 doctor", ilspy);
                        foreach (var assembly in assemblies)
                        {
                            var output = Path.Combine(Path.GetDirectoryName(assembly)!, "Decompiled");
                            Directory.CreateDirectory(output);
                            if (!await ProcessUtil.RunAsync(ilspy, $"-p -o \"{output}\" \"{assembly}\"", context.Root)) throw new InvalidOperationException($"反编译失败: {assembly}");
                            decompiled++;
                        }
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(entry.target)!);
                    if (Directory.Exists(entry.target)) Directory.Delete(entry.target, true); else if (File.Exists(entry.target)) File.Delete(entry.target);
                    Directory.Move(staging, entry.target);
                    results.Add(new { name = entry.mod.Name, packageId = entry.mod.PackageId, sourceDirectory = entry.mod.SourceDirectory, targetDirectory = entry.target, decompiledAssemblies = decompiled, ok = true });
                }
                catch (Exception ex)
                {
                    results.Add(new { name = entry.mod.Name, packageId = entry.mod.PackageId, sourceDirectory = entry.mod.SourceDirectory, targetDirectory = entry.target, ok = false, error = ex.Message });
                    return CliResult.WriteFailure("ref pull", ExitCodes.TaskFailed, "PULL_FAILED", $"拉取 {entry.mod.PackageId} 失败: {ex.Message}", context.Root, new { runId, results });
                }
            }
            return CliResult.WriteSuccess("ref pull", context.Root, new { runId, results });
        }
        finally { if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, true); }
    }

    private static string QueryArgument(string[] args, string command) => args.FirstOrDefault(x => !x.StartsWith('-') && x is not "ref" and not "pull" and not "deps" and not "related") ?? throw new ArgumentException($"缺少查询参数: ref {command} <query>");

    private static List<ModCandidate> FindCandidates(ProjectContext context, string keyword, bool fuzzy)
    {
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(context.Config.RimWorldDir)) roots.Add(Path.Combine(context.Config.RimWorldDir, "Mods"));
        roots.AddRange(context.Config.WorkshopPaths);
        var candidates = new List<ModCandidate>();
        foreach (var root in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var about in Directory.EnumerateFiles(root, "About.xml", SearchOption.AllDirectories))
            {
                var candidate = ParseCandidate(about); if (candidate is null) continue;
                if (string.IsNullOrWhiteSpace(keyword) || Matches(candidate, keyword, fuzzy)) candidates.Add(candidate);
            }
        }
        return candidates.GroupBy(x => x.PackageId, StringComparer.OrdinalIgnoreCase).Select(x => x.OrderBy(y => y.SourceDirectory.Length).First()).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool Matches(ModCandidate c, string keyword, bool fuzzy)
    {
        var values = new[] { c.Name, c.PackageId, c.FolderName };
        if (values.Any(x => x.Equals(keyword, StringComparison.OrdinalIgnoreCase))) return true;
        if (values.Any(x => x.Contains(keyword, StringComparison.OrdinalIgnoreCase))) return true;
        if (!fuzzy) return false;
        return values.Any(x => FuzzyScore(x, keyword) >= Math.Max(1, keyword.Length * 0.55));
    }

    private static int FuzzyScore(string source, string query)
    {
        var score = 0; var cursor = 0;
        foreach (var c in query.ToLowerInvariant()) { var index = source.ToLowerInvariant().IndexOf(c, cursor); if (index < 0) return 0; score++; cursor = index + 1; }
        return score;
    }

    private static ModCandidate? ResolveUnique(ProjectContext context, string query, bool fuzzy, out CliError? error)
    {
        error = null;
        if (Path.IsPathRooted(query) && Directory.Exists(query)) { var direct = ParseCandidate(Path.Combine(query, "About", "About.xml")); if (direct is not null) return direct; error = new("MOD_INVALID", "绝对目录中没有有效 About.xml"); return null; }
        var all = FindCandidates(context, "", false);
        var levels = new Func<ModCandidate, bool>[] { x => x.PackageId.Equals(query, StringComparison.OrdinalIgnoreCase), x => x.Name.Equals(query, StringComparison.OrdinalIgnoreCase), x => x.FolderName.Equals(query, StringComparison.OrdinalIgnoreCase), x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || x.PackageId.Contains(query, StringComparison.OrdinalIgnoreCase) || x.FolderName.Contains(query, StringComparison.OrdinalIgnoreCase) };
        IEnumerable<ModCandidate> matches = [];
        foreach (var level in levels) { matches = all.Where(level).ToArray(); if (matches.Any()) break; }
        if (!matches.Any() && fuzzy) matches = all.Where(x => Matches(x, query, true));
        var array = matches.ToArray();
        if (array.Length == 1) return array[0];
        error = array.Length == 0 ? new("MOD_NOT_FOUND", $"未找到参考模组: {query}") : new("MOD_AMBIGUOUS", $"查询存在多个候选: {query}", array);
        return null;
    }

    private static ModCandidate? ParseCandidate(string aboutPath)
    {
        try
        {
            var root = XDocument.Load(aboutPath).Root; if (root is null) return null;
            var package = root.Element("packageId")?.Value.Trim(); if (string.IsNullOrWhiteSpace(package)) return null;
            var modRoot = Directory.GetParent(aboutPath)?.Parent?.FullName ?? "";
            var deps = root.Descendants("modDependencies").Elements("li").Select(x => x.Element("packageId")?.Value.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return new ModCandidate(root.Element("name")?.Value.Trim() ?? Path.GetFileName(modRoot), package, Path.GetFileName(modRoot), modRoot, false, deps);
        }
        catch { return null; }
    }

    private static void CopyReference(string source, string dest, IReadOnlyCollection<string> ignored)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file); var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Any(x => ignored.Contains(x, StringComparer.OrdinalIgnoreCase))) continue;
            var target = Path.Combine(dest, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target, true);
        }
    }

    private static string SafeFolderName(string packageId) => string.Concat(packageId.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}

internal sealed class TargetDirectoryLock : IDisposable
{
    private readonly FileStream _stream;
    private TargetDirectoryLock(FileStream stream) => _stream = stream;
    public static TargetDirectoryLock? Create(string root, out string? error)
    {
        error = null; Directory.CreateDirectory(root); var path = Path.Combine(root, ".rimdev-target.lock");
        try { return new TargetDirectoryLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)); }
        catch (IOException) { error = $"目标目录正被占用: {root}"; return null; }
    }
    public void Dispose() => _stream.Dispose();
}
