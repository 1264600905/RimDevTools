namespace RimDevTools;

public sealed record TestHostResolution(
    ModInstallation? LogSyncMod,
    string? LogSyncAssemblyPath,
    string? AutoTestAssemblyPath,
    string? LogSyncMessage,
    string? AutoTestMessage)
{
    public CliError? Validate(string mode)
    {
        if (LogSyncAssemblyPath is null)
            return new CliError("LOGSYNC_TEST_HOST_UNAVAILABLE", LogSyncMessage ?? "缺少所选 RimWorld 版本对应的 LogSync 测试宿主");
        if ((mode is "auto" or "xml") && AutoTestAssemblyPath is null)
            return new CliError("AUTOTEST_RUNNER_UNAVAILABLE", AutoTestMessage ?? "缺少所选 RimWorld 版本对应的 AutoTestRunner 程序集");
        return null;
    }
}

public static class TestHostResolver
{
    public static TestHostResolution Resolve(RimWorldEnvironmentResolution environment, string? gameVersion)
    {
        var logSync = environment.Mods.Find("trigger.logsyncmod");
        if (logSync is null)
            return new TestHostResolution(null, null, null, "测试宿主 trigger.logsyncmod 未安装；普通项目检查不要求它", "AutoTestRunner 只用于显式运行 auto/XML 测试");
        if (!RimWorldGameLayout.TryGet(gameVersion, out var layout))
            return new TestHostResolution(logSync, null, null, $"game_version={gameVersion ?? "未配置"} 不支持测试宿主解析", "game_version 未支持");

        var logSyncResolution = ModAssemblyResolver.Resolve(logSync.Directory, gameVersion, "LogSyncMod.dll");
        if (logSyncResolution.AssemblyPath is null)
            return new TestHostResolution(logSync, null, null, $"LogSync 没有 RimWorld {gameVersion} 对应程序集；不会回退使用其他版本", $"LogSync 没有 RimWorld {gameVersion} 对应的 AutoTestRunner");

        var autoTestResolution = ModAssemblyResolver.Resolve(logSync.Directory, gameVersion, "AutoTestRunner.dll");
        return new TestHostResolution(
            logSync,
            logSyncResolution.AssemblyPath,
            autoTestResolution.AssemblyPath,
            null,
            autoTestResolution.AssemblyPath is null ? $"LogSync 没有 RimWorld {gameVersion} 对应的 AutoTestRunner 程序集" : null);
    }
}
