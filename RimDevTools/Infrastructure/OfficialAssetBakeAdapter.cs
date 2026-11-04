using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;

namespace RimDevTools;

internal sealed record OfficialBakeRequest(string GameDirectory, string StagingDirectory, bool InspectOnly = false);
internal sealed record OfficialBakedPack(string LoadFolder, string PackPath, int ManifestEntries,
    int SourceTextures, int SourceSounds, Dictionary<string, string> Settings, string Backend,
    Dictionary<string, string> EntrySha256);
internal sealed record OfficialBakeResult(bool Succeeded, string State, OfficialBakedPack[] Packs,
    DeploymentFingerprint[] Libraries, string[] Errors);

internal static class OfficialAssetBakeAdapter
{
    private const string Prefix = "RIMDEVTOOLS_OFFICIAL_BAKE=";
    internal static bool RequiresBaking(string? gameVersion) => gameVersion == "1.7";

    internal static async Task<OfficialBakeResult> BakeAsync(ProjectContext context, string staging, CancellationToken token)
    {
        // The 1.6 path must not load/probe modern game libraries or launch a helper.
        if (!RequiresBaking(context.Config.GameVersion)) return new(true, "not_required", [], [], []);
        var game = SteamworksSessionProbe.GameDirectory(context) ?? throw new InvalidDataException("无法定位游戏目录");
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            WorkingDirectory = game, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(typeof(CliApplication).Assembly.Location);
        start.ArgumentList.Add("--official-bake-helper");
        using var process = Process.Start(start) ?? throw new IOException("无法启动官方资源烘焙 helper");
        OfficialBakeResult? result = null;
        var output = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                if (line.StartsWith(Prefix, StringComparison.Ordinal)) result = JsonSerializer.Deserialize<OfficialBakeResult>(line[Prefix.Length..], JsonDefaults.Storage);
                else if (!string.IsNullOrWhiteSpace(line)) Console.Error.WriteLine(line);
            }
        });
        var error = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new OfficialBakeRequest(game, staging), JsonDefaults.Storage));
        process.StandardInput.Close();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync(CancellationToken.None); await output; await error;
            throw;
        }
        await output;
        var diagnostic = await error;
        if (!string.IsNullOrWhiteSpace(diagnostic)) Console.Error.WriteLine(diagnostic.Trim());
        return result ?? new(false, "failed", [], [], [$"烘焙 helper 未返回结果，exit={process.ExitCode}: {diagnostic}"]);
    }

    internal static int RunHelper()
    {
        var request = JsonSerializer.Deserialize<OfficialBakeRequest>(Console.ReadLine() ?? "", JsonDefaults.Storage)
            ?? throw new InvalidDataException("缺少官方烘焙请求");
        OfficialBakeResult result;
        try { result = Bake(request); }
        catch (Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException is not null) ex = ex.InnerException;
            result = new(false, "failed", [], [], [ex.ToString()]);
        }
        Console.WriteLine(Prefix + JsonSerializer.Serialize(result, JsonDefaults.Storage));
        return result.Succeeded ? 0 : ExitCodes.TaskFailed;
    }

    private static OfficialBakeResult Bake(OfficialBakeRequest request)
    {
        var game = Path.GetFullPath(request.GameDirectory);
        var staging = Path.GetFullPath(request.StagingDirectory);
        var mods = Path.Combine(game, "Mods");
        if (!PathSafety.IsWithin(staging, mods) || !Path.GetFileName(staging).StartsWith(".rimdev-staging-", StringComparison.Ordinal))
            throw new InvalidDataException("烘焙只允许 deploy 的内部 staging 目录");
        PathSafety.EnsureNoReparsePoints(game, staging);
        if (RimWorldGameLayout.ValidateGameDirectory(game, "1.7") is { } invalid) throw new InvalidDataException(invalid);
        var nativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            var path = Path.Combine(game, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) =>
        {
            foreach (var directory in new[] { game, Path.Combine(game, "runtimes", "win-x64", "native") })
            foreach (var filename in new[] { name, name + ".dll" })
            {
                var path = Path.Combine(directory, filename);
                if (File.Exists(path)) { nativePaths.Add(path); return NativeLibrary.Load(path); }
            }
            return IntPtr.Zero;
        };
        var verse = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, "Assembly-CSharp.dll"));
        var unity = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, "UnityStubs.dll"));
        var waylith = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, "Waylith.dll"));
        var loadFoldersType = verse.GetType("Verse.ModLoadFolders", true)!;
        var loadFolders = Activator.CreateInstance(loadFoldersType)!;
        var foldersXml = Path.Combine(staging, "LoadFolders.xml");
        if (File.Exists(foldersXml))
        {
            var xml = new XmlDocument(); xml.Load(foldersXml);
            loadFoldersType.GetMethod("LoadDataFromXmlCustom")!.Invoke(loadFolders, [xml.DocumentElement!]);
        }
        var selection = verse.GetType("Verse.ModContentPack", true)!.GetMethod("AllLoadFolders", [typeof(string), loadFoldersType])!;
        var folders = ((IEnumerable)selection.Invoke(null, [staging, loadFolders])!).Cast<string>().ToArray();
        var assetPacks = unity.GetType("UnityStubs.ModAssetPacks", true)!;
        var contentPacks = unity.GetType("UnityStubs.ContentPacks", true)!;
        var assetPackType = waylith.GetType("Waylith.Assets.AssetPack", true)!;
        var packs = new List<OfficialBakedPack>();
        foreach (var folder in folders)
        {
            if (!PathSafety.IsWithinOrEqual(folder, staging)) throw new InvalidDataException("官方加载目录越出暂存目录: " + folder);
            PathSafety.EnsureNoReparsePoints(staging, folder);
            if (!(bool)assetPacks.GetMethod("HasBakeableContent")!.Invoke(null, [folder])!) continue;
            var requestMethod = assetPacks.GetMethod("Request")!;
            var requestArguments = requestMethod.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
            requestArguments[0] = folder; requestArguments[1] = Path.Combine(folder, "Assets.zip"); requestArguments[2] = false;
            var bakeRequest = requestMethod.Invoke(null, requestArguments)!;
            var settingsObject = Get(bakeRequest, "Texture")!;
            var settings = settingsObject.GetType().GetProperties().Where(p => p.PropertyType.IsPrimitive || p.PropertyType.IsEnum || p.PropertyType == typeof(string))
                .ToDictionary(p => p.Name, p => p.GetValue(settingsObject)?.ToString() ?? "null");
            var progressCount = 0;
            Action<string> progress = message =>
            {
                var count = Interlocked.Increment(ref progressCount);
                if (count == 1 || count % 25 == 0) Console.Error.WriteLine("官方烘焙: " + message);
            };
            if (request.InspectOnly) contentPacks.GetMethod("MountLoadFolder")!.Invoke(null, [folder]);
            else
            {
                var baked = assetPacks.GetMethod("BakeForUpload")!.Invoke(null, [folder, progress, CancellationToken.None])!;
                if (!(bool)Get(baked, "Succeeded")!) throw new InvalidDataException("官方烘焙失败: " + JsonSerializer.Serialize(Get(baked, "Errors")));
            }
            var packPath = Path.Combine(folder, "Assets.zip");
            using var pack = (IDisposable)(assetPackType.GetMethod("TryOpen")!.Invoke(null, [packPath]) ?? throw new InvalidDataException("官方包无法打开"));
            var hashes = ((IEnumerable)Get(pack, "ContentHashes")!).Cast<object>().ToArray();
            if (hashes.Length == 0) throw new InvalidDataException("官方资源包没有有效 bake.manifest");
            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var hash in hashes)
            {
                var name = (string)Get(hash, "Key")!;
                var bytes = (byte[])assetPackType.GetMethod("Read")!.Invoke(pack, [name])!;
                // Read uses the official codec and verifies CRC, including deduplicated aliases.
                entries[name] = Convert.ToHexString(SHA256.HashData(bytes));
            }
            // Include official texture dimensions/format metadata and manifest, beyond source entries.
            foreach (var name in ((IEnumerable)Get(pack, "Names")!).Cast<string>())
                if (!entries.ContainsKey(name)) entries[name] = Convert.ToHexString(SHA256.HashData((byte[])assetPackType.GetMethod("Read")!.Invoke(pack, [name])!));
            var textures = Path.Combine(folder, "Textures");
            var pngs = Directory.Exists(textures) ? Directory.GetFiles(textures, "*.png", SearchOption.AllDirectories) : [];
            var sounds = ((IEnumerable)assetPacks.GetMethod("SoundFiles")!.Invoke(null, [folder, Array.Empty<string>()])!).Cast<object>().ToArray();
            var contentPackType = waylith.GetType("Waylith.Assets.ContentPack", true)!;
            var currentTexture = contentPackType.GetMethod("TryGetCurrentTextureEntry");
            foreach (var png in pngs)
            {
                // Current 1.7 libraries can emit .dds.split; use their texture lookup, since
                // the upload IsPackedSource predicate in some builds only recognizes .dds.
                var included = currentTexture is not null
                    ? (bool)currentTexture.Invoke(null, [png, false, null, null])!
                    : (bool)contentPacks.GetMethod("IsPackedSource")!.Invoke(null, [png])!;
                if (!included) throw new InvalidDataException("PNG 未进入官方包: " + png + "; entries=" + string.Join(',', entries.Keys));
            }
            if (sounds.Length > 0 && entries.Keys.Count(name => name.StartsWith("Sounds/", StringComparison.Ordinal)) < sounds.Length)
                throw new InvalidDataException("官方声音条目数少于输入声音数");
            packs.Add(new(Path.GetRelativePath(staging, folder), Path.GetRelativePath(staging, packPath), hashes.Length,
                pngs.Length, sounds.Length, settings, settings.GetValueOrDefault("EncodeOnGpu") == "True" ? "official_gpu" : "official_cpu", entries));
        }
        foreach (var path in DeploymentReceiptStore.Files(staging).ToArray())
            if ((bool)contentPacks.GetMethod("IsPackSidecar")!.Invoke(null, [path])!) File.Delete(path);
        var libraries = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location) && PathSafety.IsWithin(a.Location, game))
            .Select(a => a.Location).Concat(nativePaths).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => DeploymentReceiptStore.HashFile(path, path, "official_library")).ToArray();
        return new(true, packs.Count == 0 ? "not_required" : "verified", packs.ToArray(), libraries, []);
    }

    private static object? Get(object value, string member)
        => value.GetType().GetProperty(member)?.GetValue(value) ?? value.GetType().GetField(member)?.GetValue(value);
}
