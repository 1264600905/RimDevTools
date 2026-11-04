using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RimDevTools;

internal sealed record DeploymentFingerprint(string Path, long Bytes, string Sha256, string Source = "input");
internal sealed record DeploymentReceipt(int SchemaVersion, string ToolVersion, string DeployRunId, string State,
    string Mode, string ProjectRoot, string TargetRoot, string? PackageId, string? GameVersion, string ConfigFingerprint,
    DateTimeOffset CompletedAt, DeploymentFingerprint[] Inputs, DeploymentFingerprint[] Files,
    string? PublishedFileId, string AssetPackState, bool PublishReady, bool SyncSource, OfficialBakeResult? AssetBake = null);
internal sealed record DeploymentDifference(string Scope, string Kind, string Path);
internal sealed record DeploymentVerification(bool Valid, bool PublishReady, string? DeployRunId, string Target,
    string? Action, string? PublishedFileId, string? AssetPackState, DeploymentDifference[] Differences, string[] Artifacts);

internal static class DeploymentReceiptStore
{
    internal static string PathFor(ProjectContext context) => ProjectDataPaths.File(context.Root, "deployment-receipt.json");
    internal static DeploymentReceipt? Read(ProjectContext context)
    {
        var path = PathFor(context);
        return File.Exists(path) ? JsonSerializer.Deserialize<DeploymentReceipt>(File.ReadAllText(path), JsonDefaults.Storage) : null;
    }
    internal static void Save(ProjectContext context, DeploymentReceipt receipt)
        => AtomicFile.WriteAllText(PathFor(context), JsonSerializer.Serialize(receipt, JsonDefaults.StorageIndented));
    internal static void Invalidate(ProjectContext context, string runId, string target, bool hot, bool syncSource)
        => Save(context, new(1, typeof(CliApplication).Assembly.GetName().Version!.ToString(), runId, "in_progress",
            hot ? "xml_hot" : "full", Path.GetFullPath(context.Root), target, context.Config.CurrentPackageId(context.Root),
            context.Config.GameVersion, ConfigurationFingerprint(context, syncSource), DateTimeOffset.UtcNow,
            [], [], null, "unverified", false, syncSource));

    internal static string ConfigurationFingerprint(ProjectContext context, bool syncSource)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            context.Config.ModName, context.Config.GameVersion, context.Config.RimWorldDir, context.Config.CsprojPaths,
            context.Config.SyncItems, context.Config.ProtectedFilesRegex, context.Config.ShaderBundle, syncSource
        }, JsonDefaults.Storage))));

    internal static DeploymentFingerprint HashFile(string path, string label, string source = "input")
    {
        using var stream = File.OpenRead(path);
        return new(label.Replace('\\', '/'), stream.Length, Convert.ToHexString(SHA256.HashData(stream)), source);
    }

    internal static DeploymentFingerprint[] ScanTarget(string target, string? staging = null)
        => Files(target, skipData: false).Select(path => HashFile(path, Path.GetRelativePath(target, path),
            IsIdentity(path) ? "identity" : staging is not null && File.Exists(Path.Combine(staging, Path.GetRelativePath(target, path))) ? "deployed" : "retained"))
            .OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();

    internal static bool IsIdentity(string path) => path.Replace('\\', '/').EndsWith("/About/PublishedFileId.txt", StringComparison.OrdinalIgnoreCase);

    internal static IEnumerable<string> Files(string root, bool skipData = true)
    {
        if (File.Exists(root)) { yield return root; yield break; }
        if (!Directory.Exists(root)) yield break;
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            PathSafety.EnsureNoReparsePoints(root, directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (skipData && ProjectDataPaths.IsDataPath(entry)) continue;
                PathSafety.EnsureNoReparsePoints(root, entry);
                if (Directory.Exists(entry)) pending.Push(entry); else yield return entry;
            }
        }
    }

    private static bool BuildGenerated(string path)
        => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(x => x.Equals("bin", StringComparison.OrdinalIgnoreCase) || x.Equals("obj", StringComparison.OrdinalIgnoreCase)
                || x.Equals(".git", StringComparison.OrdinalIgnoreCase) || x.Equals("TestResults", StringComparison.OrdinalIgnoreCase)
                || x.Equals("log", StringComparison.OrdinalIgnoreCase));

    // Ask the real evaluated project for Compile/resources, imports and ProjectReference inputs.
    // This catches linked sources and external props as well as new files in SDK globs.
    internal static async Task<DeploymentFingerprint[]> CaptureInputsAsync(ProjectContext context, bool syncSource, CancellationToken token = default, ISet<string>? buildOutputs = null)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var evaluated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = RimWorldEnvironmentResolver.Resolve(context).References;
        foreach (var project in context.Config.CsprojPaths) await Evaluate(Path.GetFullPath(project, context.Root));
        foreach (var item in context.Config.SyncItems.Concat(syncSource ? ["source"] : Array.Empty<string>()))
        {
            var root = Path.GetFullPath(item, context.Root);
            foreach (var path in Files(root))
                if (!BuildGenerated(path) && !outputs.Contains(path) && !path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
                    && !path.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)) paths.Add(path);
        }
        foreach (var path in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "NuGet.Config" })
            if (File.Exists(Path.Combine(context.Root, path))) paths.Add(Path.Combine(context.Root, path));
        return paths.Where(path => !ProjectDataPaths.IsDataPath(path)).Select(path => HashFile(path, Path.GetRelativePath(context.Root, path), IsIdentity(path) ? "identity" : "input"))
            .OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();

        async Task Evaluate(string project)
        {
            if (!evaluated.Add(project)) return;
            paths.Add(project);
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = context.Root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "msbuild", project, "-nologo", "-property:Configuration=Release", $"-property:RimWorldDir={context.Config.RimWorldDir}",
                $"-property:RimWorldVersion={context.Config.GameVersion}", $"-property:HarmonyDllPath={references.HarmonyDllPath}",
                $"-property:RimWorldAssemblyDir={references.AssemblyDirectory}", $"-property:RimWorldManagedDir={references.AssemblyDirectory}",
                "-getItem:Compile,EmbeddedResource,Content,None,AdditionalFiles,Analyzer,ProjectReference,Reference",
                "-getProperty:MSBuildAllProjects,TargetPath,ProjectAssetsFile" }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new IOException("无法评估项目构建输入");
            var stdout = process.StandardOutput.ReadToEndAsync(token);
            var stderr = process.StandardError.ReadToEndAsync(token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); throw; }
            var output = await stdout; var errors = await stderr;
            if (process.ExitCode != 0) throw new IOException("无法评估实际构建输入: " + errors + output);
            using var doc = JsonDocument.Parse(output);
            var properties = doc.RootElement.GetProperty("Properties");
            var target = properties.GetProperty("TargetPath").GetString();
            if (!string.IsNullOrWhiteSpace(target))
            {
                var fullTarget = Path.GetFullPath(target, Path.GetDirectoryName(project)!);
                outputs.Add(fullTarget); buildOutputs?.Add(fullTarget);
            }
            foreach (var import in properties.GetProperty("MSBuildAllProjects").GetString()!.Split(';', StringSplitOptions.RemoveEmptyEntries))
                if (File.Exists(import)) paths.Add(Path.GetFullPath(import));
            // MSBuildAllProjects is incomplete for modern SDK projects. Preprocess on stdout
            // supplies the actual, conditionally expanded import graph without writing a report.
            for (var i = start.ArgumentList.Count - 1; i >= 0; i--)
                if (start.ArgumentList[i].StartsWith("-get", StringComparison.Ordinal)) start.ArgumentList.RemoveAt(i);
            start.ArgumentList.Add("-preprocess");
            using (var imports = Process.Start(start) ?? throw new IOException("无法读取构建导入图"))
            {
                var preprocessed = imports.StandardOutput.ReadToEndAsync(token);
                var diagnostics = imports.StandardError.ReadToEndAsync(token);
                using var importTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                importTimeout.CancelAfter(TimeSpan.FromSeconds(60));
                try { await imports.WaitForExitAsync(importTimeout.Token); }
                catch { if (!imports.HasExited) imports.Kill(true); await imports.WaitForExitAsync(CancellationToken.None); throw; }
                var xml = await preprocessed; var importError = await diagnostics;
                if (imports.ExitCode != 0) throw new IOException("读取构建导入图失败: " + importError);
                foreach (var line in xml.Split('\n'))
                {
                    var candidate = line.Trim();
                    if (Path.IsPathRooted(candidate) && File.Exists(candidate)) paths.Add(Path.GetFullPath(candidate));
                }
            }
            var assets = properties.GetProperty("ProjectAssetsFile").GetString();
            if (!string.IsNullOrWhiteSpace(assets) && File.Exists(assets)) paths.Add(Path.GetFullPath(assets));
            foreach (var group in doc.RootElement.GetProperty("Items").EnumerateObject())
            foreach (var item in group.Value.EnumerateArray())
            {
                var full = item.TryGetProperty("FullPath", out var value) ? value.GetString() : null;
                if (group.Name == "Reference")
                {
                    full = item.TryGetProperty("HintPath", out value) ? value.GetString() : null;
                    if (full is not null) full = Path.GetFullPath(full, Path.GetDirectoryName(project)!);
                }
                if (string.IsNullOrWhiteSpace(full) || ProjectDataPaths.IsDataPath(full)) continue;
                if (group.Name == "ProjectReference") await Evaluate(full);
                if (File.Exists(full) && !BuildGenerated(full)) paths.Add(Path.GetFullPath(full));
                else if (group.Name is "Compile" or "EmbeddedResource" && !BuildGenerated(full))
                    throw new FileNotFoundException("构建输入缺失", full);
            }
        }
    }

    internal static DeploymentDifference[] Compare(string scope, DeploymentFingerprint[] expected, DeploymentFingerprint[] current)
    {
        var before = expected.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        var after = current.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        return before.Keys.Union(after.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)
            .Select(path => !before.TryGetValue(path, out var old) ? new DeploymentDifference(scope, "added", path)
                : !after.TryGetValue(path, out var now) ? new(scope, "missing", path)
                : old.Bytes != now.Bytes || old.Sha256 != now.Sha256 ? new(scope, "modified", path) : null)
            .OfType<DeploymentDifference>().ToArray();
    }

    internal static void RememberBinding(ProjectContext context, string? id)
    {
        var package = context.Config.CurrentPackageId(context.Root) ?? throw new InvalidDataException("缺少 packageId");
        var path = ProjectDataPaths.File(context.Root, "steamworkshops-bindings.json");
        var bindings = File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), JsonDefaults.Storage)
            ?? throw new InvalidDataException("绑定记录无效") : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (bindings.TryGetValue(package, out var existing) && existing != id)
            throw new InvalidDataException("已知工坊绑定丢失或冲突: " + existing);
        if (id is not null) { bindings[package] = id; AtomicFile.WriteAllText(path, JsonSerializer.Serialize(bindings, JsonDefaults.StorageIndented)); }
    }

    internal static string? KnownBinding(ProjectContext context)
    {
        var path = ProjectDataPaths.File(context.Root, "steamworkshops-bindings.json");
        if (!File.Exists(path)) return null;
        var bindings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), JsonDefaults.Storage)
            ?? throw new InvalidDataException("绑定记录无效");
        return bindings.FirstOrDefault(x => x.Key.Equals(context.Config.CurrentPackageId(context.Root), StringComparison.OrdinalIgnoreCase)).Value;
    }
}

internal static class DeploymentVerifier
{
    internal static async Task<DeploymentVerification> VerifyAsync(ProjectContext context, CancellationToken token = default)
    {
        var game = SteamworksSessionProbe.GameDirectory(context) ?? throw new InvalidDataException("未找到游戏目录");
        var target = Path.GetFullPath(Path.Combine(game, "Mods", context.Config.ModName ?? ""));
        if (target.Equals(Path.Combine(game, "Mods"), StringComparison.OrdinalIgnoreCase) || !PathSafety.IsWithin(target, Path.Combine(game, "Mods")))
            throw new InvalidDataException("部署目标无效");
        var differences = new List<DeploymentDifference>();
        var path = DeploymentReceiptStore.PathFor(context);
        DeploymentReceipt? receipt;
        try { receipt = DeploymentReceiptStore.Read(context); }
        catch { receipt = null; }
        var artifacts = File.Exists(path) ? new[] { path } : [];
        if (receipt is null || receipt.SchemaVersion != 1 || receipt.State != "completed" || receipt.Mode != "full")
            return new(false, false, receipt?.DeployRunId, target, null, null, receipt?.AssetPackState, [new("receipt", "receiptInvalid", path)], artifacts);
        if (!receipt.ProjectRoot.Equals(Path.GetFullPath(context.Root), StringComparison.OrdinalIgnoreCase)
            || !receipt.TargetRoot.Equals(target, StringComparison.OrdinalIgnoreCase) || receipt.PackageId != context.Config.CurrentPackageId(context.Root)
            || receipt.GameVersion != context.Config.GameVersion || receipt.ConfigFingerprint != DeploymentReceiptStore.ConfigurationFingerprint(context, receipt.SyncSource))
            differences.Add(new("receipt", "identityMismatch", path));
        PathSafety.EnsureNoReparsePoints(game, target);
        differences.AddRange(DeploymentReceiptStore.Compare("target", receipt.Files, DeploymentReceiptStore.ScanTarget(target)));
        differences.AddRange(DeploymentReceiptStore.Compare("project", receipt.Inputs, await DeploymentReceiptStore.CaptureInputsAsync(context, receipt.SyncSource, token)));
        if (receipt.AssetBake is { } bake)
        {
            foreach (var library in bake.Libraries)
            {
                if (!File.Exists(library.Path)) differences.Add(new("assetLibrary", "missing", library.Path));
                else if (DeploymentReceiptStore.HashFile(library.Path, library.Path).Sha256 != library.Sha256)
                    differences.Add(new("assetLibrary", "modified", library.Path));
            }
        }
        var id = WorkshopUploadCommand.ReadId(Path.Combine(context.Root, "About", "PublishedFileId.txt"))?.ToString();
        var deployed = WorkshopUploadCommand.ReadId(Path.Combine(target, "About", "PublishedFileId.txt"))?.ToString();
        var known = DeploymentReceiptStore.KnownBinding(context);
        if (id != deployed || id != receipt.PublishedFileId || known is not null && id != known)
            differences.Add(new("binding", "identityMismatch", "About/PublishedFileId.txt"));
        return new(differences.Count == 0, differences.Count == 0 && receipt.PublishReady, receipt.DeployRunId, target,
            id is null ? "create" : "update", id, receipt.AssetPackState, differences.ToArray(), artifacts);
    }
}
