using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace LogSyncMod
{
    internal static partial class LogSyncDebugActions
    {
        private static void StartNeuralTool()
        {
            HediffDef psylinkDef = ModsConfig.RoyaltyActive ? DefDatabase<HediffDef>.GetNamedSilentFail("PsychicAmplifier") : null;
            HediffDef mechlinkDef = ModsConfig.BiotechActive ? DefDatabase<HediffDef>.GetNamedSilentFail("MechlinkImplant") : null;
            if (psylinkDef == null && mechlinkDef == null)
            {
                Messages.Message("LogSyncMod.DebugNeuralUnavailable".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            Map map = Find.CurrentMap;
            string label = "LogSyncMod.DebugNeuralToggle".Translate();
            DebugToolsGeneral.GenericRectTool(label, rect =>
            {
                if (!Prefs.DevMode || map == null || Find.CurrentMap != map) return;
                int added = 0, removed = 0, skipped = 0, failed = 0;
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.Where(p => rect.Contains(p.Position)).ToList())
                {
                    if (pawn.Dead || pawn.Faction != Faction.OfPlayer || !pawn.RaceProps.Humanlike || pawn.health == null)
                    {
                        skipped++;
                        continue;
                    }
                    Hediff_LogSyncNeuralTest state = pawn.health.hediffSet.GetFirstHediffOfDef(LogSyncDebugDefOf.LogSync_DebugNeuralTest) as Hediff_LogSyncNeuralTest;
                    try
                    {
                        if (state != null)
                        {
                            pawn.health.RemoveHediff(state);
                            removed++;
                            continue;
                        }
                        BodyPartRecord brain = pawn.health.hediffSet.GetBrain();
                        if (brain == null || psylinkDef != null && pawn.abilities == null)
                        {
                            skipped++;
                            continue;
                        }
                        state = (Hediff_LogSyncNeuralTest)HediffMaker.MakeHediff(LogSyncDebugDefOf.LogSync_DebugNeuralTest, pawn);
                        pawn.health.AddHediff(state);
                        state.Apply(psylinkDef, mechlinkDef, brain);
                        added++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Log.Warning("[LogSyncMod] Neural debug state failed for " + pawn.LabelShort + ": " + ex);
                        if (state != null && pawn.health.hediffSet.hediffs.Contains(state))
                        {
                            try { pawn.health.RemoveHediff(state); }
                            catch (Exception rollbackError) { Log.Warning("[LogSyncMod] Neural debug cleanup failed: " + rollbackError); }
                        }
                    }
                }
                string available = psylinkDef != null && mechlinkDef != null ? "LogSyncMod.DebugNeuralBoth" :
                    psylinkDef != null ? "LogSyncMod.DebugNeuralPsyOnly" : "LogSyncMod.DebugNeuralMechOnly";
                Messages.Message("LogSyncMod.DebugPawnResult".Translate(label, added, removed, skipped, failed) + "\n" + available.Translate(),
                    MessageTypeDefOf.NeutralEvent, historical: false);
            });
        }
    }

    public sealed class Hediff_LogSyncNeuralTest : HediffWithComps
    {
        private Hediff_Psylink psylink;
        private bool addedPsylink;
        private bool raisedPsylink;
        private int originalPsylinkLevel;
        private Hediff addedMechlink;
        private List<AbilityDef> grantedPsycasts = new List<AbilityDef>();

        internal void Apply(HediffDef psylinkDef, HediffDef mechlinkDef, BodyPartRecord brain)
        {
            if (psylinkDef != null)
            {
                HashSet<AbilityDef> previous = new HashSet<AbilityDef>(pawn.abilities.abilities.Select(a => a.def));
                try
                {
                    psylink = pawn.health.hediffSet.GetFirstHediffOfDef(psylinkDef) as Hediff_Psylink;
                    if (psylink == null)
                    {
                        psylink = (Hediff_Psylink)HediffMaker.MakeHediff(psylinkDef, pawn, brain);
                        addedPsylink = true;
                        psylink.suppressPostAddLetter = true;
                        try { pawn.health.AddHediff(psylink, brain); }
                        finally { psylink.suppressPostAddLetter = false; }
                    }
                    else originalPsylinkLevel = psylink.level;
                    if (psylink.level < 6)
                    {
                        raisedPsylink = true;
                        psylink.ChangeLevel(6 - psylink.level, sendLetter: false);
                    }
                    if (!pawn.health.hediffSet.hediffs.Contains(psylink) || psylink.level < 6)
                        throw new InvalidOperationException("Unable to grant psylink level 6.");
                    psylink.Severity = psylink.level;
                }
                finally
                {
                    grantedPsycasts = pawn.abilities.abilities.Select(a => a.def)
                        .Where(a => a.IsPsycast && !previous.Contains(a)).Distinct().ToList();
                }
            }
            if (mechlinkDef != null)
            {
                if (!pawn.health.hediffSet.HasHediff(mechlinkDef))
                {
                    addedMechlink = HediffMaker.MakeHediff(mechlinkDef, pawn, brain);
                    pawn.health.AddHediff(addedMechlink, brain);
                }
                if (!pawn.health.hediffSet.HasHediff(mechlinkDef))
                    throw new InvalidOperationException("Unable to grant mechlink.");
            }
            RefreshTrackers();
        }

        private void RefreshTrackers()
        {
            pawn.health.Notify_HediffChanged(this);
            foreach (StatDef stat in DefDatabase<StatDef>.AllDefsListForReading) stat.Worker.ClearCacheForThing(pawn);
            PawnComponentsUtility.AddAndRemoveDynamicComponents(pawn);
            pawn.mechanitor?.Notify_BandwidthChanged();
        }

        public override void PostRemoved()
        {
            base.PostRemoved();
            if (psylink != null && pawn.health.hediffSet.hediffs.Contains(psylink))
            {
                if (addedPsylink) pawn.health.RemoveHediff(psylink);
                else if (raisedPsylink)
                {
                    psylink.ChangeLevel(originalPsylinkLevel - psylink.level, sendLetter: false);
                    psylink.Severity = psylink.level;
                }
            }
            foreach (AbilityDef ability in grantedPsycasts) pawn.abilities?.RemoveAbility(ability);
            if (addedMechlink != null && pawn.health.hediffSet.hediffs.Contains(addedMechlink)) pawn.health.RemoveHediff(addedMechlink);
            RefreshTrackers();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (psylink != null && !pawn.health.hediffSet.hediffs.Contains(psylink)) psylink = null;
                if (addedMechlink != null && !pawn.health.hediffSet.hediffs.Contains(addedMechlink)) addedMechlink = null;
            }
            Scribe_References.Look(ref psylink, "testPsylink");
            Scribe_Values.Look(ref addedPsylink, "addedPsylink", false);
            Scribe_Values.Look(ref raisedPsylink, "raisedPsylink", false);
            Scribe_Values.Look(ref originalPsylinkLevel, "originalPsylinkLevel", 0);
            Scribe_References.Look(ref addedMechlink, "addedMechlink");
            Scribe_Collections.Look(ref grantedPsycasts, "grantedPsycasts", LookMode.Def);
            if (grantedPsycasts == null) grantedPsycasts = new List<AbilityDef>();
        }
    }
}
