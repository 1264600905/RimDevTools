using System;
using System.Collections.Generic;
using System.Linq;
using LogSyncMod;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed class AutoTestPawnFactionResolution
    {
        public AutoTestPawnFactionMode RequestedMode;
        public AutoTestPawnFactionMode EffectiveMode;
        public Faction Faction;
        public string FactionDefName;
        public string Reason;
    }

    internal static class AutoTestPawnFactionResolver
    {
        public static AutoTestPawnFactionResolution Resolve(PawnKindDef pawnKind, AutoTestPawnFactionMode requestedMode)
        {
            bool isMechanoid = pawnKind?.race?.race?.IsMechanoid == true;
            List<Faction> factions = Find.FactionManager?.AllFactionsListForReading ?? new List<Faction>();
            Faction player = Faction.OfPlayer ?? Find.FactionManager?.OfPlayer;
            AutoTestPawnFactionDecision decision = AutoTestPawnFactionPlanner.Resolve(
                requestedMode,
                isMechanoid,
                pawnKind?.defaultFactionDef?.defName,
                factions.Select(faction => faction?.def?.defName),
                player?.def?.defName);

            Faction resolved = null;
            switch (decision.EffectiveMode)
            {
                case AutoTestPawnFactionMode.Mechanoid:
                    resolved = Faction.OfMechanoids;
                    break;
                case AutoTestPawnFactionMode.PlayerCompatibility:
                    resolved = player;
                    break;
                case AutoTestPawnFactionMode.Native:
                    resolved = factions.FirstOrDefault(faction => faction?.def == pawnKind?.defaultFactionDef) ??
                        factions.FirstOrDefault(faction => string.Equals(faction?.def?.defName, decision.FactionDefName, StringComparison.OrdinalIgnoreCase));
                    break;
            }

            return new AutoTestPawnFactionResolution
            {
                RequestedMode = decision.RequestedMode,
                EffectiveMode = decision.EffectiveMode,
                Faction = resolved,
                FactionDefName = resolved?.def?.defName ?? decision.FactionDefName ?? string.Empty,
                Reason = decision.Reason ?? string.Empty
            };
        }

        public static AutoTestPawnFactionResolution ResolveEffective(PawnKindDef pawnKind, AutoTestPawnFactionMode requestedMode, AutoTestPawnFactionMode effectiveMode)
        {
            if (effectiveMode == AutoTestPawnFactionMode.FactionlessFallback)
            {
                return FactionlessFallback(requestedMode, "Triptych generation retained the source Pawn faction mode.");
            }

            AutoTestPawnFactionResolution resolution = Resolve(pawnKind,
                effectiveMode == AutoTestPawnFactionMode.PlayerCompatibility
                    ? AutoTestPawnFactionMode.PlayerCompatibility
                    : requestedMode);
            if (effectiveMode == AutoTestPawnFactionMode.Mechanoid)
            {
                resolution.EffectiveMode = AutoTestPawnFactionMode.Mechanoid;
                resolution.Faction = Faction.OfMechanoids;
            }
            return resolution;
        }

        public static AutoTestPawnFactionResolution FactionlessFallback(AutoTestPawnFactionMode requestedMode, string reason)
        {
            return new AutoTestPawnFactionResolution
            {
                RequestedMode = requestedMode,
                EffectiveMode = AutoTestPawnFactionMode.FactionlessFallback,
                Faction = null,
                FactionDefName = string.Empty,
                Reason = reason ?? "Native faction generation failed; retrying safely with no faction."
            };
        }

        public static AutoTestPawnFactionMode ParseRequestedMode(string value)
        {
            return string.Equals(value, AutoTestPawnFactionMode.PlayerCompatibility.ToString(), StringComparison.OrdinalIgnoreCase)
                ? AutoTestPawnFactionMode.PlayerCompatibility
                : AutoTestPawnFactionMode.Native;
        }
    }

    internal enum AutoTestBuildingContext
    {
        BuildingInfoCard,
        BuildingStaticView,
        RecipeWorktable,
        InteractiveBuilding,
        ExplicitFactionless
    }

    internal sealed partial class AutoTestRunner
    {
        private bool PrepareBuildingFactionBeforeSpawn(
            Thing thing,
            AutoTestBuildingContext context,
            AutoTestResult result,
            Faction explicitFaction = null,
            bool explicitFactionless = false,
            bool logWithoutResult = true)
        {
            string reason;
            bool prepared = false;
            Faction finalFaction = thing?.Faction;

            if (!(thing is Building))
            {
                reason = "NonBuilding";
            }
            else if (thing.def == null || !thing.def.CanHaveFaction)
            {
                reason = "CannotHaveFaction";
            }
            else if (thing.def.IsBlueprint || thing.def.IsFrame)
            {
                reason = "BlueprintOrFrame";
            }
            else if (thing.def.building?.isNaturalRock == true)
            {
                reason = "NaturalRock";
            }
            else if (thing.Faction != null)
            {
                reason = "AlreadyAssigned";
            }
            else if (explicitFactionless || context == AutoTestBuildingContext.ExplicitFactionless)
            {
                reason = "ExplicitFactionless";
            }
            else
            {
                Faction desiredFaction = explicitFaction ?? Faction.OfPlayer ?? Find.FactionManager?.OfPlayer;
                if (desiredFaction == null)
                {
                    reason = "RequestedFactionUnavailable";
                }
                else
                {
                    thing.SetFactionDirect(desiredFaction);
                    finalFaction = thing.Faction;
                    prepared = finalFaction == desiredFaction;
                    reason = explicitFaction != null ? "ExplicitSpecFaction" : context.ToString();
                }
            }

            string notes = "BuildingFactionPrepared=" + prepared +
                ";BuildingFactionReason=" + reason +
                ";BuildingFactionFinal=" + (finalFaction?.def?.defName ?? "None");
            if (result != null)
            {
                result.Notes = AppendNote(result.Notes, notes);
            }
            else if (logWithoutResult)
            {
                Log.Message("[LogSyncAutoTest] " + notes + ";Def=" + (thing?.def?.defName ?? "null"));
            }
            return prepared;
        }

        private static Faction ResolveFactionFromSpec(LogSyncAutoTestSpecDef spec)
        {
            if (string.IsNullOrWhiteSpace(spec?.buildingFactionDefName)) return null;
            return Find.FactionManager?.AllFactionsListForReading?.FirstOrDefault(faction =>
                string.Equals(faction?.def?.defName, spec.buildingFactionDefName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
