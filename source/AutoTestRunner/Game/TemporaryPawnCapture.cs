using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class TemporaryPawnCapture
    {
        internal static TemporaryPawnSnapshot Capture(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead || pawn.kindDef == null) throw new ScenarioConfigurationException("Select a living Pawn with a registered PawnKind");
            var first = Build(pawn);
            var second = Build(pawn);
            if (first.Sha256 != second.Sha256) throw new InvalidOperationException("Pawn changed while being captured; no staged template was written");
            return first;
        }

        internal static string CaptureAndSave(Pawn pawn, string projectRoot)
        {
            // Capture reads the player's Pawn only. Applying these data always generates a new Pawn.
            var snapshot = Capture(pawn);
            return TemporaryPawnData.Save(projectRoot, snapshot);
        }

        private static TemporaryPawnSnapshot Build(Pawn pawn)
        {
            string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
            var unsupported = new List<string> { "Names, backstories, faction relations and jobs", "Inventory and carried objects", "Ability cooldowns and charges", "Unregistered mod components and Hediff component private state", "Beards, tattoos and mod-specific appearance channels" };
            var root = new XElement("PawnTemplate", new XAttribute("id", "temporaryPawn"),
                new XElement("basePawnKind", pawn.kindDef.defName), new XElement("race", pawn.def.defName), new XElement("gender", pawn.gender));
            if (pawn.ageTracker != null) root.Add(new XElement("Age", new XAttribute("biologicalYears", Number((double)pawn.ageTracker.AgeBiologicalTicks / GenDate.TicksPerYear)), new XAttribute("chronologicalYears", Number((double)pawn.ageTracker.AgeChronologicalTicks / GenDate.TicksPerYear))));
            if (pawn.story != null)
            {
                var appearance = new XElement("Appearance");
                if (pawn.story.hairDef != null) appearance.Add(new XElement("hair", pawn.story.hairDef.defName));
                if (pawn.story.bodyType != null) appearance.Add(new XElement("bodyType", pawn.story.bodyType.defName));
                if (pawn.story.headType != null) appearance.Add(new XElement("headType", pawn.story.headType.defName));
                appearance.Add(new XElement("hairColor", "#" + ColorUtility.ToHtmlStringRGBA(pawn.story.HairColor)), new XElement("skinColor", "#" + ColorUtility.ToHtmlStringRGBA(pawn.story.SkinColor)));
                root.Add(appearance);
                root.Add(new XElement("Traits", new XAttribute("mode", "replace"), pawn.story.traits.allTraits.Select(t => new XElement("Set", new XAttribute("def", t.def.defName), new XAttribute("degree", t.Degree)))));
            }
            if (pawn.skills != null)
            {
                var skills = new XElement("Skills", new XAttribute("mode", "patch"));
                foreach (var skill in pawn.skills.skills)
                {
                    if (skill.TotallyDisabled) { unsupported.Add("Disabled skill: " + skill.def.defName); continue; }
                    skills.Add(new XElement("Set", new XAttribute("def", skill.def.defName), new XAttribute("level", skill.Level), new XAttribute("passion", skill.passion)));
                }
                root.Add(skills);
            }
            XElement Item(Thing thing, string tag)
            {
                var item = new XElement(tag, new XAttribute("def", thing.def.defName));
                if (thing.Stuff != null) item.Add(new XAttribute("stuff", thing.Stuff.defName));
                if (thing.TryGetQuality(out var quality)) item.Add(new XAttribute("quality", quality));
                var colorable = thing.TryGetComp<CompColorable>();
                if (colorable?.Active == true) item.Add(new XAttribute("color", "#" + ColorUtility.ToHtmlStringRGBA(colorable.Color)));
                return item;
            }
            if (pawn.apparel != null) root.Add(new XElement("Apparel", new XAttribute("mode", "replace"), pawn.apparel.WornApparel.Select(a => Item(a, "Add"))));
            if (pawn.equipment != null)
            {
                var equipment = new XElement("Equipment", new XAttribute("mode", "replace"));
                if (pawn.equipment.Primary != null) equipment.Add(Item(pawn.equipment.Primary, "SetPrimary"));
                if (pawn.equipment.AllEquipmentListForReading.Count > 1) unsupported.Add("Additional equipment beyond primary");
                root.Add(equipment);
            }
            if (pawn.genes != null)
            {
                if (pawn.genes.Xenotype != null) root.Add(new XElement("Xenotype", new XAttribute("def", pawn.genes.Xenotype.defName)));
                root.Add(new XElement("Genes", new XAttribute("mode", "replace"), pawn.genes.GenesListForReading.Select(g => new XElement("Add", new XAttribute("def", g.def.defName), new XAttribute("xenogene", pawn.genes.Xenogenes.Contains(g) ? "true" : "false")))));
            }
            if (pawn.abilities != null) root.Add(new XElement("Abilities", new XAttribute("mode", "replace"), pawn.abilities.abilities.Select(a => new XElement("Ensure", new XAttribute("def", a.def.defName)))));
            if (pawn.health != null)
            {
                string PartPath(BodyPartRecord part) => part.parent == null ? part.def.defName : PartPath(part.parent) + "/" + part.def.defName + "[" + part.parent.parts.IndexOf(part) + "]";
                var hediffs = new XElement("Hediffs", new XAttribute("mode", "replace"));
                foreach (var group in pawn.health.hediffSet.hediffs.GroupBy(h => h.def.defName + "@" + (h.Part == null ? "whole" : PartPath(h.Part))))
                {
                    if (group.Count() != 1) { unsupported.Add("Multiple Hediffs with one Def/part: " + group.Key); continue; }
                    var hediff = group.Single();
                    if (hediff.Part?.parent != null && pawn.health.hediffSet.PartIsMissing(hediff.Part.parent)) { unsupported.Add("Hediff on descendant of missing body part: " + group.Key); continue; }
                    var item = new XElement("Ensure", new XAttribute("def", hediff.def.defName), new XAttribute("severity", Number(hediff.Severity)));
                    if (hediff.Part != null) item.Add(new XAttribute("part", PartPath(hediff.Part)));
                    if (hediff.TryGetComp<HediffComp_Disappears>() is HediffComp_Disappears disappears && disappears.ticksToDisappear > 0) item.Add(new XAttribute("durationTicks", disappears.ticksToDisappear));
                    hediffs.Add(item);
                }
                root.Add(hediffs);
            }
            var resources = new XElement("Resources");
            foreach (var resource in pawn.genes?.GenesListForReading.OfType<Gene_Resource>().Where(g => g.Active) ?? Enumerable.Empty<Gene_Resource>())
                resources.Add(new XElement("GeneResource", new XAttribute("def", resource.def.defName), new XAttribute("value", Number(resource.Value))));
            if (pawn.GetPsylinkLevel() > 0 && pawn.psychicEntropy != null) resources.Add(new XElement("Psyfocus", new XAttribute("value", Number(pawn.psychicEntropy.CurrentPsyfocus))));
            if (pawn.AllComps.Any(c => c.GetType().FullName == "Axolotl.CompAxolotlEnergy")) resources.Add(new XElement("AxolotlQi", new XAttribute("value", Number(ScenarioGameBackend.ReadAxolotlQi(pawn, out _)))));
            if (resources.HasElements) root.Add(resources);
            return TemporaryPawnData.Create(root, pawn.GetUniqueLoadID(), root.Elements().Select(e => e.Name.LocalName), unsupported);
        }
    }
}
