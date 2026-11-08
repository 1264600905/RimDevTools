using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using Verse;

namespace LogSyncMod.AutoTest
{
    // Observation only. Native think trees create and schedule every combat job.
    internal static class GroupCombatAiDiagnostics
    {
        private static readonly HashSet<string> observedJobs = new();
        private static readonly HashSet<Pawn> shooters = new();
        internal static void Reset() { observedJobs.Clear(); shooters.Clear(); }

        internal static void Observe()
        {
            foreach (var pair in CombatFightService.Pawns)
            {
                var pawn = pair.Value;
                if (!CombatFightService.IsCombatParticipant(pawn) || pawn.Dead || pawn.Downed || pawn.CurJob == null) continue;
                var job = pawn.CurJob;
                if (observedJobs.Count < 2000 && observedJobs.Add(pair.Key + "/" + job.def.defName))
                    CombatFightService.TraceAi("group.ai_native_job", "pawn=" + pair.Key + "; tick=" + CombatFightService.Display.ElapsedTicks
                        + "; job=" + job.def.defName + "; duty=" + pawn.mindState?.duty?.def.defName + "; target=" + job.targetA);
            }
        }

        internal static void NotifyShot(Verb verb)
        {
            var pawn = verb.CasterPawn;
            if (!CombatFightService.IsCombatParticipant(pawn) || !shooters.Add(pawn)) return;
            var target = verb.CurrentTarget.Thing as Pawn;
            CombatFightService.TraceAi("group.ai_shot", "pawn=" + CombatFightService.RosterId(pawn)
                + "; target=" + CombatFightService.RosterId(target) + "; tick=" + CombatFightService.Display.ElapsedTicks
                + "; distance=" + (target == null ? "none" : pawn.Position.DistanceTo(target.Position).ToString("F2", CultureInfo.InvariantCulture))
                + "; weapon=" + pawn.equipment?.Primary?.def.defName + "; verb=" + verb.GetType().Name
                + "; range=" + verb.EffectiveRange.ToString("F2", CultureInfo.InvariantCulture) + "; launched=true; controller=native");
        }
    }

    [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
    internal static class GroupCombat_Shot_Patch
    {
        private static void Postfix(Verb_LaunchProjectile __instance, bool __result)
        {
            if (__result) GroupCombatAiDiagnostics.NotifyShot(__instance);
        }
    }

    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.Spawn), new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool), typeof(bool) })]
    internal static class GroupCombat_InvalidSpawnDiagnostic_Patch
    {
        private static void Prefix(Thing newThing, Map map)
        {
            if (CombatFightService.Busy && map == CombatFightService.Map && newThing?.Destroyed == true)
                CombatFightService.DamageDiagnostic("group.invalid_spawn: " + newThing + "; " + Environment.StackTrace);
        }
    }
}
