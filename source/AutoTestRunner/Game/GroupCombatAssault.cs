using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace LogSyncMod.AutoTest
{
    // Assign the native assault duty without replacing a pawn's race/mod think trees.
    // A single toil also avoids the colony raid's non-hostile-to-player exit transition.
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

    internal sealed class LordToil_GroupCombatAssault : LordToil_AssaultColony
    {
        public override bool AllowSelfTend => false;
    }

    [HarmonyPatch(typeof(PawnNameColorUtility), nameof(PawnNameColorUtility.PawnNameColorOf))]
    internal static class GroupCombat_NameColor_Patch
    {
        private static void Postfix(Pawn pawn, ref Color __result)
        {
            if (GroupCombatService.Busy && GroupCombatService.RosterId(pawn) != null)
                __result = GroupCombatOverlay.NameColor(pawn.Dead ? "dead" : pawn.Downed ? "downed" : "standing");
        }
    }
}
