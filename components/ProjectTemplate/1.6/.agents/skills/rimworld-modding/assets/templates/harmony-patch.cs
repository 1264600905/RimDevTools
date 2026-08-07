using HarmonyLib;
using System.Reflection;
using Verse;

namespace TemplateMod
{
    [StaticConstructorOnStartup]
    public static class HarmonyBootstrap
    {
        static HarmonyBootstrap()
        {
            var harmony = new Harmony("publisher.templatemod");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
    }

    // Verify the canonical target and signature with DecompilerServer, then add
    // the appropriate HarmonyPatch attribute and Prefix/Postfix implementation.
    public static class PatchTemplate
    {
        public static bool Prefix()
        {
            return true;
        }
    }
}
