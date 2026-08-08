namespace RimDevTools;

public sealed record RimWorldReferencePaths(string? RimWorldDirectory, string? ManagedDirectory, string? HarmonyDllPath, IReadOnlyList<string> RequiredGameAssemblies);
public sealed record RimWorldEnvironmentResolution(SteamInstallationResult Steam, ModInstallationIndex Mods, RimWorldReferencePaths References, ModAssemblyResolution? HarmonyResolution);

public static class RimWorldEnvironmentResolver
{
    public static readonly string[] RequiredManagedAssemblies =
    [
        "Assembly-CSharp.dll",
        "UnityEngine.CoreModule.dll",
        "UnityEngine.ImageConversionModule.dll",
        "UnityEngine.IMGUIModule.dll",
        "UnityEngine.TextRenderingModule.dll"
    ];

    public static RimWorldEnvironmentResolution Resolve(ProjectContext context, SteamResolverInput? steamInput = null)
    {
        var steam = SteamInstallationResolver.Resolve(context.Config.RimWorldDir, context.Config.WorkshopPaths, steamInput);
        var mods = ModInstallationIndex.Build(steam.RimWorldDirectory, steam.WorkshopPaths, context.Root);
        var harmony = mods.Find("brrainz.harmony");
        var harmonyResolution = harmony is null ? null : ModAssemblyResolver.Resolve(harmony.Directory, context.Config.GameVersion, "0Harmony.dll");
        var managed = steam.RimWorldDirectory is null ? null : Path.Combine(steam.RimWorldDirectory, "RimWorldWin64_Data", "Managed");
        var assemblies = managed is null ? [] : RequiredManagedAssemblies.Select(file => Path.Combine(managed, file)).ToArray();
        return new RimWorldEnvironmentResolution(steam, mods, new RimWorldReferencePaths(steam.RimWorldDirectory, managed, harmonyResolution?.AssemblyPath, assemblies), harmonyResolution);
    }
}
