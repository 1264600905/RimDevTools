using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LogSyncMod;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed class AutoTestHediffPartResolution
    {
        public BodyPartRecord Part;
        public string Source;
        public RecipeDef InstallRecipe;
        public string InstallContext;
        public bool Supported;
        public string ErrorMessage;
    }

    internal sealed class AutoTestHediffPartResolver
    {
        private readonly ModContentPack targetMod;

        public AutoTestHediffPartResolver(ModContentPack targetMod)
        {
            this.targetMod = targetMod;
        }

        public AutoTestHediffPartResolution Resolve(HediffDef def, Pawn pawn)
        {
            if (def == null || pawn?.health?.hediffSet == null)
                return Unsupported("HediffDef or pawn health context was missing.");

            List<BodyPartRecord> parts = (pawn.health.hediffSet.GetNotMissingParts(default(BodyPartHeight), default(BodyPartDepth), null, null)
                ?? Enumerable.Empty<BodyPartRecord>()).ToList();

            if (def.defaultInstallPart != null)
            {
                BodyPartRecord defaultPart = parts
                    .Where(part => part?.def == def.defaultInstallPart)
                    .OrderBy(part => StablePartName(part), StringComparer.Ordinal)
                    .FirstOrDefault();
                if (defaultPart != null)
                {
                    return new AutoTestHediffPartResolution
                    {
                        Part = defaultPart,
                        Source = "defaultInstallPart",
                        Supported = true
                    };
                }
            }

            List<AutoTestHediffPartCandidate<AutoTestHediffPartResolution>> recipeCandidates = new List<AutoTestHediffPartCandidate<AutoTestHediffPartResolution>>();
            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading
                .Where(recipe => recipe?.addsHediff == def && recipe.appliedOnFixedBodyParts != null)
                .OrderBy(recipe => recipe.defName, StringComparer.Ordinal))
            {
                foreach (BodyPartDef partDef in recipe.appliedOnFixedBodyParts.Where(partDef => partDef != null).OrderBy(partDef => partDef.defName, StringComparer.Ordinal))
                {
                    BodyPartRecord actualPart = parts
                        .Where(part => part?.def == partDef)
                        .OrderBy(part => StablePartName(part), StringComparer.Ordinal)
                        .FirstOrDefault();
                    if (actualPart == null) continue;

                    bool targetRecipe = recipe.modContentPack == targetMod;
                    AutoTestHediffPartResolution resolution = new AutoTestHediffPartResolution
                    {
                        Part = actualPart,
                        Source = targetRecipe ? "targetModInstallRecipe" : "installRecipe",
                        InstallRecipe = recipe,
                        Supported = true
                    };
                    recipeCandidates.Add(new AutoTestHediffPartCandidate<AutoTestHediffPartResolution>
                    {
                        Value = resolution,
                        Source = targetRecipe ? AutoTestHediffPartSourceKind.TargetModInstallRecipe : AutoTestHediffPartSourceKind.OtherInstallRecipe,
                        StableName = (recipe.defName ?? string.Empty) + "/" + StablePartName(actualPart)
                    });
                }
            }

            AutoTestHediffPartCandidate<AutoTestHediffPartResolution> selected = AutoTestHediffPartPolicy.Select(recipeCandidates);
            if (selected != null)
                return selected.Value;

            AutoTestHediffPartResolution compResolution = ResolveFromTargetModComp(def, parts);
            if (compResolution != null)
                return compResolution;

            if (def.addedPartProps != null || def.injuryProps != null || def.defaultInstallPart != null)
                return Unsupported("Part-specific Hediff has no legal defaultInstallPart or installation Recipe part on pawn " + pawn.kindDef?.defName + ".");

            return new AutoTestHediffPartResolution
            {
                Part = null,
                Source = "wholeBody",
                Supported = true
            };
        }

        private static AutoTestHediffPartResolution Unsupported(string message)
        {
            return new AutoTestHediffPartResolution { Supported = false, ErrorMessage = message };
        }

        private AutoTestHediffPartResolution ResolveFromTargetModComp(HediffDef def, List<BodyPartRecord> parts)
        {
            List<AutoTestHediffPartResolution> matches = new List<AutoTestHediffPartResolution>();
            foreach (ThingDef thingDef in DefDatabase<ThingDef>.AllDefsListForReading
                .Where(candidate => candidate?.modContentPack == targetMod && candidate.comps != null)
                .OrderBy(candidate => candidate.defName, StringComparer.Ordinal))
            {
                foreach (CompProperties comp in thingDef.comps.Where(comp => comp != null).OrderBy(comp => comp.GetType().FullName, StringComparer.Ordinal))
                {
                    FieldInfo[] fields = comp.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    bool referencesHediff = fields.Where(field => typeof(HediffDef).IsAssignableFrom(field.FieldType))
                        .Any(field => ReferenceEquals(field.GetValue(comp), def));
                    if (!referencesHediff) continue;

                    foreach (FieldInfo bodyPartField in fields.Where(field => typeof(BodyPartDef).IsAssignableFrom(field.FieldType)).OrderBy(field => field.Name, StringComparer.Ordinal))
                    {
                        BodyPartDef partDef = bodyPartField.GetValue(comp) as BodyPartDef;
                        BodyPartRecord actualPart = parts.Where(part => part?.def == partDef).OrderBy(StablePartName, StringComparer.Ordinal).FirstOrDefault();
                        if (actualPart == null) continue;
                        matches.Add(new AutoTestHediffPartResolution
                        {
                            Part = actualPart,
                            Source = "targetModCompDefinition",
                            InstallContext = thingDef.defName + ":" + comp.GetType().FullName + "." + bodyPartField.Name,
                            Supported = true
                        });
                    }
                }
            }
            return matches.OrderBy(match => match.InstallContext, StringComparer.Ordinal).FirstOrDefault();
        }

        private static string StablePartName(BodyPartRecord part)
        {
            if (part == null) return string.Empty;
            List<string> path = new List<string>();
            for (BodyPartRecord cursor = part; cursor != null; cursor = cursor.parent)
                path.Add(cursor.def?.defName ?? string.Empty);
            path.Reverse();
            return string.Join("/", path.ToArray());
        }
    }

    internal sealed class AutoTestHediffPawnResolution
    {
        public Pawn Pawn;
        public PawnKindDef PawnKind;
        public string Source;
        public AutoTestHediffPartResolution PartResolution;
        public readonly List<Hediff> AddedPrerequisites = new List<Hediff>();
        public bool PassedAfterRetry;
        public string FirstFailure;
        public string ErrorMessage;
    }

    internal sealed class AutoTestHediffPawnResolver
    {
        private readonly Map map;
        private readonly ModContentPack targetMod;
        private readonly Func<IntVec3> allocateSpawnCell;
        private readonly AutoTestHediffPartResolver partResolver;

        public AutoTestHediffPawnResolver(Map map, ModContentPack targetMod, Func<IntVec3> allocateSpawnCell)
        {
            this.map = map;
            this.targetMod = targetMod;
            this.allocateSpawnCell = allocateSpawnCell;
            partResolver = new AutoTestHediffPartResolver(targetMod);
        }

        public AutoTestHediffPawnResolution Resolve(HediffDef hediffDef, LogSyncAutoTestSpecDef spec)
        {
            AutoTestHediffPawnResolution final = new AutoTestHediffPawnResolution();
            string requiredRace = (spec?.requiredRace ?? string.Empty).Trim();
            List<AutoTestHediffPawnCandidate<PawnKindDef>> candidates = BuildCandidates(hediffDef, spec)
                .Where(candidate => candidate.Value?.race != null &&
                    (requiredRace.Length == 0 || string.Equals(candidate.Value.race.defName, requiredRace, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            foreach (AutoTestHediffPawnCandidate<PawnKindDef> candidate in AutoTestHediffPawnPolicy.Order(candidates))
            {
                string firstFailure = null;
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    Pawn pawn = null;
                    try
                    {
                        pawn = GeneratePawn(candidate.Value, spec);
                        if (pawn == null)
                            throw new InvalidOperationException("PawnGenerator.GeneratePawn returned null.");
                        if (pawn.health == null)
                            throw new InvalidOperationException("Generated pawn has no health tracker.");

                        List<Hediff> addedPrerequisites = AddDeclaredPrerequisites(pawn, spec);
                        AutoTestHediffPartResolution part = partResolver.Resolve(hediffDef, pawn);
                        if (!part.Supported)
                        {
                            if (firstFailure == null) firstFailure = part.ErrorMessage;
                            DestroyPawn(pawn);
                            break;
                        }

                        IntVec3 cell = allocateSpawnCell == null ? IntVec3.Invalid : allocateSpawnCell();
                        if (!cell.IsValid || !cell.InBounds(map))
                            throw new InvalidOperationException("No safe spawn cell remained for Hediff presenter pawn.");
                        GenSpawn.Spawn(pawn, cell, map, WipeMode.Vanish);
                        if (pawn.Destroyed || pawn.Dead || pawn.Map != map)
                            throw new InvalidOperationException("Presenter pawn was destroyed, died, or failed to enter the map during the stability check.");

                        final.Pawn = pawn;
                        final.PawnKind = candidate.Value;
                        final.Source = candidate.Source.ToString();
                        final.PartResolution = part;
                        final.AddedPrerequisites.AddRange(addedPrerequisites);
                        final.PassedAfterRetry = attempt == 2;
                        final.FirstFailure = firstFailure;
                        return final;
                    }
                    catch (Exception ex)
                    {
                        if (firstFailure == null) firstFailure = ex.Message;
                        DestroyPawn(pawn);
                        if (!AutoTestRetryPolicy.CanRetry(AutoTestFailureKind.TransientPawnGeneration, attempt))
                            break;
                    }
                }

                final.FirstFailure = firstFailure;
            }

            final.ErrorMessage = final.FirstFailure ?? "No legal PawnKind candidate could be generated for Hediff " + hediffDef?.defName + ".";
            return final;
        }

        private List<AutoTestHediffPawnCandidate<PawnKindDef>> BuildCandidates(HediffDef hediffDef, LogSyncAutoTestSpecDef spec)
        {
            List<AutoTestHediffPawnCandidate<PawnKindDef>> candidates = new List<AutoTestHediffPawnCandidate<PawnKindDef>>();
            string explicitPawnKind = (spec?.presenterPawnKind ?? string.Empty).Trim();
            if (explicitPawnKind.Length > 0)
                AddCandidate(candidates, DefDatabase<PawnKindDef>.GetNamedSilentFail(explicitPawnKind), AutoTestHediffPawnCandidateSource.ExplicitSpec);

            IEnumerable<PawnKindDef> allKinds = DefDatabase<PawnKindDef>.AllDefsListForReading.Where(kind => kind?.race?.race != null);
            foreach (PawnKindDef kind in allKinds.Where(kind => HasStartingHediff(kind, hediffDef)))
                AddCandidate(candidates, kind, AutoTestHediffPawnCandidateSource.DirectStartingHediff);

            foreach (PawnKindDef kind in allKinds.Where(kind => kind.modContentPack == targetMod && HasNativePrerequisiteEnvironment(kind, hediffDef)))
                AddCandidate(candidates, kind, AutoTestHediffPawnCandidateSource.NativePrerequisiteEnvironment);

            foreach (PawnKindDef kind in allKinds.Where(kind => kind.modContentPack == targetMod && kind.RaceProps.Humanlike))
                AddCandidate(candidates, kind, AutoTestHediffPawnCandidateSource.SameModHumanlike);

            foreach (PawnKindDef kind in allKinds.Where(kind => kind.modContentPack == targetMod && !kind.RaceProps.Humanlike))
                AddCandidate(candidates, kind, AutoTestHediffPawnCandidateSource.SameModAnimalOrMechanoid);

            AddCandidate(candidates, PawnKindDefOf.Colonist, AutoTestHediffPawnCandidateSource.VanillaColonist);
            return candidates;
        }

        private static void AddCandidate(List<AutoTestHediffPawnCandidate<PawnKindDef>> candidates, PawnKindDef kind, AutoTestHediffPawnCandidateSource source)
        {
            if (kind == null || candidates.Any(candidate => candidate.Value == kind)) return;
            candidates.Add(new AutoTestHediffPawnCandidate<PawnKindDef> { Value = kind, Source = source, StableName = kind.defName });
        }

        private bool HasNativePrerequisiteEnvironment(PawnKindDef kind, HediffDef targetHediff)
        {
            bool targetStartingHediff = kind.startingHediffs != null && kind.startingHediffs.Any(starting => starting?.def?.modContentPack == targetMod);
            bool targetHediffGiver = kind.RaceProps.hediffGiverSets != null && kind.RaceProps.hediffGiverSets
                .Where(set => set?.hediffGivers != null)
                .SelectMany(set => set.hediffGivers)
                .Any(giver => giver?.hediff?.modContentPack == targetMod);
            bool raceCompReference = kind.race?.comps != null && kind.race.comps.Any(comp => CompReferencesHediff(comp, targetHediff));
            return targetStartingHediff || targetHediffGiver || raceCompReference;
        }

        private static bool CompReferencesHediff(CompProperties comp, HediffDef targetHediff)
        {
            if (comp == null || targetHediff == null) return false;
            return comp.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field => typeof(HediffDef).IsAssignableFrom(field.FieldType))
                .Any(field => ReferenceEquals(field.GetValue(comp), targetHediff));
        }

        private static bool HasStartingHediff(PawnKindDef kind, HediffDef hediffDef)
        {
            return kind.startingHediffs != null && kind.startingHediffs.Any(starting => starting?.def == hediffDef);
        }

        private Pawn GeneratePawn(PawnKindDef kind, LogSyncAutoTestSpecDef spec)
        {
            Faction faction = ResolveFaction(kind, spec);
            PawnGenerationRequest request = new PawnGenerationRequest(
                kind,
                faction,
                PawnGenerationContext.NonPlayer,
                map.Tile,
                forceGenerateNewPawn: true,
                allowDead: false,
                allowDowned: false,
                canGeneratePawnRelations: false,
                mustBeCapableOfViolence: false,
                allowPregnant: false,
                allowAddictions: false,
                fixedBiologicalAge: kind.RaceProps.Humanlike ? (float?)25f : null,
                fixedChronologicalAge: kind.RaceProps.Humanlike ? (float?)25f : null,
                forceBaselinerChance: kind.RaceProps.Humanlike ? 1f : 0f,
                developmentalStages: DevelopmentalStage.Adult,
                forceNoGear: true);
            return PawnGenerator.GeneratePawn(request);
        }

        private static Faction ResolveFaction(PawnKindDef kind, LogSyncAutoTestSpecDef spec)
        {
            string mode = (spec?.requiredFactionMode ?? spec?.pawnFactionMode ?? string.Empty).Trim();
            if (mode.Equals("Factionless", StringComparison.OrdinalIgnoreCase) || mode.Equals("None", StringComparison.OrdinalIgnoreCase))
                return null;
            if (mode.Equals("Native", StringComparison.OrdinalIgnoreCase) && kind.defaultFactionDef != null)
                return Find.FactionManager?.FirstFactionOfDef(kind.defaultFactionDef);
            return Faction.OfPlayer ?? Find.FactionManager?.OfPlayer;
        }

        private List<Hediff> AddDeclaredPrerequisites(Pawn pawn, LogSyncAutoTestSpecDef spec)
        {
            List<Hediff> added = new List<Hediff>();
            foreach (string defName in spec?.prerequisiteHediffs ?? new List<string>())
            {
                HediffDef prerequisiteDef = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
                if (prerequisiteDef == null)
                    throw new InvalidOperationException("Declared prerequisite Hediff was not found: " + defName);
                if (pawn.health.hediffSet.HasHediff(prerequisiteDef)) continue;

                AutoTestHediffPartResolution part = partResolver.Resolve(prerequisiteDef, pawn);
                if (!part.Supported)
                    throw new InvalidOperationException("Declared prerequisite Hediff has no legal part: " + defName + ". " + part.ErrorMessage);
                Hediff hediff = HediffMaker.MakeHediff(prerequisiteDef, pawn, part.Part);
                pawn.health.AddHediff(hediff, part.Part, null, null);
                added.Add(hediff);
            }
            return added;
        }

        private static void DestroyPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed) return;
            try { pawn.Destroy(DestroyMode.Vanish); } catch { }
        }
    }
}
