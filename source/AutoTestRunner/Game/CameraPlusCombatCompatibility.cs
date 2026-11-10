using System;
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
        private static Type decision, cache;
        private static MethodInfo getDecision;
        private static FieldInfo edgeEnabled;
        private static MethodBase[] targets = Array.Empty<MethodBase>();
        private static bool resolved;
        private static bool disabled;
        private static bool warned;
        internal static int ThresholdReads { get; private set; }
        internal static int EdgeReads { get; private set; }
        internal static bool Active => !disabled && GroupCombatCamera.Active && Find.CurrentMap == CombatFightService.Map;

        // An empty TargetMethods sequence is not enough: Harmony may fall back to
        // a single undefined target. Skip the optional patch before target resolution.
        [HarmonyPrepare]
        private static bool Prepare()
        {
            if (disabled) return false;
            if (!resolved)
            {
                // Resolve optional APIs inside the guard, not in a static field initializer.
                // A changed third-party type/signature must not poison this class either.
                try
                {
                    decision = AccessTools.TypeByName("CameraPlus.MarkerDecision");
                    cache = AccessTools.TypeByName("CameraPlus.MarkerDecisionCache");
                    getDecision = cache == null ? null : AccessTools.Method(cache, "Get", new[] { typeof(Pawn) });
                    edgeEnabled = decision == null ? null : AccessTools.Field(decision, "edgeEnabled");
                    targets = decision == null ? Array.Empty<MethodBase>() : decision
                        .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                        .Where(m => m.Name == "For" && m.GetParameters().Length == 3).Cast<MethodBase>().ToArray();
                    resolved = true;
                }
                catch (Exception ex) { Disable("解析相机接口失败：" + ex.GetBaseException().Message); return false; }
            }
            if (targets.Length != 0) return true;
            Disable(decision == null ? "未加载 Camera+" : "当前 Camera+ 版本没有匹配的 MarkerDecision.For 方法");
            return false;
        }

        private static IEnumerable<MethodBase> TargetMethods() => targets;

        [HarmonyCleanup]
        private static Exception Cleanup(Exception __exception)
        {
            if (__exception != null) Disable("补丁注册失败：" + __exception.GetBaseException().Message);
            // This compatibility layer is optional. Never poison AutoTestBootstrap's
            // static constructor or the game's Update/OnGUI callbacks when it fails.
            return null;
        }

        private static void Disable(string reason)
        {
            disabled = true;
            if (warned) return;
            warned = true;
            Log.Warning("[LogSyncMod] 已跳过 Camera+ 战斗相机兼容：" + reason + "。继续加载游戏，保留相机原有行为。");
        }

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
                    if (field.FieldType != typeof(int)) throw new InvalidOperationException("Camera+ 字段类型已变化：" + owner + "." + field.Name);
                    ThresholdReads++;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CameraPlusCombatCompatibility), nameof(Threshold)));
                }
                else if ((owner == "CameraPlus.CameraPlusSettings" && field.Name == "edgeIndicators") ||
                    (owner == "CameraPlus.DotConfig" && field.Name == "useEdge"))
                {
                    if (field.FieldType != typeof(bool)) throw new InvalidOperationException("Camera+ 字段类型已变化：" + owner + "." + field.Name);
                    EdgeReads++;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CameraPlusCombatCompatibility), nameof(Edge)));
                }
            }
        }

        internal static void ClearCache()
        {
            if (disabled) return;
            try { (cache == null ? null : AccessTools.Method(cache, "Clear"))?.Invoke(null, null); }
            catch (Exception ex) { Disable("清理相机缓存失败：" + ex.GetBaseException().Message); }
        }

        internal static int EdgeMarkers(IEnumerable<Pawn> pawns)
        {
            if (disabled || getDecision == null || edgeEnabled == null) return 0;
            try { return pawns.Count(p => (bool)edgeEnabled.GetValue(getDecision.Invoke(null, new object[] { p }))); }
            catch (Exception ex) { Disable("读取相机标记失败：" + ex.GetBaseException().Message); return 0; }
        }
    }
}
