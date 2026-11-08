using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace LogSyncMod.AutoTest
{
    // Patch the effective decision inputs, including per-kind rules. Never write
    // Camera+ settings: outside the dedicated battle map all inputs pass through.
    [HarmonyPatch]
    internal static class CameraPlusCombatCompatibility
    {
        private static readonly System.Type decision = AccessTools.TypeByName("CameraPlus.MarkerDecision");
        private static readonly System.Type cache = AccessTools.TypeByName("CameraPlus.MarkerDecisionCache");
        private static readonly MethodInfo getDecision = cache == null ? null : AccessTools.Method(cache, "Get", new[] { typeof(Pawn) });
        private static readonly FieldInfo edgeEnabled = decision == null ? null : AccessTools.Field(decision, "edgeEnabled");
        internal static int ThresholdReads { get; private set; }
        internal static int EdgeReads { get; private set; }
        internal static bool Active => GroupCombatCamera.Active && Find.CurrentMap == CombatFightService.Map;

        private static IEnumerable<MethodBase> TargetMethods() => decision == null
            ? Enumerable.Empty<MethodBase>()
            : decision.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name == "For" && m.GetParameters().Length == 3);

        private static int Threshold(int configured) => Active ? -2 : configured;
        private static bool Edge(bool configured) => !Active && configured;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode != OpCodes.Ldfld || !(instruction.operand is FieldInfo field)) continue;
                var owner = field.DeclaringType.FullName;
                if ((owner == "CameraPlus.CameraPlusSettings" && field.Name == "dotSize") ||
                    (owner == "CameraPlus.DotConfig" && field.Name == "showBelowPixels"))
                {
                    ThresholdReads++;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CameraPlusCombatCompatibility), nameof(Threshold)));
                }
                else if ((owner == "CameraPlus.CameraPlusSettings" && field.Name == "edgeIndicators") ||
                    (owner == "CameraPlus.DotConfig" && field.Name == "useEdge"))
                {
                    EdgeReads++;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CameraPlusCombatCompatibility), nameof(Edge)));
                }
            }
        }

        internal static void ClearCache() => (cache == null ? null : AccessTools.Method(cache, "Clear"))?.Invoke(null, null);

        internal static int EdgeMarkers(IEnumerable<Pawn> pawns) => getDecision == null || edgeEnabled == null ? 0
            : pawns.Count(p => (bool)edgeEnabled.GetValue(getDecision.Invoke(null, new object[] { p })));
    }
}
