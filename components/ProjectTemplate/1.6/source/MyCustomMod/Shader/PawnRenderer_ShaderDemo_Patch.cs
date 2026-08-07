using HarmonyLib;
using Verse;

namespace MyCustomMod.Rendering
{
    [HarmonyPatch(typeof(PawnRenderer), "RenderPawnAt")]
    public static class PawnRenderer_ShaderDemo_Patch
    {
        private static bool loggedPatchEntered;

        [HarmonyPostfix]
        public static void Postfix(Pawn ___pawn)
        {
            if (!loggedPatchEntered && ___pawn != null)
            {
                loggedPatchEntered = true;
                LogUtils.Message(nameof(PawnRenderer_ShaderDemo_Patch), $"RenderPawnAt patch 已触发，pawn={___pawn.LabelShortCap}");
            }

            MinimalShaderDemoDrawer.DrawOnPawn(___pawn);
        }
    }
}
