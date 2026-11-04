using System.Reflection;
using System.Runtime.Loader;

namespace RimDevTools;

internal static class OfficialAssetLibraryProbe
{
    // Intentionally only loads and inspects signatures in batch 1. It never claims baking succeeded.
    internal static object Inspect(string game)
    {
        var loaded = new Dictionary<string, object>();
        var context = new AssemblyLoadContext("OfficialAssetLibraryProbe", isCollectible: true);
        context.Resolving += (_, name) =>
        {
            var path = Path.Combine(game, name.Name + ".dll");
            return File.Exists(path) ? Load(path) : null;
        };
        try
        {
            var wrapper = Load(Path.Combine(game, "UnityStubs.dll"));
            var core = Load(Path.Combine(game, "Waylith.AssetBake.dll"));
            var wrapperType = wrapper.GetType("UnityStubs.ModAssetPacks", throwOnError: true)!;
            var bake = wrapperType.GetMethod("BakeForUpload", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException("ModAssetPacks.BakeForUpload");
            var coreType = core.GetType("Waylith.AssetBake.ContentPackBaker", throwOnError: true)!;
            var entry = coreType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance).Where(m => m.Name == "Bake").ToArray();
            if (entry.Length == 0) throw new MissingMethodException("ContentPackBaker.Bake");
            return new { staticLoadSucceeded = true, bakeExecuted = false, wrapperEntry = bake.ToString(),
                coreEntries = entry.Select(m => m.ToString()).ToArray(), loadedLibraries = loaded.Values.ToArray(),
                note = "仅验证加载与公开入口；原生编码、最小初始化和全部资源烘焙必须在第二批实测" };
        }
        finally { context.Unload(); }

        Assembly Load(string path)
        {
            var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(path));
            loaded[path] = new { path, version = assembly.GetName().Version?.ToString(), sha256 = DeploymentReceiptStore.HashFile(path, path).Sha256,
                references = assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray() };
            return assembly;
        }
    }
}
