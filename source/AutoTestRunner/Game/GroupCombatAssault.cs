using HarmonyLib;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace LogSyncMod.AutoTest
{
    // Use the original immediate-assault graph and AssaultColony duty. Only
    // departures are removed; targeting, positioning and pawn jobs stay native.
    internal sealed class LordJob_GroupCombatAssault : LordJob_AssaultColony
    {
        public LordJob_GroupCombatAssault() : this(null) { }
        public LordJob_GroupCombatAssault(Faction faction)
            : base(faction, canKidnap: false, canTimeoutOrFlee: false, canSteal: false) { }

        public override bool AddFleeToil => false;
        public override StateGraph CreateGraph()
        {
            var graph = base.CreateGraph();
            // The native graph also has exits for diplomacy/game-ending signals.
            // A benchmark must keep fighting its opponent until its end rule fires.
            graph.transitions.RemoveAll(t => t.target is LordToil_ExitMap);
            graph.lordToils.RemoveAll(t => t is LordToil_ExitMap);
            return graph;
        }
    }

    [HarmonyPatch(typeof(LordToil), nameof(LordToil.AllowSelfTend), MethodType.Getter)]
    internal static class GroupCombat_NoSelfTend_Patch
    {
        private static void Postfix(LordToil __instance, ref bool __result)
        {
            if (CombatFightService.Busy && __instance.lord?.LordJob is LordJob_GroupCombatAssault
                && __instance.lord.Map == CombatFightService.Map) __result = false;
        }
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
