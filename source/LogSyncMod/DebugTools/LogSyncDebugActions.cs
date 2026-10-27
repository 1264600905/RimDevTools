using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;

namespace LogSyncMod
{
    [DefOf]
    public static class LogSyncDebugDefOf
    {
        public static HediffDef LogSync_DebugNeedsLock;
        public static HediffDef LogSync_DebugSkilledWorker10x;

        static LogSyncDebugDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(LogSyncDebugDefOf));
        }
    }

    internal static class LogSyncDebugActions
    {
        [DebugActionYielder]
        private static IEnumerable<DebugActionNode> TestActions()
        {
            return new[]
            {
                CreateAction("LogSyncMod.DebugSuppliesTitle",
                    () => Find.WindowStack.Add(new Dialog_DebugSupplies(Find.CurrentMap))),
                CreateAction("LogSyncMod.DebugNeedsToggle",
                    () => StartPawnTool(false)),
                CreateAction("LogSyncMod.DebugWorkerToggle",
                    () => StartPawnTool(true))
            };
        }

        private static DebugActionNode CreateAction(string key, Action action)
        {
            return new DebugActionNode(key.Translate(), DebugActionType.Action, action)
            {
                category = "LogSyncMod",
                labelGetter = () => key.Translate(),
                sourceAttribute = new DebugActionAttribute("LogSyncMod") { allowedGameStates = AllowedGameStates.PlayingOnMap }
            };
        }

        private static void StartPawnTool(bool worker)
        {
            Map map = Find.CurrentMap;
            string label = (worker ? "LogSyncMod.DebugWorkerToggle" : "LogSyncMod.DebugNeedsToggle").Translate();
            DebugToolsGeneral.GenericRectTool(label, rect =>
            {
                if (!Prefs.DevMode || Find.CurrentMap != map || map == null) return;
                HediffDef def = worker ? LogSyncDebugDefOf.LogSync_DebugSkilledWorker10x : LogSyncDebugDefOf.LogSync_DebugNeedsLock;
                List<Pawn> targets = map.mapPawns.AllPawnsSpawned.Where(p => rect.Contains(p.Position)).ToList();
                int added = 0, removed = 0, skipped = 0, failed = 0;
                foreach (Pawn pawn in targets)
                {
                    if (pawn.Dead || pawn.Faction != Faction.OfPlayer || !pawn.RaceProps.Humanlike ||
                        pawn.health == null || (worker ? pawn.skills == null : pawn.needs == null || pawn.needs.AllNeeds.Count == 0))
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        List<Hediff> existing = pawn.health.hediffSet.hediffs.Where(h => h.def == def).ToList();
                        if (existing.Count > 0)
                        {
                            foreach (Hediff hediff in existing) pawn.health.RemoveHediff(hediff);
                        }
                        else
                        {
                            if (worker) pawn.workSettings?.EnableAndInitializeIfNotAlreadyInitialized();
                            pawn.health.AddHediff(def);
                            if (!pawn.health.hediffSet.HasHediff(def)) throw new InvalidOperationException("Test hediff was not added.");
                            if (!worker)
                            {
                                foreach (Need need in pawn.needs.AllNeeds.ToList()) need.CurLevelPercentage = 1f;
                            }
                        }
                        if (worker)
                        {
                            pawn.Notify_DisabledWorkTypesChanged();
                            pawn.workSettings?.Notify_UseWorkPrioritiesChanged();
                        }
                        foreach (StatDef stat in DefDatabase<StatDef>.AllDefsListForReading)
                            stat.Worker.ClearCacheForThing(pawn);
                        if (existing.Count > 0) removed++;
                        else added++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Log.Warning("[LogSyncMod] Debug pawn state failed for " + pawn.LabelShort + ": " + ex);
                    }
                }

                string text = "LogSyncMod.DebugPawnResult".Translate(label, added, removed, skipped, failed);
                Messages.Message(text, MessageTypeDefOf.NeutralEvent, historical: false);
            });
        }
    }

    [HarmonyPatch(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.GetPriority))]
    internal static class DebugWorkerAllWorkPatch
    {
        private static void Postfix(Pawn ___pawn, ref int __result)
        {
            if (__result <= 0 && DebugPawnState.WorkerLocked(___pawn))
                __result = Pawn_WorkSettings.DefaultPriority;
        }
    }

    [HarmonyPatch]
    internal static class DebugWorkerSchedulePatch
    {
        private static MethodBase[] TargetMethods() => new MethodBase[]
        {
            AccessTools.Method(typeof(Pawn_TimetableTracker), nameof(Pawn_TimetableTracker.GetAssignment)),
            AccessTools.PropertyGetter(typeof(Pawn_TimetableTracker), nameof(Pawn_TimetableTracker.CurrentAssignment))
        };

        private static void Postfix(Pawn ___pawn, ref TimeAssignmentDef __result)
        {
            if (DebugPawnState.WorkerLocked(___pawn)) __result = TimeAssignmentDefOf.Work;
        }
    }

    internal static class DebugPawnState
    {
        internal static bool WorkerLocked(Pawn pawn)
        {
            return pawn?.health?.hediffSet != null && LogSyncDebugDefOf.LogSync_DebugSkilledWorker10x != null &&
                pawn.health.hediffSet.HasHediff(LogSyncDebugDefOf.LogSync_DebugSkilledWorker10x);
        }

        internal static bool NeedsLocked(Need need, Pawn pawn)
        {
            return need?.def != null && pawn?.health?.hediffSet != null &&
                LogSyncDebugDefOf.LogSync_DebugNeedsLock != null && pawn.health.hediffSet.HasHediff(LogSyncDebugDefOf.LogSync_DebugNeedsLock);
        }
    }

    [HarmonyPatch(typeof(Pawn), "get_CombinedDisabledWorkTags")]
    internal static class DebugWorkerDisabledTagsPatch
    {
        private static bool Prefix(Pawn __instance, ref WorkTags __result)
        {
            if (!DebugPawnState.WorkerLocked(__instance)) return true;
            __result = WorkTags.None;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetDisabledWorkTypes))]
    internal static class DebugWorkerDisabledTypesPatch
    {
        private static bool Prefix(Pawn __instance, ref List<WorkTypeDef> __result)
        {
            if (!DebugPawnState.WorkerLocked(__instance)) return true;
            // Do not clear the original list: it is cached by Pawn and must remain intact for removal.
            __result = new List<WorkTypeDef>();
            return false;
        }
    }

    [HarmonyPatch(typeof(SkillRecord), "get_TotallyDisabled")]
    internal static class DebugWorkerSkillDisabledPatch
    {
        private static bool Prefix(SkillRecord __instance, ref bool __result)
        {
            if (!DebugPawnState.WorkerLocked(__instance.Pawn)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(SkillRecord), "get_PermanentlyDisabled")]
    internal static class DebugWorkerSkillPermanentlyDisabledPatch
    {
        private static bool Prefix(SkillRecord __instance, ref bool __result)
        {
            if (!DebugPawnState.WorkerLocked(__instance.Pawn)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.GetLevel))]
    internal static class DebugSkillLevelPatch
    {
        private static bool Prefix(SkillRecord __instance, ref int __result)
        {
            if (!DebugPawnState.WorkerLocked(__instance.Pawn)) return true;
            __result = 20;
            return false;
        }
    }

    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.GetLevelForUI))]
    internal static class DebugSkillUiPatch
    {
        private static bool Prefix(SkillRecord __instance, ref int __result)
        {
            if (!DebugPawnState.WorkerLocked(__instance.Pawn)) return true;
            __result = 20;
            return false;
        }
    }

    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.GetUnclampedLevel))]
    internal static class DebugSkillUnclampedPatch
    {
        private static bool Prefix(SkillRecord __instance, ref int __result)
        {
            if (!DebugPawnState.WorkerLocked(__instance.Pawn)) return true;
            __result = 20;
            return false;
        }
    }

    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Learn))]
    internal static class DebugSkillLearningPatch
    {
        private static bool Prefix(SkillRecord __instance) => !DebugPawnState.WorkerLocked(__instance.Pawn);
    }
}
