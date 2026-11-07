using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed partial class ScenarioGameBackend
    {
        private void MarkCoverage(string capability, string defName)
        {
            foreach (var target in currentDefinition.Coverage.Where(t => t.CaseType == capability && t.DefName == defName))
                if (usedCoverage.Add(target.Key)) trace("coverage.target_used", target.Key);
        }

        internal void ObserveAbilityActivation(Ability ability)
        {
            if (acceptActivations && ability?.pawn != null && owned.Contains(ability.pawn)) MarkCoverage("SkillPocketMap", ability.def.defName);
        }

        private void MarkAttackCoverage(Thing attacker, Thing target, bool melee)
        {
            if (attacker is Building_TurretGun) MarkCoverage("TurretPocketMap", attacker.def.defName);
            if (attacker is Pawn pawn)
            {
                if (pawn.equipment?.Primary != null) MarkCoverage(melee ? "MeleeWeaponPocketMap" : "RangedWeaponPocketMap", pawn.equipment.Primary.def.defName);
                if (pawn.RaceProps.Animal) MarkCoverage("AnimalCombat", pawn.kindDef.defName);
                if (pawn.RaceProps.IsMechanoid) MarkCoverage("MechanoidCombat", pawn.kindDef.defName);
                if (pawn.RaceProps.Humanlike && pawn.def != ThingDefOf.Human) MarkCoverage("AlienCombat", pawn.kindDef.defName);
            }
            if (target is Pawn victim)
            {
                foreach (var apparel in victim.apparel?.WornApparel ?? Enumerable.Empty<Apparel>()) MarkCoverage("ApparelCombat", apparel.def.defName);
            }
        }

        public void ValidateCompletion()
        {
            var unused = currentDefinition.Coverage.Where(t => !usedCoverage.Contains(t.Key)).ToList();
            if (unused.Count > 0) throw new ScenarioConfigurationException(currentDefinition.Source + ": coverage target had no corresponding native execution/observation; ownership or a label alone cannot cover it: " + string.Join(",", unused.Select(t => t.Key)));
        }
    }

    [HarmonyPatch]
    internal static class ScenarioAbilityActivationPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => typeof(Ability).GetMethods().Where(m => m.Name == nameof(Ability.Activate) && m.ReturnType == typeof(bool));
        private static void Postfix(Ability __instance, bool __result) { if (__result) ScenarioGameBackend.AbilityActivation?.Invoke(__instance); }
    }
}
