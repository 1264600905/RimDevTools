namespace RimDevTools;

public sealed record RimWorldReferencePaths(
    string? RimWorldDirectory,
    string? ExecutablePath,
    string? AssemblyDirectory,
    string? HarmonyDllPath,
    IReadOnlyList<string> RequiredGameAssemblies,
    string? GameVersion);
public sealed record RimWorldEnvironmentResolution(SteamInstallationResult Steam, ModInstallationIndex Mods, RimWorldReferencePaths References, ModAssemblyResolution? HarmonyResolution);

public static class RimWorldEnvironmentResolver
{
    public static readonly IReadOnlyList<string> RequiredManagedAssemblies = RimWorldGameLayout.LegacyRequiredAssemblies;

    public static RimWorldEnvironmentResolution Resolve(ProjectContext context, SteamResolverInput? steamInput = null)
    {
        var hasConfiguredVersion = RimWorldGameLayout.TryGet(context.Config.GameVersion, out var layout);
        var expectedVersion = hasConfiguredVersion ? context.Config.GameVersion : "unsupported";
        var steam = SteamInstallationResolver.Resolve(context.Config.RimWorldDir, context.Config.WorkshopPaths, steamInput, expectedVersion);
        var mods = ModInstallationIndex.Build(steam.RimWorldDirectory, steam.WorkshopPaths, context.Root);
        var harmony = mods.Find("brrainz.harmony");
        var harmonyResolution = harmony is null || !hasConfiguredVersion
            ? null
            : ModAssemblyResolver.Resolve(harmony.Directory, context.Config.GameVersion, "0Harmony.dll");
        var assemblyDirectory = steam.RimWorldDirectory is null || !hasConfiguredVersion
            ? null
            : layout.AssemblyDirectory(steam.RimWorldDirectory);
        var executablePath = steam.RimWorldDirectory is null || !hasConfiguredVersion
            ? null
            : layout.ExecutablePath(steam.RimWorldDirectory);
        var assemblies = assemblyDirectory is null
            ? []
            : layout.RequiredAssemblies.Select(file => Path.Combine(assemblyDirectory, file)).ToArray();
        return new RimWorldEnvironmentResolution(
            steam,
            mods,
            new RimWorldReferencePaths(steam.RimWorldDirectory, executablePath, assemblyDirectory, harmonyResolution?.AssemblyPath, assemblies, hasConfiguredVersion ? context.Config.GameVersion : null),
            harmonyResolution);
    }
}
