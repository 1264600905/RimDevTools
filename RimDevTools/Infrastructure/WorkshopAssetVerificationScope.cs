using System.Text.Json;

namespace RimDevTools;

internal sealed record WorkshopAssetTestFile(string RelativePath, byte[] Bytes, string Sha256);
internal sealed record WorkshopAssetTestTransaction(int SchemaVersion, string ProjectRoot, string TargetRoot, string RunId, WorkshopAssetTestFile[] Files);

internal sealed class WorkshopAssetVerificationScope(ProjectContext context, string target)
{
    internal const string TransactionName = "workshop-asset-test-transaction.json";
    internal const string RequestName = "workshop-asset-verification-request.json";
    internal static readonly string[] SamplePngs =
    [
        "1.7/Textures/WorkshopTestAssets/ShezhangOC/CEO/Weapon/PresidentSpear.png",
        "1.7/Textures/WorkshopTestAssets/ShezhangOC/CEO/Icon/President.png",
        "1.7/Textures/WorkshopTestAssets/ShezhangOC/CEO/Effects/ReflectShield/ForceField.png",
        "1.7/Textures/WorkshopTestAssets/ShezhangOC/Wolfein/Heads/WolfeinHead11_south.png"
    ];
    internal const string Tone = "1.7/Sounds/WorkshopTestAssets/CowGunTone.wav";

    internal string RequestPath => ProjectDataPaths.File(context.Root, RequestName);

    internal static async Task<WorkshopAssetVerificationScope> BeginAsync(ProjectContext context, string runId, CancellationToken token)
    {
        if (context.Config.GameVersion != "1.7") throw new InvalidDataException("工坊资源读取验证仅支持 1.7");
        var verified = await DeploymentVerifier.VerifyAsync(context, token);
        if (!verified.Valid || !verified.PublishReady) throw new InvalidDataException("资源读取测试需要成功的普通部署和有效资源包收据");
        RestorePending(context, verified.Target);
        var scope = new WorkshopAssetVerificationScope(context, verified.Target);
        var files = SamplePngs.Append(Tone).Select(relative =>
        {
            var path = Path.Combine(verified.Target, relative);
            PathSafety.EnsureNoReparsePoints(verified.Target, path);
            var bytes = File.ReadAllBytes(path);
            return new WorkshopAssetTestFile(relative, bytes, ConfigTransaction.Hash(bytes));
        }).ToArray();
        AtomicFile.WriteAllTextUsingMoveReplacement(ProjectDataPaths.File(context.Root, TransactionName), JsonSerializer.Serialize(
            new WorkshopAssetTestTransaction(1, context.Root, verified.Target, runId, files), JsonDefaults.Storage));
        AtomicFile.WriteAllTextUsingMoveReplacement(scope.RequestPath, JsonSerializer.Serialize(new
        {
            runId, projectRoot = context.Root, targetRoot = verified.Target, packageId = context.Config.CurrentPackageId(context.Root),
            sourceFilesMustBeAbsent = files.Select(f => f.RelativePath).ToArray(),
            expectedWidths = new[] { 1024, 262, 512, 384 }, expectedHeights = new[] { 1024, 262, 512, 384 }
        }, JsonDefaults.Storage));
        foreach (var file in files) File.Delete(Path.Combine(verified.Target, file.RelativePath));
        return scope;
    }

    internal void Restore() => RestorePending(context, target);

    internal static void RestorePending(ProjectContext context, string expectedTarget)
    {
        var path = ProjectDataPaths.File(context.Root, TransactionName);
        if (!File.Exists(path)) return;
        var transaction = JsonSerializer.Deserialize<WorkshopAssetTestTransaction>(File.ReadAllText(path), JsonDefaults.Storage)
            ?? throw new InvalidDataException("资源验证事务为空");
        var allowed = SamplePngs.Append(Tone).ToHashSet(StringComparer.Ordinal);
        if (transaction.SchemaVersion != 1 || !transaction.ProjectRoot.Equals(context.Root, StringComparison.OrdinalIgnoreCase)
            || !transaction.TargetRoot.Equals(expectedTarget, StringComparison.OrdinalIgnoreCase)
            || transaction.Files.Length != 5 || !transaction.Files.Select(f => f.RelativePath).ToHashSet(StringComparer.Ordinal).SetEquals(allowed))
            throw new InvalidDataException("资源验证事务路径或文件白名单不匹配");
        foreach (var file in transaction.Files)
        {
            var full = Path.Combine(expectedTarget, file.RelativePath);
            PathSafety.EnsureNoReparsePoints(expectedTarget, full);
            if (ConfigTransaction.Hash(file.Bytes) != file.Sha256) throw new InvalidDataException("资源验证事务原文件损坏");
            if (File.Exists(full) && ConfigTransaction.Hash(File.ReadAllBytes(full)) != file.Sha256)
                throw new InvalidDataException("资源验证期间文件被其他程序修改，保留事务: " + full);
        }
        foreach (var file in transaction.Files)
            if (!File.Exists(Path.Combine(expectedTarget, file.RelativePath))) AtomicFile.WriteAllBytes(Path.Combine(expectedTarget, file.RelativePath), file.Bytes);
        File.Delete(path);
        var request = ProjectDataPaths.File(context.Root, RequestName);
        if (File.Exists(request)) File.Delete(request);
    }

    internal static async Task<bool> WaitForSignalAsync(string logPath, string runId, DateTimeOffset deadline, CancellationToken token)
    {
        var marker = "WORKSHOP_ASSETS_VERIFIED runId=" + runId;
        while (DateTimeOffset.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            if (File.Exists(logPath))
            {
                using var stream = File.Open(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                var text = await reader.ReadToEndAsync(token);
                if (text.Contains(marker, StringComparison.Ordinal)) return true;
                if (text.Contains("WORKSHOP_ASSETS_FAILED runId=" + runId, StringComparison.Ordinal)) return false;
            }
            await Task.Delay(100, token);
        }
        return false;
    }
}
