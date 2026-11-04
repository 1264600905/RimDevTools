using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace RimDevTools;

internal sealed record WorkshopContentFile(string Path, long Bytes, string Sha256);
internal sealed record WorkshopUploadRequest(string NativeLibraryPath, string ProjectRoot, string ContentPath,
    bool CreatePrivate, bool UpdateDescription, int? Visibility, ulong? PublishedFileId, string Name, string Description, string PackageId,
    string[] Tags, int TimeoutSeconds, WorkshopContentFile[] Files);

public static class WorkshopUploadCommand
{
    private const string Prefix = "RIMDEVTOOLS_STEAMWORKS_UPLOAD=";

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        const string command = "workshop upload";
        if (!CliArgs.TryPositiveInt(args, "--timeout-seconds", 180, out var seconds, out var error) || seconds is < 10 or > 1800)
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "INVALID_ARGUMENT", error ?? "超时必须在 10 至 1800 秒之间");
        var create = CliArgs.Has(args, "--create-private");
        var visibilityText = CliArgs.Value(args, "--visibility")?.ToLowerInvariant();
        int? visibility = visibilityText switch { null => null, "public" => 0, "friends-only" => 1, "private" => 2, "unlisted" => 3, _ => -1 };
        if (visibility == -1 || create && visibility.HasValue && visibility != 2)
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "INVALID_ARGUMENT", "--visibility 支持 public、friends-only、private、unlisted；--create-private 只能使用 private");
        var rawId = CliArgs.Value(args, "--expected-published-file-id");
        if (create == (rawId is not null) || rawId is not null && (!ulong.TryParse(rawId, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedId) || parsedId is 0 or ulong.MaxValue))
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "INVALID_ARGUMENT", "必须二选一：--create-private 或有效的 --expected-published-file-id");
        var context = CliApplication.RequireProject(command, out var exitCode);
        if (context is null) return exitCode;
        if (!OperatingSystem.IsWindows() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return CliResult.WriteFailure(command, ExitCodes.MissingDependency, "STEAMWORKS_PLATFORM_UNSUPPORTED", "仅支持 Windows x64", context.Root);
        var installation = SteamInstallationResolver.Resolve(context.Config.RimWorldDir, context.Config.WorkshopPaths, expectedGameVersion: context.Config.GameVersion);
        if (installation.RimWorldDirectory is not { } gameDirectory)
            return CliResult.WriteFailure(command, ExitCodes.MissingDependency, "RIMWORLD_DIR_NOT_FOUND", "未找到目标版本游戏目录", context.Root);
        var configDirectory = GameEnvironment.ConfigDirectory(context);
        if (configDirectory is null)
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "CONFIG_INVALID", "无法解析游戏 Config 目录", context.Root);
        var modsDirectory = Path.Combine(gameDirectory, "Mods");
        var content = Path.GetFullPath(Path.Combine(modsDirectory, context.Config.ModName ?? ""));
        if (content.Equals(modsDirectory, StringComparison.OrdinalIgnoreCase) || !PathSafety.IsWithin(content, modsDirectory))
            return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "MOD_PATH_INVALID", "部署目标必须位于游戏 Mods 子目录", context.Root);

        WorkshopUploadRequest request;
        try
        {
            PathSafety.EnsureNoReparsePoints(gameDirectory, content);
            var sourceAbout = Path.Combine(context.Root, "About", "About.xml");
            var aboutPath = Path.Combine(content, "About", "About.xml");
            if (!File.ReadAllBytes(sourceAbout).SequenceEqual(File.ReadAllBytes(aboutPath)))
                throw new InvalidDataException("部署目录的 About.xml 与项目不一致，请先 deploy");
            var about = XDocument.Load(aboutPath).Root ?? throw new InvalidDataException("About.xml 无根元素");
            var name = about.Element("name")?.Value.Trim();
            var package = about.Element("packageId")?.Value.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(package)) throw new InvalidDataException("缺少 name 或 packageId");
            var versions = about.Element("supportedVersions")?.Elements("li").Select(v => v.Value.Trim()).ToArray() ?? [];
            if (versions.Length == 0 || versions.Any(v => !Version.TryParse(v, out _))) throw new InvalidDataException("supportedVersions 无效");
            var preview = new FileInfo(Path.Combine(content, "About", "Preview.png"));
            if (!preview.Exists || preview.Length is < 16 or >= 1048576) throw new InvalidDataException("Preview.png 必须存在且大小在 16 字节至 1 MiB 之间");
            var sourceId = ReadId(Path.Combine(context.Root, "About", "PublishedFileId.txt"));
            var deployedId = ReadId(Path.Combine(content, "About", "PublishedFileId.txt"));
            ulong? expected = rawId is null ? null : ulong.Parse(rawId, CultureInfo.InvariantCulture);
            if (create && (sourceId.HasValue || deployedId.HasValue)) throw new InvalidDataException("已存在 PublishedFileId，拒绝重复创建；请使用预期 ID 更新");
            if (!create && (sourceId != expected || deployedId != expected)) throw new InvalidDataException("项目与部署目录的 PublishedFileId 必须都等于预期 ID");
            var native = new[] { Path.Combine(gameDirectory, "steam_api64.dll"), Path.Combine(gameDirectory, "RimWorldWin64_Data", "Plugins", "x86_64", "steam_api64.dll") }.FirstOrDefault(File.Exists)
                ?? throw new FileNotFoundException("没有找到 steam_api64.dll");
            var description = about.Element("description")?.Value.Trim() ?? "";
            if (Encoding.UTF8.GetByteCount(description) >= 8000) throw new InvalidDataException("工坊描述必须少于 8000 个 UTF-8 字节");
            request = new(native, context.Root, content, create, create || CliArgs.Has(args, "--update-description"), create ? 2 : visibility,
                expected, name, description, package,
                new[] { "Mod" }.Concat(versions).Distinct().ToArray(), seconds, InspectContent(content));
        }
        catch (Exception ex) { return CliResult.WriteFailure(command, ExitCodes.InvalidArguments, "WORKSHOP_PREFLIGHT_FAILED", ex.Message, context.Root); }

        if (CliArgs.Has(args, "--dry-run"))
            return CliResult.WriteSuccess(command, context.Root, new { dryRun = true, request });
        if (!SteamSessionGuard.CheckForLaunch().IsRunning)
            return CliResult.WriteFailure(command, ExitCodes.MissingDependency, "STEAM_NOT_RUNNING", "请先登录 Steam 客户端", context.Root);
        var before = GameProcessIds();
        if (before.Length > 0)
            return CliResult.WriteFailure(command, ExitCodes.Busy, "RIMWORLD_RUNNING", "本次独立上传验证要求 RimWorld 已关闭", context.Root, new { before });
        using var gate = EnvironmentLock.TryAcquire(configDirectory, command, context.Root, Guid.NewGuid().ToString("N"), out var busy);
        if (gate is null) return CliResult.WriteFailure(command, ExitCodes.Busy, "ENVIRONMENT_BUSY", "游戏环境被其他 RimDevTools 操作占用", context.Root, busy);

        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            WorkingDirectory = gameDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8
        };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(typeof(CliApplication).Assembly.Location);
        start.ArgumentList.Add("--steamworks-upload-helper");
        start.Environment["SteamAppId"] = "294100";
        start.Environment["SteamGameId"] = "294100";
        using var helper = Process.Start(start) ?? throw new IOException("无法启动上传 helper");
        WorkshopUploadResult? latest = null;
        var output = Task.Run(async () =>
        {
            while (await helper.StandardOutput.ReadLineAsync() is { } line)
            {
                if (line.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    latest = JsonSerializer.Deserialize<WorkshopUploadResult>(line[Prefix.Length..], JsonDefaults.Storage);
                    Console.Error.WriteLine($"Workshop: {latest?.Stage}; id={latest?.PublishedFileId ?? "pending"}");
                }
                else if (CliArgs.Has(args, "--verbose")) Console.Error.WriteLine(line);
            }
        });
        var stderr = helper.StandardError.ReadToEndAsync();
        await helper.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, JsonDefaults.Storage));
        helper.StandardInput.Close();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds + 10));
        var interrupted = false;
        try { await helper.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            interrupted = true;
            if (!helper.HasExited) helper.Kill(true);
            await helper.WaitForExitAsync(CancellationToken.None);
        }
        await output;
        var nativeErrors = await stderr;
        if (CliArgs.Has(args, "--verbose") && !string.IsNullOrWhiteSpace(nativeErrors)) Console.Error.WriteLine(nativeErrors);
        var details = new { backend = "steamworks-standalone", contentPath = content, gameStarted = false, gameProcessIdsBefore = before, gameProcessIdsAfter = GameProcessIds(), helperExitCode = helper.ExitCode, upload = latest };
        if (interrupted || helper.ExitCode != 0 || latest is null || !latest.Succeeded)
            return CliResult.WriteFailure(command, interrupted ? ExitCodes.Timeout : ExitCodes.TaskFailed,
                interrupted ? "WORKSHOP_OUTCOME_UNCONFIRMED" : latest?.ErrorCode ?? "WORKSHOP_HELPER_FAILED",
                interrupted ? "helper 被中断；根据已返回的阶段核对远端状态，不得自动重试" : latest?.Message ?? "上传 helper 未返回成功结果", context.Root, details);
        return CliResult.WriteSuccess(command, context.Root, details);
    }

    internal static int RunHelper()
    {
        return CliResult.WriteFailure("workshop upload", ExitCodes.InvalidArguments, "WORKSHOP_LEGACY_ENTRY_DISABLED",
            "旧上传 helper 已停用；请使用带部署收据校验的 steamworkshops upload");
    }

    internal static ulong? ReadId(string path)
    {
        if (!File.Exists(path)) return null;
        if (!ulong.TryParse(File.ReadAllText(path).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id is 0 or ulong.MaxValue) throw new InvalidDataException("无效的 PublishedFileId: " + path);
        return id;
    }

    internal static WorkshopContentFile[] InspectContent(string root)
    {
        var files = new List<WorkshopContentFile>();
        var pending = new Stack<string>();
        pending.Push(root);
        var prohibited = new HashSet<string>([".git", ".tools", ".agents", ".claude", ".rimdev", "source", "bin", "obj", "TestResults"], StringComparer.OrdinalIgnoreCase);
        while (pending.TryPop(out var directory))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("拒绝上传重解析点: " + path);
                if (prohibited.Contains(Path.GetFileName(path))) throw new InvalidDataException("发布目录含开发文件: " + path);
                if (Directory.Exists(path)) { pending.Push(path); continue; }
                if (path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("发布目录含禁止的构建产物: " + path);
                using var stream = File.OpenRead(path);
                files.Add(new(Path.GetRelativePath(root, path), stream.Length, Convert.ToHexString(SHA256.HashData(stream))));
            }
        }
        return files.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
    }

    private static int[] GameProcessIds()
    {
        var processes = Process.GetProcessesByName("RimWorldWin64").Concat(Process.GetProcessesByName("RimWorld")).ToArray();
        try { return processes.Select(p => p.Id).Order().ToArray(); }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}
