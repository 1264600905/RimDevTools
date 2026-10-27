using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace LogSyncMod
{
    internal static partial class LogSyncDebugActions
    {
        private static void StartQuickHealTool()
        {
            Map map = Find.CurrentMap;
            DebugToolsGeneral.GenericRectTool("LogSyncMod.DebugQuickHeal".Translate(), rect =>
            {
                if (!Prefs.DevMode || map == null || Find.CurrentMap != map) return;
                var rules = new DebugHealingRules();
                List<Pawn> targets = map.mapPawns.AllPawnsSpawned.Where(p => rect.Contains(p.Position)).ToList();
                int healed = 0, unchanged = 0, failed = 0, removed = 0, restored = 0;
                foreach (Pawn pawn in targets)
                {
                    if (pawn.Dead || pawn.health == null) { unchanged++; continue; }
                    bool changed = false, hadError = false;
                    // Remove only identified conditions. RestorePart would also erase unrelated implants
                    // and effects on child parts; missing-part hediffs can be removed individually instead.
                    List<Hediff> conditions = pawn.health.hediffSet.hediffs.Where(h => rules.ShouldRemove(h, pawn))
                        .OrderBy(h => h is Hediff_MissingPart ? 0 : h is Hediff_Injury ? 1 : 2).ToList();
                    foreach (Hediff hediff in conditions)
                    {
                        if (!pawn.health.hediffSet.hediffs.Contains(hediff)) continue;
                        try
                        {
                            pawn.health.RemoveHediff(hediff);
                            if (pawn.health.hediffSet.hediffs.Contains(hediff))
                                throw new InvalidOperationException("Health condition was not removed.");
                        }
                        catch (Exception ex)
                        {
                            hadError = true;
                            Log.Warning("[LogSyncMod] Quick heal failed for " + pawn.LabelShort + " / " + hediff.def.defName + ": " + ex);
                        }
                        finally
                        {
                            if (!pawn.health.hediffSet.hediffs.Contains(hediff))
                            {
                                changed = true;
                                if (hediff is Hediff_MissingPart) restored++;
                                else removed++;
                            }
                        }
                    }
                    if (changed)
                    {
                        pawn.Notify_DisabledWorkTypesChanged();
                        foreach (StatDef stat in DefDatabase<StatDef>.AllDefsListForReading)
                            stat.Worker.ClearCacheForThing(pawn);
                    }
                    if (hadError) failed++;
                    else if (changed) healed++;
                    else unchanged++;
                }
                Messages.Message("LogSyncMod.DebugQuickHealResult".Translate(healed, unchanged, failed, removed, restored),
                    failed == 0 ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.CautionInput, historical: false);
            });
        }
    }

    internal sealed class DebugHealingRules
    {
        private readonly HashSet<HediffDef> drugEffects = new HashSet<HediffDef>();
        private readonly HashSet<HediffDef> drugAddictions = new HashSet<HediffDef>();
        private readonly HashSet<HediffDef> diseases = new HashSet<HediffDef>();

        internal DebugHealingRules()
        {
            foreach (ChemicalDef chemical in DefDatabase<ChemicalDef>.AllDefsListForReading)
            {
                if (chemical.addictionHediff != null) drugAddictions.Add(chemical.addictionHediff);
                if (chemical.toleranceHediff != null) drugEffects.Add(chemical.toleranceHediff);
            }
            foreach (ThingDef item in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!item.IsDrug && item.GetCompProperties<CompProperties_Drug>() == null) continue;
                if (item.ingestible?.outcomeDoers == null) continue;
                foreach (IngestionOutcomeDoer_GiveHediff giver in item.ingestible.outcomeDoers.OfType<IngestionOutcomeDoer_GiveHediff>())
                    if (giver.hediffDef != null) drugEffects.Add(giver.hediffDef);
            }
            AddNamed(drugEffects, "DrugOverdose", "Anesthetic");
            foreach (IncidentDef incident in DefDatabase<IncidentDef>.AllDefsListForReading)
                if (incident.diseaseIncident != null) diseases.Add(incident.diseaseIncident);
            // These official ailments have no consistent infection/chronic/immunity marker.
            AddNamed(diseases, "BloodLoss", "FoodPoisoning", "ToxicBuildup", "Malnutrition", "Heatstroke", "Hypothermia",
                "Cirrhosis", "ChemicalDamageModerate", "ChemicalDamageSevere", "HeartAttack", "Abasia", "Scaria",
                "CryptosleepSickness", "ResurrectionSickness", "ResurrectionPsychosis");
        }

        private static void AddNamed(HashSet<HediffDef> set, params string[] names)
        {
            foreach (string name in names)
            {
                HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(name);
                if (def != null) set.Add(def);
            }
        }

        internal bool ShouldRemove(Hediff hediff, Pawn pawn)
        {
            HediffDef def = hediff.def;
            if (hediff is Hediff_Injury) return true; // Includes permanent scars and mechanical injuries.
            if (hediff is Hediff_MissingPart)
                return hediff.Part != null && !pawn.health.hediffSet.PartOrAnyAncestorHasDirectlyAddedParts(hediff.Part);

            // Genetic chemical dependency is not a drug addiction; implants and test markers are not diseases.
            if (hediff is Hediff_Implant || def.countsAsAddedPartOrImplant || def.pregnant || hediff is Hediff_ChemicalDependency ||
                def == LogSyncDebugDefOf.LogSync_DebugNeedsLock || def == LogSyncDebugDefOf.LogSync_DebugSkilledWorker10x ||
                def == LogSyncDebugDefOf.LogSync_DebugNeuralTest) return false;
            if (def.IsAddiction) return drugAddictions.Contains(def) || drugEffects.Contains(def);
            if (drugAddictions.Contains(def) || drugEffects.Contains(def) || hediff is Hediff_High) return true;
            return diseases.Contains(def) || def.isInfection || def.chronic ||
                hediff.TryGetComp<HediffComp_Immunizable>() != null;
        }
    }
}
