using System;
using System.Linq;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed partial class ScenarioGameBackend
    {
        private void ValidateCapability(ScenarioCase definition, XElement c)
        {
            var type = definition.CaseType;
            var steps = c.Element("Steps").Elements().ToList();
            var attacks = steps.Where(e => e.Name == "Attack").ToList();
            bool PawnAttack(Func<Pawn, bool> predicate) => attacks.Any(a => One(A(a, "actor")) is Pawn p && predicate(p));
            bool ok = type switch {
                "ExternalScenario" => true,
                "TurretPocketMap" => attacks.Any(a => One(A(a, "actor")) is Building_TurretGun),
                "RangedWeaponPocketMap" => PawnAttack(p => p.equipment?.Primary?.def.IsRangedWeapon == true),
                "MeleeWeaponPocketMap" => attacks.Any(a => A(a, "melee", "false") == "true" && One(A(a, "actor")) is Pawn p && p.equipment?.Primary?.def.IsMeleeWeapon == true),
                "ApparelCombat" => attacks.Any(a => One(A(a, "target")) is Pawn p && p.apparel?.WornApparel.Count > 0),
                "AnimalCombat" => PawnAttack(p => p.RaceProps.Animal),
                "MechanoidCombat" => PawnAttack(p => p.RaceProps.IsMechanoid),
                "AlienCombat" => PawnAttack(p => p.RaceProps.Humanlike && p.def != ThingDefOf.Human),
                "SkillPocketMap" => steps.Any(e => e.Name == "CastAbility") || steps.Any(e => e.Name == "Attack" || e.Name == "ApplyDamage") && c.Descendants("HasHediff").Any(),
                "RecipeExecution" => steps.Any(e => e.Name == "ExecuteRecipe"),
                "HediffEffect" => c.Descendants("HasHediff").Any(),
                "CorpseLifecycle" => actors.Values.SelectMany(v => v).Any(t => t is Corpse) || steps.Any(e => e.Name == "Kill"),
                "ThingBehavior" => steps.Any(e => new[] { "CreateThing", "Destroy", "TransferThing", "DropInventory", "Equip", "DropEquipment" }.Contains(e.Name.LocalName)),
                _ when ScenarioViews.Types.Contains(type) => steps.Where(e => e.Name == "View").Any(e => IsTypedView(type, e, c)),
                _ => throw new ScenarioConfigurationException("Dedicated adapter for " + type + " is not delivered yet") };
            if (!ok) throw new ScenarioConfigurationException(type + " requires its actual typed actors and native execution steps; changing the caseType label alone does not define that test");
            if (type != "ExternalScenario") trace("capability.validated", type + "; custom actors/actions/predicates from XML, no legacy preparation policy applied");
        }
    }
}
