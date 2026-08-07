using HarmonyLib;
using Verse;

namespace MyCustomMod.Rendering
{
    [StaticConstructorOnStartup]
    public static class ShaderBootstrap
    {
        static ShaderBootstrap()
        {
            var harmony = new Harmony("MyCustomMod.Shader");
            harmony.PatchAll();
            LogUtils.Message(nameof(ShaderBootstrap), "Shader bootstrap initialized.");
        }
    }
}
