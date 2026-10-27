using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LogSyncMod
{
    internal static class DebugNeedsPatchTargets
    {
        internal static IEnumerable<MethodBase> Methods(string name, params Type[] parameters)
        {
            // Include declared overrides: mod needs do not have to call the base implementation.
            foreach (Type type in GenTypes.AllTypes.Where(t => !t.ContainsGenericParameters && typeof(Need).IsAssignableFrom(t)))
            {
                MethodInfo method = type.GetMethod(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, parameters, null);
                if (method != null && !method.IsAbstract) yield return method;
            }
        }
    }

    [HarmonyPatch]
    internal static class DebugNeedsFrozenPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => DebugNeedsPatchTargets.Methods("get_IsFrozen");

        private static void Postfix(Need __instance, Pawn ___pawn, ref bool __result)
        {
            if (DebugPawnState.NeedsLocked(__instance, ___pawn)) __result = true;
        }
    }

    [HarmonyPatch]
    internal static class DebugNeedsReadPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => DebugNeedsPatchTargets.Methods("get_CurLevel");

        private static void Postfix(Need __instance, Pawn ___pawn, ref float ___curLevelInt, ref float __result)
        {
            if (!DebugPawnState.NeedsLocked(__instance, ___pawn)) return;
            ___curLevelInt = __result = __instance.MaxLevel;
        }
    }

    [HarmonyPatch]
    internal static class DebugNeedsWritePatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => DebugNeedsPatchTargets.Methods("set_CurLevel", typeof(float));

        private static void Prefix(Need __instance, Pawn ___pawn, ref float value)
        {
            if (DebugPawnState.NeedsLocked(__instance, ___pawn)) value = __instance.MaxLevel;
        }

        private static void Postfix(Need __instance, Pawn ___pawn, ref float ___curLevelInt)
        {
            if (DebugPawnState.NeedsLocked(__instance, ___pawn)) ___curLevelInt = __instance.MaxLevel;
        }
    }

    [HarmonyPatch]
    internal static class DebugNeedsIntervalPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => DebugNeedsPatchTargets.Methods(nameof(Need.NeedInterval));

        private static void Prefix(Need __instance, Pawn ___pawn)
        {
            if (DebugPawnState.NeedsLocked(__instance, ___pawn)) __instance.CurLevel = __instance.MaxLevel;
        }

        private static void Postfix(Need __instance, Pawn ___pawn) => Prefix(__instance, ___pawn);
    }
}
