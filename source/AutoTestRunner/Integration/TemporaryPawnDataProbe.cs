using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class TemporaryPawnDataProbe
    {
        internal static object LastResult;
        internal static object Run()
        {
            if (ManualTestService.Busy || global::LogSyncMod.LogSyncMod.settings is not { testSessionActive: true, testSessionKind: "manual" }) throw new InvalidOperationException("Pawn-data verification requires an idle CLI manual test session");
            var template = XElement.Parse("<PawnTemplate id='probe'><basePawnKind>Colonist</basePawnKind><gender>Female</gender><Age biologicalYears='35' chronologicalYears='35'/><Traits mode='replace'><Set def='Nimble' degree='0'/></Traits><Skills><Set def='Shooting' level='12' passion='Major'/></Skills><Apparel mode='replace'><Add def='Apparel_Pants' stuff='Cloth' quality='Good'/><Add def='Apparel_FlakVest' quality='Excellent'/><Add def='Apparel_SimpleHelmet' stuff='Steel' quality='Excellent'/></Apparel><Equipment><SetPrimary def='Gun_AssaultRifle' quality='Masterwork'/></Equipment><Hediffs mode='replace'><Ensure def='Scratch' part='Torso' severity='3'/></Hediffs></PawnTemplate>");
            if (ModsConfig.BiotechActive)
            {
                template.Add(new XElement("Genes", new XAttribute("mode", "replace"), new XElement("Add", new XAttribute("def", "Hemogenic"), new XAttribute("xenogene", "true"))),
                    new XElement("Abilities", new XElement("Ensure", new XAttribute("def", "MetalbloodInjection"))),
                    new XElement("Resources", new XElement("GeneResource", new XAttribute("def", "Hemogenic"), new XAttribute("value", ".3"))));
            }
            var objects = new List<Thing>(); var checks = new Dictionary<string, bool>();
            string sourceId = null, cloneId = null, hash = null;
            string stagedHash = null, stagedKind = null;
            try
            {
                var source = ScenarioGameBackend.GenerateTemporaryPawn(TemporaryPawnData.Create(template, "native-data-probe", null, null), null, null, out var sourceObjects); objects.AddRange(sourceObjects);
                sourceId = source.GetUniqueLoadID();
                var captured = TemporaryPawnCapture.Capture(source); hash = captured.Sha256;
                var frozen = TemporaryPawnData.Parse(TemporaryPawnData.Serialize(captured));
                var clone = ScenarioGameBackend.GenerateTemporaryPawn(frozen, null, null, out var cloneObjects); objects.AddRange(cloneObjects); cloneId = clone.GetUniqueLoadID();
                checks["source_unchanged"] = TemporaryPawnCapture.Capture(source).Sha256 == captured.Sha256;
                checks["fresh_identity"] = !ReferenceEquals(source, clone) && sourceId != cloneId;
                checks["kind_race_gender_age"] = clone.kindDef == source.kindDef && clone.def == source.def && clone.gender == Gender.Female && clone.ageTracker.AgeBiologicalTicks == source.ageTracker.AgeBiologicalTicks;
                checks["trait_skill"] = clone.story.traits.HasTrait(DefDatabase<TraitDef>.GetNamed("Nimble")) && clone.skills.GetSkill(SkillDefOf.Shooting).Level == 12 && clone.skills.GetSkill(SkillDefOf.Shooting).passion == Passion.Major;
                checks["apparel"] = clone.apparel.WornApparel.Count == 3 && clone.apparel.WornApparel.Any(a => a.def.defName == "Apparel_FlakVest" && a.TryGetQuality(out var q) && q == QualityCategory.Excellent);
                checks["weapon"] = clone.equipment.Primary?.def.defName == "Gun_AssaultRifle" && clone.equipment.Primary.TryGetQuality(out var weaponQuality) && weaponQuality == QualityCategory.Masterwork;
                checks["hediff_body_part"] = clone.health.hediffSet.hediffs.Any(h => h.def.defName == "Scratch" && h.Part?.def.defName == "Torso" && Math.Abs(h.Severity - 3) < .001f);
                checks["appearance"] = clone.story.hairDef == source.story.hairDef && clone.story.headType == source.story.headType && clone.story.bodyType == source.story.bodyType;
                checks["frozen_hash"] = frozen.Sha256 == captured.Sha256 && frozen.TemplateXml == captured.TemplateXml;
                if (ModsConfig.BiotechActive)
                {
                    checks["ability_gene_resource"] = clone.abilities.GetAbility(DefDatabase<AbilityDef>.GetNamed("MetalbloodInjection")) != null && Math.Abs(clone.genes.GetFirstGeneOfType<Gene_Hemogen>().Value - .3f) < .001f;
                }
                var project = ManualProjectBinding.Resolve();
                if (System.IO.File.Exists(TemporaryPawnData.PathFor(project.Root)))
                {
                    var persisted = TemporaryPawnData.Load(project.Root); stagedHash = persisted.Sha256;
                    var stagedPawn = ScenarioGameBackend.GenerateTemporaryPawn(persisted, null, null, out var stagedObjects); objects.AddRange(stagedObjects); stagedKind = stagedPawn.kindDef.defName;
                    checks["persisted_staged_pawn_loaded"] = stagedKind == XElement.Parse(persisted.TemplateXml).Element("basePawnKind").Value.Trim() && TemporaryPawnData.Load(project.Root).Sha256 == stagedHash;
                }
            }
            finally
            {
                foreach (var thing in objects.Distinct()) { if (thing is Pawn pawn && Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn); if (!thing.Destroyed) thing.Destroy(DestroyMode.Vanish); }
                checks["owned_cleanup"] = objects.All(t => t.Destroyed);
            }
            LastResult = new { allPassed = checks.Values.All(v => v), checks, sourceId, cloneId, templateHash = hash, stagedHash, stagedKind, artifacts = ManualTestService.Artifacts };
            return LastResult;
        }
    }
}
