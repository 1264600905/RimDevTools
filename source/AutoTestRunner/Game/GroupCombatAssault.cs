using HarmonyLib;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace LogSyncMod.AutoTest
{
    // The race/mod think trees stay intact; their duty hook runs our combat decisions.
    internal sealed class LordJob_GroupCombatAssault : LordJob
    {
        public override bool AddFleeToil => false;
        public override StateGraph CreateGraph()
        {
            var graph = new StateGraph();
            graph.AddToil(new LordToil_GroupCombatAssault());
            return graph;
        }
    }

    internal sealed class LordToil_GroupCombatAssault : LordToil
    {
        public override bool AllowSelfTend => false;
        public override bool AllowSatisfyLongNeeds => false;
        public override bool ForceHighStoryDanger => true;

        public override void UpdateAllDuties()
        {
            var duty = DefDatabase<DutyDef>.GetNamed("LogSync_TestCombat");
            foreach (var pawn in lord.ownedPawns)
            {
                if (pawn.mindState.duty?.def == duty) continue;
                pawn.mindState.duty = new PawnDuty(duty);
                pawn.TryGetComp<CompCanBeDormant>()?.WakeUp();
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
        }
    }

    public sealed class JobGiver_GroupCombatAttack : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn) => GroupCombatTactics.CreateJob(pawn);
    }

    [HarmonyPatch(typeof(PawnNameColorUtility), nameof(PawnNameColorUtility.PawnNameColorOf))]
    internal static class GroupCombat_NameColor_Patch
    {
        private static void Postfix(Pawn pawn, ref Color __result)
        {
            if (CombatFightService.Busy && CombatFightService.RosterId(pawn) != null)
                __result = GroupCombatOverlay.NameColor(pawn.Dead ? "dead" : pawn.Downed ? "downed" : "standing");
        }
    }

    // Replace only the map overlay's label call. Colonist bars and other UI
    // also call GenMapUI.DrawPawnLabel and must keep their names.
    [HarmonyPatch(typeof(PawnUIOverlay), nameof(PawnUIOverlay.DrawPawnGUIOverlay))]
    internal static class GroupCombat_MapName_Patch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => Transpilers.MethodReplacer(instructions,
                AccessTools.Method(typeof(GenMapUI), nameof(GenMapUI.DrawPawnLabel), new[] {
                    typeof(Pawn), typeof(Vector2), typeof(float), typeof(float),
                    typeof(Dictionary<string, string>), typeof(GameFont), typeof(bool), typeof(bool) }),
                AccessTools.Method(typeof(GroupCombat_MapName_Patch), nameof(DrawMapLabel)));

        private static void DrawMapLabel(Pawn pawn, Vector2 pos, float alpha, float truncateToWidth,
            Dictionary<string, string> truncatedLabelsCache, GameFont font, bool alwaysDrawBg, bool alignCenter)
        {
            if (CombatFightService.Busy && pawn.MapHeld == CombatFightService.Map && CombatFightService.RosterId(pawn) != null) return;
            GenMapUI.DrawPawnLabel(pawn, pos, alpha, truncateToWidth, truncatedLabelsCache, font, alwaysDrawBg, alignCenter);
        }
    }
}
