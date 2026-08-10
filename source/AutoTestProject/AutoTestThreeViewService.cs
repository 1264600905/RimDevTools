using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed class AutoTestThreeViewTargetSpec
    {
        public AutoTestKind Kind;
        public AutoTestCaseType CaseType;
        public AutoTestManifestEntry ManifestEntry;
        public Def TargetDef;
        public PawnKindDef PawnKindDef;
        public ThingDef RaceDef;
        public ThingDef ItemDef;
        public HairDef TargetHairDef;
        public HeadTypeDef TargetHeadTypeDef;
        public GeneDef TargetGeneDef;
        public XenotypeDef TargetXenotypeDef;
        public List<PawnKindDef> PresenterCandidates;
        public string CompatibilitySource;
        public string SkipReason;
        public string BeforeVisualSignature;
        public string AfterVisualSignature;
        public bool VisualEffectDetected;
        public BodyTypeDef ForcedBodyType;
        public Gender? ForcedGender;
        public bool WearAsApparel;
        public bool HoldAsWeapon;
        public bool ForceStripLoadout;
        public LifeStageDef TargetLifeStage;
        public int TargetLifeStageIndex = -1;
        public float TargetBiologicalAgeYears = -1f;
        public string RelativeScreenshotSubdirectory;
        public string ScreenshotStageSuffix;
        public string OverlayLine1;
        public string OverlayLine2;
        public string OverlayLine3;
        public string OverlayLine4;
        public bool LifeStageLockFallbackUsed;
        public string PawnRaceDefName;
        public string PawnRaceLabel;
        public string PawnBodyTypeDefName;
        public string PawnBodyTypeLabel;
        public string DefName;
        public string Label;
        public AutoTestScreenshotProfile Profile;
    }

    internal sealed class AutoTestThreeViewTargetContext
    {
        public Map Map;
        public IntVec3 AnchorCell;
        public AutoTestScreenshotProfile Profile;
        public readonly List<Pawn> Pawns = new List<Pawn>();
        public readonly List<IntVec3> Cells = new List<IntVec3>();
        public readonly List<Rot4> Rotations = new List<Rot4>();
        public readonly List<IntVec3> WalkTargets = new List<IntVec3>();
        public bool DirectionalWalkPrimed;
        public int ActualLifeStageIndex = -1;
        public LifeStageDef ActualLifeStage;
        public float ActualBiologicalAgeYears = -1f;
        public bool LifeStageLockFallbackUsed;
        public bool VisualEffectDetected;
        public bool TargetGenePresent;
        public string TargetGeneLocation;
        public bool TargetXenotypeMatches;
        public bool TargetXenotypeGeneSetMatches;
    }

    internal sealed class AutoTestThreeViewAppearanceTemplate
    {
        public Gender Gender;
        public long BiologicalAgeTicks;
        public long ChronologicalAgeTicks;
        public BodyTypeDef BodyType;
        public HeadTypeDef HeadType;
        public HairDef HairDef;
        public Color HairColor;
        public float Melanin;
        public Color? SkinColorBase;
        public Color? SkinColorOverride;
        public BeardDef BeardDef;
        public TattooDef FaceTattoo;
        public TattooDef BodyTattoo;
        public XenotypeDef Xenotype;
        public readonly List<GeneDef> Endogenes = new List<GeneDef>();
        public readonly List<GeneDef> Xenogenes = new List<GeneDef>();
        public Name Name;
    }

    internal sealed class AutoTestThreeViewService
    {
        private static readonly Rot4[] DefaultRotations = { Rot4.West, Rot4.South, Rot4.North };
        private static readonly FieldInfo StoryMelaninField = typeof(Pawn_StoryTracker).GetField("melanin", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo RecalculateLifeStageIndexMethod = typeof(Pawn_AgeTracker).GetMethod("RecalculateLifeStageIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public bool PrepareStudioArea(Map map, IntVec3 rootCell, int width, int height)
        {
            if (map == null)
            {
                return false;
            }

            TerrainDef soil = TerrainDefOf.Soil ?? DefDatabase<TerrainDef>.GetNamedSilentFail("Soil");
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    IntVec3 cell = new IntVec3(rootCell.x + x, 0, rootCell.z + z);
                    if (!cell.InBounds(map))
                    {
                        continue;
                    }

                    foreach (Thing thing in cell.GetThingList(map).ToList())
                    {
                        if (thing == null || thing.Destroyed)
                        {
                            continue;
                        }

                        if (!(thing.def?.destroyable ?? false))
                        {
                            continue;
                        }

                        try
                        {
                            thing.Destroy(DestroyMode.Vanish);
                        }
                        catch
                        {
                        }
                    }

                    try
                    {
                        if (soil != null)
                        {
                            map.terrainGrid.SetTerrain(cell, soil);
                        }

                        map.fogGrid?.Unfog(cell);
                        map.roofGrid?.SetRoof(cell, null);
                    }
                    catch
                    {
                    }
                }
            }

            return true;
        }

        public List<AutoTestThreeViewTargetSpec> ResolveDefaultTargets(
            ModContentPack targetMod,
            ISet<ThingDef> excludedWeaponDefs,
            Func<AutoTestCaseType, string, string, string, AutoTestManifestEntry> registerDiscoveredCase)
        {
            List<AutoTestThreeViewTargetSpec> targets = new List<AutoTestThreeViewTargetSpec>();
            if (registerDiscoveredCase == null)
            {
                return targets;
            }

            AddTargetModPawnKindTargets(targets, targetMod, registerDiscoveredCase, AutoTestKind.HumanoidThreeView, AutoTestCaseType.HumanoidThreeView, def => def?.race == ThingDefOf.Human);
            AddTargetModAnimalLifeStageTargets(targets, targetMod, registerDiscoveredCase);
            AddTargetModPawnKindTargets(targets, targetMod, registerDiscoveredCase, AutoTestKind.MechanoidThreeView, AutoTestCaseType.MechanoidThreeView, def => def?.race?.race?.IsMechanoid == true);
            AddTargetModPawnKindTargets(targets, targetMod, registerDiscoveredCase, AutoTestKind.AlienThreeView, AutoTestCaseType.AlienThreeView, def => IsAlienHumanlike(def?.race));
            AddTargetModHumanlikeLifeStageTargets(targets, targetMod, registerDiscoveredCase);
            AddTargetModApparelTargets(targets, targetMod, registerDiscoveredCase);
            AddTargetModWeaponTargets(targets, targetMod, excludedWeaponDefs, registerDiscoveredCase);
            AddTargetModHairTargets(targets, targetMod, registerDiscoveredCase);
            AddTargetModHeadTypeTargets(targets, targetMod, registerDiscoveredCase);
            AddTargetModGeneTargets(targets, targetMod, registerDiscoveredCase);
            AddTargetModXenotypeTargets(targets, targetMod, registerDiscoveredCase);

            return targets;
        }

        public bool TryResolveAppearancePresenter(AutoTestThreeViewTargetSpec spec, out string skipReason)
        {
            skipReason = null;
            if (spec == null || !IsAppearanceSpec(spec))
            {
                return true;
            }

            if (spec.TargetHairDef?.noGraphic == true)
            {
                skipReason = "no_graphic";
                return false;
            }

            List<PawnKindDef> candidates = spec.PresenterCandidates == null
                ? new List<PawnKindDef>()
                : spec.PresenterCandidates.Where(candidate => candidate != null).ToList();
            if (candidates.Count == 0 && spec.PawnKindDef != null)
            {
                candidates.Add(spec.PawnKindDef);
            }

            string lastReason = "no_compatible_presenter";
            foreach (PawnKindDef candidate in candidates)
            {
                foreach (Gender? gender in ResolveAppearanceProbeGenders(spec))
                {
                    Pawn pawn = null;
                    try
                    {
                        if (!TryGenerateProbePawn(candidate, out pawn) || pawn == null)
                        {
                            lastReason = "presenter_generation_failed:" + candidate.defName;
                            continue;
                        }

                        if (gender.HasValue)
                        {
                            pawn.gender = gender.Value;
                        }

                        pawn.apparel?.DestroyAll();
                        pawn.equipment?.DestroyAllEquipment();
                        RefreshPawnGraphics(pawn);
                        string before = BuildPawnVisualSignature(pawn);
                        if (!ApplyAppearanceTarget(pawn, spec, out string compatibilitySource, out string applyError))
                        {
                            lastReason = string.IsNullOrWhiteSpace(applyError) ? "appearance_apply_failed" : applyError;
                            continue;
                        }

                        string after = BuildPawnVisualSignature(pawn);
                        bool visualEffect = !string.Equals(before, after, StringComparison.Ordinal);
                        if (!visualEffect)
                        {
                            lastReason = "no_visual_effect:" + candidate.defName;
                            continue;
                        }

                        spec.PawnKindDef = candidate;
                        spec.RaceDef = candidate.race;
                        spec.ForcedGender = gender;
                        spec.PawnRaceDefName = candidate.race?.defName;
                        spec.PawnRaceLabel = candidate.race?.label ?? candidate.race?.defName ?? string.Empty;
                        spec.PawnBodyTypeDefName = pawn.story?.bodyType?.defName ?? string.Empty;
                        spec.PawnBodyTypeLabel = pawn.story?.bodyType?.label ?? pawn.story?.bodyType?.defName ?? string.Empty;
                        spec.Profile = ResolveThreeViewProfileForPawnKind(candidate);
                        spec.CompatibilitySource = AppendCompatibilitySource(spec.CompatibilitySource, compatibilitySource);
                        spec.BeforeVisualSignature = before;
                        spec.AfterVisualSignature = after;
                        spec.VisualEffectDetected = true;
                        spec.OverlayLine1 = string.IsNullOrWhiteSpace(spec.Label) ? spec.DefName : spec.Label;
                        spec.OverlayLine2 = spec.DefName + " / " + candidate.defName;
                        spec.OverlayLine3 = "Race: " + (candidate.race?.label ?? candidate.race?.defName ?? "Unknown") + " / Gender: " + pawn.gender;
                        spec.OverlayLine4 = "Compatibility: " + spec.CompatibilitySource;
                        return true;
                    }
                    catch (Exception ex)
                    {
                        lastReason = "presenter_probe_error:" + ex.Message;
                    }
                    finally
                    {
                        try
                        {
                            pawn?.Destroy(DestroyMode.Vanish);
                        }
                        catch
                        {
                        }
                    }
                }
            }

            skipReason = spec.TargetGeneDef != null || spec.TargetXenotypeDef != null
                ? "no_visual_effect"
                : lastReason;
            return false;
        }

        private static bool IsAppearanceSpec(AutoTestThreeViewTargetSpec spec)
        {
            return spec?.TargetHairDef != null ||
                spec?.TargetHeadTypeDef != null ||
                spec?.TargetGeneDef != null ||
                spec?.TargetXenotypeDef != null;
        }

        private static IEnumerable<Gender?> ResolveAppearanceProbeGenders(AutoTestThreeViewTargetSpec spec)
        {
            if (spec?.TargetHeadTypeDef != null && spec.TargetHeadTypeDef.gender != Gender.None)
            {
                return new Gender?[] { spec.TargetHeadTypeDef.gender };
            }

            string styleGender = spec?.TargetHairDef?.styleGender.ToString() ?? string.Empty;
            if (string.Equals(styleGender, "Male", StringComparison.OrdinalIgnoreCase))
            {
                return new Gender?[] { Gender.Male };
            }

            if (string.Equals(styleGender, "Female", StringComparison.OrdinalIgnoreCase))
            {
                return new Gender?[] { Gender.Female };
            }

            return new Gender?[] { null, Gender.Female, Gender.Male };
        }

        private static bool ApplyAppearanceTarget(
            Pawn pawn,
            AutoTestThreeViewTargetSpec spec,
            out string compatibilitySource,
            out string errorMessage)
        {
            compatibilitySource = "VanillaDynamicProbe";
            errorMessage = null;
            if (pawn == null || spec == null)
            {
                errorMessage = "appearance_target_missing";
                return false;
            }

            try
            {
                if (spec.TargetHairDef != null)
                {
                    HairDef hairDef = spec.TargetHairDef;
                    if (pawn.story == null || hairDef.noGraphic)
                    {
                        errorMessage = hairDef.noGraphic ? "no_graphic" : "presenter_story_missing";
                        return false;
                    }

                    if (hairDef.requiredMutant != null)
                    {
                        errorMessage = "required_mutant:" + hairDef.requiredMutant.defName;
                        return false;
                    }

                    if (hairDef.requiredGene != null)
                    {
                        if (pawn.genes == null)
                        {
                            errorMessage = "required_gene_without_gene_tracker:" + hairDef.requiredGene.defName;
                            return false;
                        }

                        if (pawn.genes.GetGene(hairDef.requiredGene) == null)
                        {
                            pawn.genes.AddGene(hairDef.requiredGene, xenogene: true);
                        }
                    }

                    if (!TryEvaluateHarStyleCompatibility(pawn, hairDef, out bool harCompatible, out string harSource) || !harCompatible)
                    {
                        errorMessage = "har_style_incompatible";
                        return false;
                    }

                    compatibilitySource = harSource;
                    pawn.story.hairDef = hairDef;
                }
                else if (spec.TargetHeadTypeDef != null)
                {
                    HeadTypeDef headTypeDef = spec.TargetHeadTypeDef;
                    if (pawn.story == null)
                    {
                        errorMessage = "presenter_story_missing";
                        return false;
                    }

                    if (headTypeDef.gender != Gender.None)
                    {
                        pawn.gender = headTypeDef.gender;
                    }

                    if (headTypeDef.requiredGenes != null && headTypeDef.requiredGenes.Count > 0)
                    {
                        if (pawn.genes == null)
                        {
                            errorMessage = "head_required_genes_without_tracker";
                            return false;
                        }

                        foreach (GeneDef requiredGene in headTypeDef.requiredGenes.Where(gene => gene != null))
                        {
                            if (pawn.genes.GetGene(requiredGene) == null)
                            {
                                pawn.genes.AddGene(requiredGene, xenogene: true);
                            }
                        }
                    }

                    if (!TryEvaluateHarHeadCompatibility(pawn, headTypeDef, out bool harCompatible, out string harSource) || !harCompatible)
                    {
                        errorMessage = "har_head_incompatible";
                        return false;
                    }

                    compatibilitySource = harSource;
                    pawn.story.headType = headTypeDef;
                }
                else if (spec.TargetGeneDef != null)
                {
                    if (pawn.genes == null)
                    {
                        errorMessage = "biotech_gene_tracker_missing";
                        return false;
                    }

                    pawn.genes.ClearXenogenes();
                    Gene existing = pawn.genes.GetGene(spec.TargetGeneDef);
                    if (existing != null)
                    {
                        pawn.genes.RemoveGene(existing);
                    }

                    pawn.genes.AddGene(spec.TargetGeneDef, xenogene: true);
                    compatibilitySource = "DynamicVisualSignature";
                }
                else if (spec.TargetXenotypeDef != null)
                {
                    if (pawn.genes == null)
                    {
                        errorMessage = "biotech_gene_tracker_missing";
                        return false;
                    }

                    foreach (Gene gene in pawn.genes.Xenogenes.ToList())
                    {
                        pawn.genes.RemoveGene(gene);
                    }

                    foreach (Gene gene in pawn.genes.Endogenes.ToList())
                    {
                        pawn.genes.RemoveGene(gene);
                    }

                    pawn.genes.SetXenotypeDirect(spec.TargetXenotypeDef);
                    foreach (GeneDef geneDef in (spec.TargetXenotypeDef.genes ?? new List<GeneDef>()).Where(gene => gene != null))
                    {
                        pawn.genes.AddGene(geneDef, xenogene: false);
                    }
                    compatibilitySource = "DynamicVisualSignature";
                }

                RefreshPawnGraphics(pawn);
                RefreshOptionalAlienGraphics(pawn);

                if (spec.TargetHairDef != null && pawn.story?.hairDef != spec.TargetHairDef)
                {
                    errorMessage = "hair_assignment_not_retained";
                    return false;
                }

                if (spec.TargetHeadTypeDef != null && pawn.story?.headType != spec.TargetHeadTypeDef)
                {
                    errorMessage = "head_assignment_not_retained";
                    return false;
                }

                if (spec.TargetGeneDef != null && pawn.genes?.GetGene(spec.TargetGeneDef) == null)
                {
                    errorMessage = "gene_assignment_not_retained";
                    return false;
                }

                if (spec.TargetXenotypeDef != null && pawn.genes?.Xenotype != spec.TargetXenotypeDef)
                {
                    errorMessage = "xenotype_assignment_not_retained";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public bool TryPopulateTriptych(Map map, IntVec3 anchorCell, AutoTestThreeViewTargetSpec spec, out AutoTestThreeViewTargetContext context, out string errorMessage)
        {
            context = null;
            errorMessage = null;
            if (map == null || spec?.PawnKindDef == null)
            {
                errorMessage = "ThreeView map or PawnKindDef was missing.";
                return false;
            }

            AutoTestThreeViewTargetContext createdContext = new AutoTestThreeViewTargetContext
            {
                Map = map,
                AnchorCell = anchorCell,
                Profile = spec.Profile
            };

            IntVec3[] cells =
            {
                anchorCell + new IntVec3(-2, 0, 0),
                anchorCell,
                anchorCell + new IntVec3(2, 0, 0)
            };
            Pawn sourcePawn = null;

            for (int i = 0; i < cells.Length; i++)
            {
                Pawn pawn;
                if (sourcePawn == null)
                {
                    if (!TryGeneratePawn(spec, map.Tile, out pawn, out string pawnError))
                    {
                        Cleanup(createdContext);
                        errorMessage = pawnError;
                        return false;
                    }

                    sourcePawn = pawn;
                }
                else if (!TryCreateReplicaPawn(spec, map.Tile, sourcePawn, out pawn, out string pawnError))
                {
                    Cleanup(createdContext);
                    errorMessage = pawnError;
                    return false;
                }

                IntVec3 cell = cells[i];
                Rot4 rotation = DefaultRotations[i];
                IntVec3 walkTarget = ResolveWalkTarget(cell, rotation);
                try
                {
                    GenSpawn.Spawn(pawn, cell, map, rotation, WipeMode.Vanish, false, false);
                    StabilizePawnForSettle(pawn, cell, rotation);
                    createdContext.Pawns.Add(pawn);
                    createdContext.Cells.Add(cell);
                    createdContext.Rotations.Add(rotation);
                    createdContext.WalkTargets.Add(walkTarget);
                }
                catch (Exception ex)
                {
                    try
                    {
                        pawn.Destroy(DestroyMode.Vanish);
                    }
                    catch
                    {
                    }

                    Cleanup(createdContext);
                    errorMessage = ex.Message;
                    return false;
                }
            }

            context = createdContext;
            if (sourcePawn?.ageTracker != null)
            {
                createdContext.ActualLifeStageIndex = sourcePawn.ageTracker.CurLifeStageIndex;
                createdContext.ActualLifeStage = sourcePawn.ageTracker.CurLifeStage;
                createdContext.ActualBiologicalAgeYears = sourcePawn.ageTracker.AgeBiologicalYearsFloat;
                createdContext.LifeStageLockFallbackUsed = spec.LifeStageLockFallbackUsed;
            }
            createdContext.VisualEffectDetected = spec.VisualEffectDetected;
            createdContext.TargetGenePresent = spec.TargetGeneDef == null || sourcePawn?.genes?.GetGene(spec.TargetGeneDef) != null;
            if (spec.TargetGeneDef != null && sourcePawn?.genes != null)
            {
                bool endogene = sourcePawn.genes.Endogenes.Any(gene => gene?.def == spec.TargetGeneDef);
                bool xenogene = sourcePawn.genes.Xenogenes.Any(gene => gene?.def == spec.TargetGeneDef);
                createdContext.TargetGeneLocation = endogene && xenogene
                    ? "Endogene+Xenogene"
                    : (endogene ? "Endogene" : (xenogene ? "Xenogene" : "Missing"));
            }
            createdContext.TargetXenotypeMatches = spec.TargetXenotypeDef == null || sourcePawn?.genes?.Xenotype == spec.TargetXenotypeDef;
            if (spec.TargetXenotypeDef != null && sourcePawn?.genes != null)
            {
                HashSet<GeneDef> expectedGenes = new HashSet<GeneDef>((spec.TargetXenotypeDef.genes ?? new List<GeneDef>()).Where(gene => gene != null));
                HashSet<GeneDef> actualGenes = new HashSet<GeneDef>(sourcePawn.genes.GenesListForReading.Select(gene => gene?.def).Where(gene => gene != null));
                createdContext.TargetXenotypeGeneSetMatches = expectedGenes.SetEquals(actualGenes);
            }
            return true;
        }

        public void PrepareForSettle(AutoTestThreeViewTargetContext context)
        {
            if (context == null)
            {
                return;
            }

            for (int i = 0; i < context.Pawns.Count; i++)
            {
                Pawn pawn = context.Pawns[i];
                if (pawn == null || pawn.Destroyed || !pawn.Spawned)
                {
                    continue;
                }

                StabilizePawnForSettle(pawn, context.Cells[i], context.Rotations[i]);
            }
        }

        public bool TryPrimeDirectionalWalk(AutoTestThreeViewTargetContext context, out string errorMessage)
        {
            errorMessage = null;
            if (context == null)
            {
                errorMessage = "ThreeView context was null.";
                return false;
            }

            try
            {
                for (int i = 0; i < context.Pawns.Count; i++)
                {
                    Pawn pawn = context.Pawns[i];
                    if (pawn == null || pawn.Destroyed || !pawn.Spawned)
                    {
                        errorMessage = "ThreeView pawn was missing before movement prime.";
                        return false;
                    }

                    IntVec3 origin = context.Cells[i];
                    IntVec3 walkTarget = i < context.WalkTargets.Count ? context.WalkTargets[i] : origin;
                    if (pawn.Position != origin)
                    {
                        pawn.Position = origin;
                    }

                    pawn.jobs?.StopAll();
                    pawn.pather?.StopDead();
                    pawn.Rotation = context.Rotations[i];
                    bool immobile = pawn.Downed || pawn.ageTracker?.CurLifeStage?.alwaysDowned == true;
                    if (!immobile)
                    {
                        pawn.rotationTracker?.FaceCell(walkTarget);
                        if (pawn.drafter != null)
                        {
                            pawn.drafter.Drafted = true;
                        }

                        if (walkTarget != origin)
                        {
                            pawn.pather?.StartPath(walkTarget, Verse.AI.PathEndMode.OnCell);
                        }
                    }
                }

                context.DirectionalWalkPrimed = true;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public bool TryPrimeDirectionalFacingForPausedCapture(AutoTestThreeViewTargetContext context, out string errorMessage)
        {
            errorMessage = null;
            if (context == null)
            {
                errorMessage = "ThreeView context was null.";
                return false;
            }

            try
            {
                for (int i = 0; i < context.Pawns.Count; i++)
                {
                    Pawn pawn = context.Pawns[i];
                    if (pawn == null || pawn.Destroyed || !pawn.Spawned)
                    {
                        errorMessage = "ThreeView pawn was missing before paused capture prime.";
                        return false;
                    }

                    IntVec3 origin = context.Cells[i];
                    IntVec3 walkTarget = i < context.WalkTargets.Count ? context.WalkTargets[i] : origin;
                    if (pawn.Position != origin)
                    {
                        pawn.Position = origin;
                    }

                    pawn.jobs?.StopAll();
                    pawn.pather?.StopDead();
                    pawn.Rotation = context.Rotations[i];
                    bool immobile = pawn.Downed || pawn.ageTracker?.CurLifeStage?.alwaysDowned == true;
                    if (!immobile)
                    {
                        if (pawn.drafter != null)
                        {
                            pawn.drafter.Drafted = true;
                        }

                        if (walkTarget != origin)
                        {
                            pawn.pather?.StartPath(walkTarget, Verse.AI.PathEndMode.OnCell);
                        }
                    }

                    pawn.Drawer?.renderer?.SetAllGraphicsDirty();
                }

                context.DirectionalWalkPrimed = true;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public void PrepareForCapture(AutoTestThreeViewTargetContext context)
        {
            if (context == null)
            {
                return;
            }

            for (int i = 0; i < context.Pawns.Count; i++)
            {
                Pawn pawn = context.Pawns[i];
                if (pawn == null || pawn.Destroyed || !pawn.Spawned)
                {
                    continue;
                }

                IntVec3 walkTarget = i < context.WalkTargets.Count ? context.WalkTargets[i] : context.Cells[i];
                try
                {
                    bool immobile = pawn.Downed || pawn.ageTracker?.CurLifeStage?.alwaysDowned == true;
                    if (immobile)
                    {
                        pawn.Rotation = context.Rotations[i];
                    }
                    else
                    {
                        pawn.rotationTracker?.FaceCell(walkTarget);
                        if (context.DirectionalWalkPrimed && walkTarget != pawn.Position && !(pawn.pather?.Moving ?? false))
                        {
                            pawn.pather?.StartPath(walkTarget, Verse.AI.PathEndMode.OnCell);
                        }
                    }

                    pawn.Drawer?.renderer?.SetAllGraphicsDirty();
                }
                catch
                {
                }
            }
        }

        public bool IsReadyForCapture(AutoTestThreeViewTargetContext context, out string details)
        {
            details = "ContextMissing";
            if (context == null || !context.DirectionalWalkPrimed)
            {
                return false;
            }

            int readyCount = 0;
            List<string> states = new List<string>();
            for (int i = 0; i < context.Pawns.Count; i++)
            {
                Pawn pawn = context.Pawns[i];
                if (pawn == null || pawn.Destroyed || !pawn.Spawned)
                {
                    states.Add("pawn" + i + "=missing");
                    continue;
                }

                IntVec3 walkTarget = i < context.WalkTargets.Count ? context.WalkTargets[i] : context.Cells[i];
                Rot4 desiredRotation = context.Rotations[i];
                try
                {
                    pawn.rotationTracker?.FaceCell(walkTarget);
                }
                catch
                {
                }

                bool rotationReady = pawn.Rotation == desiredRotation;
                bool movementReady = pawn.Downed || pawn.ageTracker?.CurLifeStage?.alwaysDowned == true || walkTarget == pawn.Position || (pawn.pather?.Moving ?? false) || (pawn.pather?.MovingNow ?? false);
                if (rotationReady && movementReady)
                {
                    readyCount++;
                }

                states.Add("pawn" + i + "=" + pawn.Rotation + "/" + (movementReady ? "moving" : "idle"));
            }

            details = string.Join(",", states);
            return readyCount == context.Pawns.Count;
        }

        public bool Focus(Map map, AutoTestThreeViewTargetContext context)
        {
            if (map == null || context == null)
            {
                return false;
            }

            try
            {
                CameraDriver cameraDriver = Find.CameraDriver;
                if (cameraDriver == null)
                {
                    return false;
                }

                float rootSize = context.Profile?.FixedRootSize ?? context.Profile?.MinRootSize ?? 6f;
                Vector3 anchor = context.AnchorCell.ToVector3Shifted();
                float cameraHeight = context.Profile?.CameraHeight ?? 65f;
                cameraDriver.SetRootPosAndSize(new Vector3(anchor.x, cameraHeight, anchor.z), rootSize);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Cleanup(AutoTestThreeViewTargetContext context)
        {
            if (context == null)
            {
                return;
            }

            foreach (Pawn pawn in context.Pawns)
            {
                if (pawn == null || pawn.Destroyed)
                {
                    continue;
                }

                try
                {
                    pawn.Destroy(DestroyMode.Vanish);
                }
                catch
                {
                }
            }

            context.Pawns.Clear();
            context.Cells.Clear();
            context.Rotations.Clear();
            context.WalkTargets.Clear();
            context.DirectionalWalkPrimed = false;
        }

        private static void AddIfResolved(
            ICollection<AutoTestThreeViewTargetSpec> targets,
            Func<AutoTestCaseType, string, string, string, AutoTestManifestEntry> registerDiscoveredCase,
            AutoTestKind kind,
            AutoTestCaseType caseType,
            PawnKindDef pawnKindDef,
            AutoTestScreenshotProfile profile)
        {
            if (targets == null || registerDiscoveredCase == null || pawnKindDef == null)
            {
                return;
            }

            targets.Add(new AutoTestThreeViewTargetSpec
            {
                Kind = kind,
                CaseType = caseType,
                ManifestEntry = registerDiscoveredCase(caseType, nameof(PawnKindDef), pawnKindDef.defName, pawnKindDef.label),
                TargetDef = pawnKindDef,
                PawnKindDef = pawnKindDef,
                RaceDef = pawnKindDef.race,
                PawnRaceDefName = pawnKindDef.race?.defName,
                PawnRaceLabel = pawnKindDef.race?.label ?? pawnKindDef.race?.defName ?? string.Empty,
                DefName = pawnKindDef.defName,
                Label = pawnKindDef.label,
                Profile = profile
            });
        }

        private static void AddTargetModPawnKindTargets(
            ICollection<AutoTestThreeViewTargetSpec> targets,
            ModContentPack targetMod,
            Func<AutoTestCaseType, string, string, string, AutoTestManifestEntry> registerDiscoveredCase,
            AutoTestKind kind,
            AutoTestCaseType caseType,
            Func<PawnKindDef, bool> predicate)
        {
            if (targets == null || targetMod == null || registerDiscoveredCase == null || predicate == null)
            {
                return;
            }

            foreach (PawnKindDef pawnKindDef in DefDatabase<PawnKindDef>.AllDefsListForReading
                .Where(def => def != null && def.modContentPack == targetMod && predicate(def))
                .OrderBy(def => def.defName))
            {
                AddIfResolved(
                    targets,
                    registerDiscoveredCase,
                    kind,
                    caseType,
                    pawnKindDef,
                    ResolveThreeViewProfileForPawnKind(pawnKindDef));
            }
        }

        private static void AddTargetModAnimalLifeStageTargets(
            ICollection<AutoTestThreeViewTargetSpec> targets,
            ModContentPack targetMod,
            Func<AutoTestCaseType, string, string, string, AutoTestManifestEntry> registerDiscoveredCase)
        {
            if (targets == null || targetMod == null || registerDiscoveredCase == null)
            {
                return;
            }

            foreach (PawnKindDef pawnKindDef in DefDatabase<PawnKindDef>.AllDefsListForReading
                .Where(def => def != null && def.modContentPack == targetMod && def.race?.race?.Animal == true)
                .OrderBy(def => def.defName))
            {
                List<LifeStageAge> stages = pawnKindDef.race?.race?.lifeStageAges;
                if (stages == null || stages.Count == 0)
                {
                    AddIfResolved(targets, registerDiscoveredCase, AutoTestKind.AnimalThreeView, AutoTestCaseType.AnimalThreeView, pawnKindDef, AutoTestScreenshotProfiles.ThreeViewAnimal);
                    continue;
                }

                HashSet<LifeStageDef> seenStages = new HashSet<LifeStageDef>();
                for (int index = 0; index < stages.Count; index++)
                {
                    LifeStageDef lifeStage = stages[index]?.def;
                    if (lifeStage == null || !seenStages.Add(lifeStage))
                    {
                        continue;
                    }

                    float ageYears = ResolveStableLifeStageAge(stages, index);
                    string stageLabel = ResolveLifeStageLabel(lifeStage);
                    string identity = (pawnKindDef.defName ?? "UnknownPawn") + "@" + lifeStage.defName;
                    targets.Add(new AutoTestThreeViewTargetSpec
                    {
                        Kind = AutoTestKind.AnimalThreeView,
                        CaseType = AutoTestCaseType.AnimalThreeView,
                        ManifestEntry = registerDiscoveredCase(AutoTestCaseType.AnimalThreeView, nameof(PawnKindDef), identity, pawnKindDef.label),
                        TargetDef = pawnKindDef,
                        PawnKindDef = pawnKindDef,
                        RaceDef = pawnKindDef.race,
                        TargetLifeStage = lifeStage,
                        TargetLifeStageIndex = index,
                        TargetBiologicalAgeYears = ageYears,
                        RelativeScreenshotSubdirectory = pawnKindDef.defName,
                        ScreenshotStageSuffix = lifeStage.defName + "_triptych",
                        PawnRaceDefName = pawnKindDef.race?.defName,
                        PawnRaceLabel = pawnKindDef.race?.label ?? pawnKindDef.race?.defName ?? string.Empty,
                        DefName = identity,
                        Label = pawnKindDef.label,
                        OverlayLine1 = pawnKindDef.label ?? pawnKindDef.defName,
                        OverlayLine2 = pawnKindDef.defName + " / " + (pawnKindDef.race?.defName ?? "UnknownRace"),
                        OverlayLine3 = "Stage: " + stageLabel + " (" + lifeStage.defName + ")",
                        OverlayLine4 = "Age: " + ageYears.ToString("0.###") + " / StageIndex=" + index,
                        Profile = AutoTestScreenshotProfiles.ThreeViewAnimal
                    });
                }
            }
        }

        private static void AddTargetModHumanlikeLifeStageTargets(
            ICollection<AutoTestThreeViewTargetSpec> targets,
            ModContentPack targetMod,
            Func<AutoTestCaseType, string, string, string, AutoTestManifestEntry> registerDiscoveredCase)
        {
            if (targets == null || targetMod == null || registerDiscoveredCase == null)
            {
                return;
            }

            HashSet<ThingDef> races = new HashSet<ThingDef>(
                DefDatabase<ThingDef>.AllDefsListForReading
                    .Where(def => def != null && def.modContentPack == targetMod && def.race?.Humanlike == true));
            foreach (PawnKindDef pawnKind in DefDatabase<PawnKindDef>.AllDefsListForReading
                .Where(def => def != null && def.modContentPack == targetMod && def.race?.race?.Humanlike == true))
            {
                races.Add(pawnKind.race);
            }

            foreach (ThingDef raceDef in races.Where(def => def != null).OrderBy(def => def.defName))
            {
                List<LifeStageAge> stages = raceDef.race?.lifeStageAges;
                PawnKindDef representative = ResolveLifeStageRepresentativePawnKind(raceDef, targetMod, stages?.Count ?? 0);
                if (representative == null || stages == null || stages.Count == 0)
                {
                    continue;
                }

                HashSet<LifeStageDef> seenStages = new HashSet<LifeStageDef>();
                for (int index = 0; index < stages.Count; index++)
                {
                    LifeStageDef lifeStage = stages[index]?.def;
                    if (lifeStage == null || !seenStages.Add(lifeStage))
                    {
                        continue;
                    }

                    float ageYears = ResolveStableLifeStageAge(stages, index);
                    string stageLabel = ResolveLifeStageLabel(lifeStage);
                    string identity = (raceDef.defName ?? "UnknownRace") + "@" + lifeStage.defName;
                    targets.Add(new AutoTestThreeViewTargetSpec
                    {
                        Kind = AutoTestKind.PawnLifeStageThreeView,
                        CaseType = AutoTestCaseType.PawnLifeStageThreeView,
                        ManifestEntry = registerDiscoveredCase(AutoTestCaseType.PawnLifeStageThreeView, nameof(ThingDef), identity, raceDef.label),
                        TargetDef = raceDef,
                        PawnKindDef = representative,
                        RaceDef = raceDef,
                        ForceStripLoadout = true,
                        TargetLifeStage = lifeStage,
                        TargetLifeStageIndex = index,
                        TargetBiologicalAgeYears = ageYears,
                        RelativeScreenshotSubdirectory = raceDef.defName,
                        ScreenshotStageSuffix = lifeStage.defName + "_triptych",
                        PawnRaceDefName = raceDef.defName,
                        PawnRaceLabel = raceDef.label ?? raceDef.defName,
                        DefName = identity,
                        Label = raceDef.label,
                        OverlayLine1 = raceDef.label ?? representative.label ?? raceDef.defName,
                        OverlayLine2 = raceDef.defName + " / " + representative.defName,
                        OverlayLine3 = "Stage: " + stageLabel + " (" + lifeStage.defName + ")",
                        OverlayLine4 = "Age: " + ageYears.ToString("0.###") + " / StageIndex=" + index,
                        Profile = IsAlienHumanlike(raceDef)
                            ? AutoTestScreenshotProfiles.ThreeViewAlien
                            : AutoTestScreenshotProfiles.ThreeViewHumanoid
                    });
                }
            }
        }

        private static PawnKindDef ResolveLifeStageRepresentativePawnKind(ThingDef raceDef, ModContentPack targetMod, int expectedStageCount)
        {
            return DefDatabase<PawnKindDef>.AllDefsListForReading
                .Where(def => def != null && def.race == raceDef)
                .OrderByDescending(def => def.modContentPack == targetMod)
                .ThenByDescending(def => def.lifeStages != null && def.lifeStages.Count >= expectedStageCount)
                .ThenByDescending(def => string.Equals(def.defName, "Colonist", StringComparison.OrdinalIgnoreCase))
                .ThenBy(def => def.defName)
                .FirstOrDefault();
        }

        private static float ResolveStableLifeStageAge(IReadOnlyList<LifeStageAge> stages, int index)
        {
            float minAge = Math.Max(0f, stages?[index]?.minAge ?? 0f);
            if (stages != null && index + 1 < stages.Count)
            {
                float nextAge = Math.Max(minAge, stages[index + 1]?.minAge ?? minAge);
                if (nextAge > minAge)
                {
                    return minAge + (nextAge - minAge) * 0.5f;
                }
            }

            return minAge + Math.Max(0.1f, minAge * 0.05f);
        }

        private static string ResolveLifeStageLabel(LifeStageDef lifeStage)
        {
            if (lifeStage == null)
            {
                return "UnknownStage";
            }

            if (!string.IsNullOrWhiteSpace(lifeStage.label))
            {
                return lifeStage.label;
            }

            if (!string.IsNullOrWhiteSpace(lifeStage.Adjective))
            {
                return lifeStage.Adjective;
            }

            return lifeStage.defName ?? "UnknownStage";
        }

        private static void AddTargetModApparelTargets(
            ICollection<AutoTestThreeViewTargetSpec> targets,
            ModContentPack targetMod,
            Func<AutoTestCaseType, string, string, string, AutoTestManifestEntry> registerDiscoveredCase)
        {
            if (targets == null || targetMod == null || registerDiscoveredCase == null)
            {
                return;
            }

            List<PawnKindDef> alienPawnKinds = ResolvePreferredAlienPawnKinds(targetMod);
            PawnKindDef humanPawnKind = ResolveDefaultHumanPawnKind();

            foreach (ThingDef apparelDef in DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => def != null && def.modContentPack == targetMod && def.IsApparel)
                .OrderBy(def => def.defName))
            {
                List<BodyTypeDef> explicitBodyTypes = ResolveSupportedApparelBodyTypes(apparelDef);
                if (explicitBodyTypes.Count == 0)
                {
                    explicitBodyTypes.Add(ResolveDeterministicBodyType(ResolvePreferredGenderForVariant(apparelDef, null)));
                }

                foreach (BodyTypeDef bodyType in explicitBodyTypes.Distinct())
                {
                    Gender? forcedGender = ResolvePreferredGenderForVariant(apparelDef, bodyType);
                    if (!TryResolvePresenterPawnKindForApparel(apparelDef, bodyType, forcedGender, humanPawnKind, alienPawnKinds, out PawnKindDef pawnKind))
                    {
                        continue;
                    }

                    string manifestDefName = BuildVariantManifestKey(apparelDef.defName, pawnKind?.defName, bodyType?.defName);
                    string displayBodyType = bodyType?.label ?? bodyType?.defName ?? "Default";
                    string label = string.IsNullOrWhiteSpace(apparelDef.label)
                        ? apparelDef.defName
                        : apparelDef.label + " [" + displayBodyType + "]";

                    targets.Add(new AutoTestThreeViewTargetSpec
                    {
                        Kind = AutoTestKind.ApparelThreeView,
                        CaseType = AutoTestCaseType.ApparelThreeView,
                        ManifestEntry = registerDiscoveredCase(AutoTestCaseType.ApparelThreeView, nameof(ThingDef), manifestDefName, label),
                        TargetDef = apparelDef,
                        PawnKindDef = pawnKind,
                        RaceDef = pawnKind?.race,
                        ItemDef = apparelDef,
                        ForcedBodyType = bodyType,
                        ForcedGender = forcedGender,
                        WearAsApparel = true,
                        HoldAsWeapon = false,
                        PawnRaceDefName = pawnKind?.race?.defName,
                        PawnRaceLabel = pawnKind?.race?.label ?? pawnKind?.race?.defName ?? string.Empty,
                        PawnBodyTypeDefName = bodyType?.defName ?? string.Empty,
                        PawnBodyTypeLabel = displayBodyType,
                        DefName = apparelDef.defName,
                        Label = apparelDef.label,
                        Profile = ResolveThreeViewProfileForPawnKind(pawnKind)
                    });
                }
            }
        }

        private static void AddTargetModWeaponTargets(
            ICollection<AutoTestThreeViewTargetSpec> targets,
            ModContentPack targetMod,
            ISet<ThingDef> excludedWeaponDefs,
            Func<AutoTestCaseType, string, string, string, AutoTestManifestEntry> registerDiscoveredCase)
        {
            if (targets == null || targetMod == null || registerDiscoveredCase == null)
            {
                return;
            }

            List<PawnKindDef> alienPawnKinds = ResolvePreferredAlienPawnKinds(targetMod);
            PawnKindDef humanPawnKind = ResolveDefaultHumanPawnKind();

            foreach (ThingDef weaponDef in DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def =>
                    def != null &&
                    def.modContentPack == targetMod &&
                    def.IsWeapon &&
                    !def.IsApparel &&
                    (excludedWeaponDefs == null || !excludedWeaponDefs.Contains(def)))
                .OrderBy(def => def.defName))
            {
                Gender? forcedGender = null;
                BodyTypeDef forcedBodyType = ResolveDeterministicBodyType(forcedGender);
                if (!TryResolvePresenterPawnKindForWeapon(weaponDef, forcedBodyType, forcedGender, humanPawnKind, alienPawnKinds, out PawnKindDef pawnKind))
                {
                    continue;
                }

                string manifestDefName = BuildVariantManifestKey(weaponDef.defName, pawnKind?.defName, forcedBodyType?.defName);
                targets.Add(new AutoTestThreeViewTargetSpec
                {
                    Kind = AutoTestKind.WeaponThreeView,
                    CaseType = AutoTestCaseType.WeaponThreeView,
                    ManifestEntry = registerDiscoveredCase(AutoTestCaseType.WeaponThreeView, nameof(ThingDef), manifestDefName, weaponDef.label),
                    TargetDef = weaponDef,
                    PawnKindDef = pawnKind,
                    RaceDef = pawnKind?.race,
                    ItemDef = weaponDef,
                    ForcedBodyType = forcedBodyType,
                    ForcedGender = forcedGender,
                    WearAsApparel = false,
                    HoldAsWeapon = true,
                    PawnRaceDefName = pawnKind?.race?.defName,
                    PawnRaceLabel = pawnKind?.race?.label ?? pawnKind?.race?.defName ?? string.Empty,
                    PawnBodyTypeDefName = forcedBodyType?.defName ?? string.Empty,
                    PawnBodyTypeLabel = forcedBodyType?.label ?? forcedBodyType?.defName ?? "Default",
                    DefName = weaponDef.defName,
                    Label = weaponDef.label,
                    Profile = ResolveThreeViewProfileForPawnKind(pawnKind)
                });
            }
        }

        private static void AddTargetModHairTargets(
            ICollection<AutoTestThreeViewTargetSpec> targets,
            ModContentPack targetMod,
            Func<AutoTestCaseType, string, string, string, AutoTestManifestEntry> registerDiscoveredCase)
        {
            List<PawnKindDef> candidates = ResolveAppearancePresenterCandidates(targetMod);
            foreach (HairDef hairDef in DefDatabase<HairDef>.AllDefsListForReading
                .Where(def => def != null && def.modContentPack == targetMod)
                .OrderBy(def => def.defName))
            {
                PawnKindDef presenter = candidates.FirstOrDefault();
                targets.Add(new AutoTestThreeViewTargetSpec
                {
                    Kind = AutoTestKind.HairThreeView,
                    CaseType = AutoTestCaseType.HairThreeView,
                    ManifestEntry = registerDiscoveredCase(AutoTestCaseType.HairThreeView, nameof(HairDef), hairDef.defName, hairDef.label),
                    TargetDef = hairDef,
                    TargetHairDef = hairDef,
                    PawnKindDef = presenter,
                    RaceDef = presenter?.race,
                    PresenterCandidates = candidates,
                    ForceStripLoadout = true,
                    DefName = hairDef.defName,
                    Label = hairDef.label,
                    ScreenshotStageSuffix = "triptych",
                    Profile = ResolveThreeViewProfileForPawnKind(presenter)
                });
            }
        }

        private static void AddTargetModHeadTypeTargets(
            ICollection<AutoTestThreeViewTargetSpec> targets,
            ModContentPack targetMod,
            Func<AutoTestCaseType, string, string, string, AutoTestManifestEntry> registerDiscoveredCase)
        {
            List<PawnKindDef> candidates = ResolveAppearancePresenterCandidates(targetMod);
            foreach (HeadTypeDef headTypeDef in DefDatabase<HeadTypeDef>.AllDefsListForReading
                .Where(def => def != null && def.modContentPack == targetMod)
                .OrderBy(def => def.defName))
            {
                PawnKindDef presenter = candidates.FirstOrDefault();
                targets.Add(new AutoTestThreeViewTargetSpec
                {
                    Kind = AutoTestKind.HeadTypeThreeView,
                    CaseType = AutoTestCaseType.HeadTypeThreeView,
                    ManifestEntry = registerDiscoveredCase(AutoTestCaseType.HeadTypeThreeView, nameof(HeadTypeDef), headTypeDef.defName, headTypeDef.label),
                    TargetDef = headTypeDef,
                    TargetHeadTypeDef = headTypeDef,
                    PawnKindDef = presenter,
                    RaceDef = presenter?.race,
                    PresenterCandidates = candidates,
                    ForceStripLoadout = true,
                    DefName = headTypeDef.defName,
                    Label = headTypeDef.label,
                    ScreenshotStageSuffix = "triptych",
                    Profile = ResolveThreeViewProfileForPawnKind(presenter)
                });
            }
        }

        private static void AddTargetModGeneTargets(
            ICollection<AutoTestThreeViewTargetSpec> targets,
            ModContentPack targetMod,
            Func<AutoTestCaseType, string, string, string, AutoTestManifestEntry> registerDiscoveredCase)
        {
            List<PawnKindDef> candidates = ResolveAppearancePresenterCandidates(targetMod);
            foreach (GeneDef geneDef in DefDatabase<GeneDef>.AllDefsListForReading
                .Where(def => def != null && def.modContentPack == targetMod)
                .OrderBy(def => def.defName))
            {
                PawnKindDef presenter = candidates.FirstOrDefault();
                targets.Add(new AutoTestThreeViewTargetSpec
                {
                    Kind = AutoTestKind.GeneThreeView,
                    CaseType = AutoTestCaseType.GeneThreeView,
                    ManifestEntry = registerDiscoveredCase(AutoTestCaseType.GeneThreeView, nameof(GeneDef), geneDef.defName, geneDef.label),
                    TargetDef = geneDef,
                    TargetGeneDef = geneDef,
                    PawnKindDef = presenter,
                    RaceDef = presenter?.race,
                    PresenterCandidates = candidates,
                    ForceStripLoadout = true,
                    CompatibilitySource = DescribeGeneStaticVisualHints(geneDef),
                    DefName = geneDef.defName,
                    Label = geneDef.label,
                    ScreenshotStageSuffix = "triptych",
                    Profile = ResolveThreeViewProfileForPawnKind(presenter)
                });
            }
        }

        private static void AddTargetModXenotypeTargets(
            ICollection<AutoTestThreeViewTargetSpec> targets,
            ModContentPack targetMod,
            Func<AutoTestCaseType, string, string, string, AutoTestManifestEntry> registerDiscoveredCase)
        {
            List<PawnKindDef> candidates = ResolveAppearancePresenterCandidates(targetMod);
            foreach (XenotypeDef xenotypeDef in DefDatabase<XenotypeDef>.AllDefsListForReading
                .Where(def => def != null && def.modContentPack == targetMod)
                .OrderBy(def => def.defName))
            {
                PawnKindDef presenter = candidates.FirstOrDefault();
                targets.Add(new AutoTestThreeViewTargetSpec
                {
                    Kind = AutoTestKind.XenotypeThreeView,
                    CaseType = AutoTestCaseType.XenotypeThreeView,
                    ManifestEntry = registerDiscoveredCase(AutoTestCaseType.XenotypeThreeView, nameof(XenotypeDef), xenotypeDef.defName, xenotypeDef.label),
                    TargetDef = xenotypeDef,
                    TargetXenotypeDef = xenotypeDef,
                    PawnKindDef = presenter,
                    RaceDef = presenter?.race,
                    PresenterCandidates = candidates,
                    ForceStripLoadout = true,
                    CompatibilitySource = "XenotypeGenes=" + (xenotypeDef.genes?.Count ?? 0),
                    DefName = xenotypeDef.defName,
                    Label = xenotypeDef.label,
                    ScreenshotStageSuffix = "triptych",
                    Profile = ResolveThreeViewProfileForPawnKind(presenter)
                });
            }
        }

        private static List<PawnKindDef> ResolveAppearancePresenterCandidates(ModContentPack targetMod)
        {
            List<PawnKindDef> candidates = DefDatabase<PawnKindDef>.AllDefsListForReading
                .Where(def => def != null && def.modContentPack == targetMod && def.race?.race?.Humanlike == true)
                .OrderBy(def => def.defName)
                .ToList();
            PawnKindDef human = ResolveDefaultHumanPawnKind();
            if (human != null)
            {
                candidates.Add(human);
            }

            return candidates
                .Where(def => def != null && def.race?.race?.Humanlike == true)
                .GroupBy(def => def.defName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private static string DescribeGeneStaticVisualHints(GeneDef geneDef)
        {
            if (geneDef == null)
            {
                return "StaticVisualHints=None";
            }

            List<string> hints = new List<string>();
            if (geneDef.HasDefinedGraphicProperties) hints.Add("DefinedGraphicProperties");
            if (geneDef.renderNodeProperties != null && geneDef.renderNodeProperties.Count > 0) hints.Add("RenderNodes");
            if (geneDef.hairColorOverride.HasValue) hints.Add("HairColor");
            if (geneDef.skinColorBase.HasValue || geneDef.skinColorOverride.HasValue) hints.Add("SkinColor");
            if (geneDef.bodyType.HasValue) hints.Add("BodyType");
            if (geneDef.forcedHeadTypes != null && geneDef.forcedHeadTypes.Count > 0) hints.Add("HeadType");
            if (geneDef.forcedHair != null) hints.Add("Hair");
            if (geneDef.hairTagFilter != null || geneDef.beardTagFilter != null) hints.Add("StyleFilter");
            if (geneDef.modExtensions != null && geneDef.modExtensions.Count > 0)
            {
                hints.AddRange(geneDef.modExtensions.Select(extension => extension?.GetType().FullName).Where(name => !string.IsNullOrWhiteSpace(name)));
            }

            return "StaticVisualHints=" + (hints.Count == 0 ? "None" : string.Join(",", hints.Distinct().ToArray()));
        }

        private static PawnKindDef ResolveDefaultHumanPawnKind()
        {
            return ResolvePawnKind(new[] { "Colonist" }, def => def?.race == ThingDefOf.Human);
        }

        private static List<PawnKindDef> ResolvePreferredAlienPawnKinds(ModContentPack targetMod)
        {
            return DefDatabase<PawnKindDef>.AllDefsListForReading
                .Where(def => def != null && def.modContentPack == targetMod && IsAlienHumanlike(def.race))
                .OrderBy(def => def.defName)
                .ToList();
        }

        private static AutoTestScreenshotProfile ResolveThreeViewProfileForPawnKind(PawnKindDef pawnKindDef)
        {
            if (pawnKindDef?.race?.race?.IsMechanoid == true)
            {
                return AutoTestScreenshotProfiles.ThreeViewMechanoid;
            }

            if (pawnKindDef?.race?.race?.Animal == true)
            {
                return AutoTestScreenshotProfiles.ThreeViewAnimal;
            }

            if (IsAlienHumanlike(pawnKindDef?.race))
            {
                return AutoTestScreenshotProfiles.ThreeViewAlien;
            }

            return AutoTestScreenshotProfiles.ThreeViewHumanoid;
        }

        private static string BuildVariantManifestKey(string defName, string pawnKindDefName, string bodyTypeDefName)
        {
            return (defName ?? "UnknownDef") + "@" + (pawnKindDefName ?? "UnknownPawn") + "@" + (bodyTypeDefName ?? "Default");
        }

        private static Gender? ResolvePreferredGenderForVariant(ThingDef apparelDef, BodyTypeDef bodyType)
        {
            if (bodyType != null)
            {
                if (string.Equals(bodyType.defName, "Female", StringComparison.OrdinalIgnoreCase))
                {
                    return Gender.Female;
                }

                if (string.Equals(bodyType.defName, "Male", StringComparison.OrdinalIgnoreCase))
                {
                    return Gender.Male;
                }
            }

            if (apparelDef?.apparel != null && apparelDef.apparel.gender != Gender.None)
            {
                return apparelDef.apparel.gender;
            }

            return null;
        }

        private static BodyTypeDef ResolveDeterministicBodyType(Gender? gender)
        {
            string preferredDefName = gender == Gender.Female ? "Female" : "Male";
            return DefDatabase<BodyTypeDef>.GetNamedSilentFail(preferredDefName)
                ?? DefDatabase<BodyTypeDef>.GetNamedSilentFail("Thin")
                ?? DefDatabase<BodyTypeDef>.AllDefsListForReading.OrderBy(def => def.defName).FirstOrDefault();
        }

        private static List<BodyTypeDef> ResolveSupportedApparelBodyTypes(ThingDef apparelDef)
        {
            HashSet<BodyTypeDef> bodyTypes = new HashSet<BodyTypeDef>();
            WornGraphicData wornGraphicData = apparelDef?.apparel?.wornGraphicData;
            if (wornGraphicData != null)
            {
                TryAddSupportedBodyType(bodyTypes, "Male", wornGraphicData.male);
                TryAddSupportedBodyType(bodyTypes, "Female", wornGraphicData.female);
                TryAddSupportedBodyType(bodyTypes, "Thin", wornGraphicData.thin);
                TryAddSupportedBodyType(bodyTypes, "Fat", wornGraphicData.fat);
                TryAddSupportedBodyType(bodyTypes, "Hulk", wornGraphicData.hulk);

                TryAddDirectionBodyTypes(bodyTypes, wornGraphicData.north);
                TryAddDirectionBodyTypes(bodyTypes, wornGraphicData.south);
                TryAddDirectionBodyTypes(bodyTypes, wornGraphicData.east);
                TryAddDirectionBodyTypes(bodyTypes, wornGraphicData.west);
            }

            foreach (BodyTypeDef inferredBodyType in ResolveTextureBackedApparelBodyTypes(apparelDef))
            {
                if (inferredBodyType != null)
                {
                    bodyTypes.Add(inferredBodyType);
                }
            }

            return bodyTypes
                .Where(def => def != null)
                .OrderBy(def => def.defName)
                .ToList();
        }

        private static IEnumerable<BodyTypeDef> ResolveTextureBackedApparelBodyTypes(ThingDef apparelDef)
        {
            string wornGraphicPath = apparelDef?.apparel?.wornGraphicPath;
            string modRootDirectory = apparelDef?.modContentPack?.RootDir;
            if (string.IsNullOrWhiteSpace(wornGraphicPath) || string.IsNullOrWhiteSpace(modRootDirectory))
            {
                return Enumerable.Empty<BodyTypeDef>();
            }

            try
            {
                string normalizedGraphicPath = wornGraphicPath
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar);
                string graphicDirectory = Path.GetDirectoryName(normalizedGraphicPath);
                string graphicBaseName = Path.GetFileName(normalizedGraphicPath);
                if (string.IsNullOrWhiteSpace(graphicBaseName))
                {
                    return Enumerable.Empty<BodyTypeDef>();
                }

                string texturesRoot = Path.Combine(modRootDirectory, "Textures");
                string searchDirectory = string.IsNullOrWhiteSpace(graphicDirectory)
                    ? texturesRoot
                    : Path.Combine(texturesRoot, graphicDirectory);
                if (!Directory.Exists(searchDirectory))
                {
                    return Enumerable.Empty<BodyTypeDef>();
                }

                HashSet<BodyTypeDef> inferredBodyTypes = new HashSet<BodyTypeDef>();
                foreach (string filePath in Directory.EnumerateFiles(searchDirectory, graphicBaseName + "*.*", SearchOption.TopDirectoryOnly))
                {
                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    if (!fileName.StartsWith(graphicBaseName + "_", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string suffix = fileName.Substring(graphicBaseName.Length + 1);
                    int separatorIndex = suffix.IndexOf('_');
                    string token = separatorIndex >= 0 ? suffix.Substring(0, separatorIndex) : suffix;
                    BodyTypeDef bodyType = ResolveBodyTypeByGraphicToken(token);
                    if (bodyType != null)
                    {
                        inferredBodyTypes.Add(bodyType);
                    }
                }

                return inferredBodyTypes
                    .Where(def => def != null)
                    .OrderBy(def => def.defName)
                    .ToList();
            }
            catch
            {
                return Enumerable.Empty<BodyTypeDef>();
            }
        }

        private static BodyTypeDef ResolveBodyTypeByGraphicToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            switch (token.Trim().ToLowerInvariant())
            {
                case "male":
                    return DefDatabase<BodyTypeDef>.GetNamedSilentFail("Male");
                case "female":
                    return DefDatabase<BodyTypeDef>.GetNamedSilentFail("Female");
                case "thin":
                    return DefDatabase<BodyTypeDef>.GetNamedSilentFail("Thin");
                case "fat":
                    return DefDatabase<BodyTypeDef>.GetNamedSilentFail("Fat");
                case "hulk":
                    return DefDatabase<BodyTypeDef>.GetNamedSilentFail("Hulk");
                default:
                    return null;
            }
        }

        private static void TryAddDirectionBodyTypes(ICollection<BodyTypeDef> bodyTypes, WornGraphicDirectionData directionData)
        {
            if (bodyTypes == null)
            {
                return;
            }

            TryAddSupportedBodyType(bodyTypes, "Male", directionData.male);
            TryAddSupportedBodyType(bodyTypes, "Female", directionData.female);
            TryAddSupportedBodyType(bodyTypes, "Thin", directionData.thin);
            TryAddSupportedBodyType(bodyTypes, "Fat", directionData.fat);
            TryAddSupportedBodyType(bodyTypes, "Hulk", directionData.hulk);
        }

        private static void TryAddSupportedBodyType(ICollection<BodyTypeDef> bodyTypes, string defName, object bodyTypeData)
        {
            if (bodyTypes == null || bodyTypeData == null)
            {
                return;
            }

            BodyTypeDef bodyTypeDef = DefDatabase<BodyTypeDef>.GetNamedSilentFail(defName);
            if (bodyTypeDef != null)
            {
                bodyTypes.Add(bodyTypeDef);
            }
        }

        private static bool TryResolvePresenterPawnKindForApparel(
            ThingDef apparelDef,
            BodyTypeDef bodyType,
            Gender? forcedGender,
            PawnKindDef humanPawnKind,
            IEnumerable<PawnKindDef> alienPawnKinds,
            out PawnKindDef pawnKind)
        {
            pawnKind = null;
            foreach (PawnKindDef alienPawnKind in alienPawnKinds ?? Enumerable.Empty<PawnKindDef>())
            {
                if (CanPawnKindWearApparel(alienPawnKind, apparelDef, bodyType, forcedGender))
                {
                    pawnKind = alienPawnKind;
                    return true;
                }
            }

            if (CanPawnKindWearApparel(humanPawnKind, apparelDef, bodyType, forcedGender))
            {
                pawnKind = humanPawnKind;
                return true;
            }

            return false;
        }

        private static bool TryResolvePresenterPawnKindForWeapon(
            ThingDef weaponDef,
            BodyTypeDef bodyType,
            Gender? forcedGender,
            PawnKindDef humanPawnKind,
            IEnumerable<PawnKindDef> alienPawnKinds,
            out PawnKindDef pawnKind)
        {
            pawnKind = null;
            foreach (PawnKindDef alienPawnKind in alienPawnKinds ?? Enumerable.Empty<PawnKindDef>())
            {
                if (CanPawnKindHoldWeapon(alienPawnKind, weaponDef, bodyType, forcedGender))
                {
                    pawnKind = alienPawnKind;
                    return true;
                }
            }

            if (CanPawnKindHoldWeapon(humanPawnKind, weaponDef, bodyType, forcedGender))
            {
                pawnKind = humanPawnKind;
                return true;
            }

            return false;
        }

        private static PawnKindDef ResolvePawnKind(IEnumerable<string> preferredDefNames, Func<PawnKindDef, bool> fallbackPredicate)
        {
            foreach (string preferredDefName in preferredDefNames ?? Enumerable.Empty<string>())
            {
                PawnKindDef preferred = DefDatabase<PawnKindDef>.GetNamedSilentFail(preferredDefName);
                if (preferred != null)
                {
                    return preferred;
                }
            }

            return DefDatabase<PawnKindDef>.AllDefsListForReading
                .Where(def => def != null && fallbackPredicate(def))
                .OrderBy(def => def.defName)
                .FirstOrDefault();
        }

        private static bool TryGeneratePawn(AutoTestThreeViewTargetSpec spec, int tile, out Pawn pawn, out string errorMessage)
        {
            pawn = null;
            errorMessage = null;
            if (spec?.PawnKindDef == null)
            {
                errorMessage = "PawnKindDef was null.";
                return false;
            }

            Faction faction = ResolveFaction(spec);
            Exception lastException = null;
            for (int attempt = 0; attempt < 6 && pawn == null; attempt++)
            {
                try
                {
                    PawnGenerationRequest request = new PawnGenerationRequest(
                        spec.PawnKindDef,
                        faction,
                        PawnGenerationContext.NonPlayer,
                        tile,
                        forceGenerateNewPawn: true,
                        allowDowned: spec.TargetLifeStage?.alwaysDowned == true,
                        canGeneratePawnRelations: false,
                        mustBeCapableOfViolence: false,
                        fixedBiologicalAge: spec.TargetLifeStage != null && spec.TargetBiologicalAgeYears >= 0f
                            ? spec.TargetBiologicalAgeYears
                            : (float?)null,
                        fixedChronologicalAge: spec.TargetLifeStage != null && spec.TargetBiologicalAgeYears >= 0f
                            ? spec.TargetBiologicalAgeYears
                            : (float?)null,
                        fixedGender: spec.ForcedGender,
                        developmentalStages: spec.TargetLifeStage?.developmentalStage ?? DevelopmentalStage.Adult,
                        dontGiveWeapon: spec.ForceStripLoadout,
                        forceNoGear: spec.ForceStripLoadout);
                    Pawn candidate = PawnGenerator.GeneratePawn(request);
                    if (candidate == null)
                    {
                        continue;
                    }

                    if (ShouldStripGeneratedPawnLoadout(spec, candidate))
                    {
                        try
                        {
                            candidate.apparel?.DestroyAll();
                            candidate.equipment?.DestroyAllEquipment();
                        }
                        catch
                        {
                        }
                    }

                    if (!PreparePawnForSpec(candidate, spec, out string prepareError))
                    {
                        try
                        {
                            candidate.Destroy(DestroyMode.Vanish);
                        }
                        catch
                        {
                        }

                        lastException = new InvalidOperationException(prepareError ?? "Failed to prepare pawn for three-view spec.");
                        continue;
                    }

                    pawn = candidate;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }
            }

            if (pawn != null)
            {
                return true;
            }

            errorMessage = lastException != null
                ? lastException.Message
                : "Pawn generation returned null.";
            return false;
        }

        private static Faction ResolveFaction(AutoTestThreeViewTargetSpec spec)
        {
            if (spec == null)
            {
                return null;
            }

            if (spec.Kind == AutoTestKind.MechanoidThreeView)
            {
                return Faction.OfMechanoids;
            }

            if (spec.RaceDef?.race?.Humanlike == true)
            {
                return Faction.OfPlayer ?? Find.FactionManager?.OfPlayer;
            }

            return null;
        }

        private static bool PreparePawnForSpec(Pawn pawn, AutoTestThreeViewTargetSpec spec, out string errorMessage)
        {
            errorMessage = null;
            if (pawn == null)
            {
                errorMessage = "Preview pawn was null.";
                return false;
            }

            try
            {
                if (spec?.ForcedGender.HasValue == true)
                {
                    pawn.gender = spec.ForcedGender.Value;
                }

                if (pawn.story != null && spec?.ForcedBodyType != null)
                {
                    pawn.story.bodyType = spec.ForcedBodyType;
                }

                if (!EnsureTargetLifeStage(pawn, spec, out bool usedLifeStageLock, out string lifeStageError))
                {
                    errorMessage = lifeStageError;
                    return false;
                }

                if (usedLifeStageLock)
                {
                    spec.LifeStageLockFallbackUsed = true;
                }

                if (IsAppearanceSpec(spec))
                {
                    if (!ApplyAppearanceTarget(pawn, spec, out string compatibilitySource, out string appearanceError))
                    {
                        errorMessage = appearanceError ?? "Failed to apply appearance target.";
                        return false;
                    }

                    spec.CompatibilitySource = AppendCompatibilitySource(spec.CompatibilitySource, compatibilitySource);
                }

                if (ShouldStripGeneratedPawnLoadout(spec, pawn))
                {
                    pawn.apparel?.DestroyAll();
                    pawn.equipment?.DestroyAllEquipment();
                }

                if (spec?.WearAsApparel == true && spec.ItemDef != null)
                {
                    Apparel apparel = ThingMaker.MakeThing(spec.ItemDef, ResolveStuff(spec.ItemDef)) as Apparel;
                    if (apparel == null)
                    {
                        errorMessage = "Thing could not be created as apparel: " + spec.ItemDef.defName;
                        return false;
                    }

                    if (!apparel.PawnCanWear(pawn))
                    {
                        apparel.Destroy(DestroyMode.Vanish);
                        errorMessage = "Pawn cannot wear apparel: " + spec.ItemDef.defName;
                        return false;
                    }

                    if (pawn.apparel != null && !pawn.apparel.CanWearWithoutDroppingAnything(apparel.def))
                    {
                        pawn.apparel.DestroyAll();
                    }

                    pawn.apparel?.Wear(apparel, false, false);
                }

                if (spec?.HoldAsWeapon == true && spec.ItemDef != null)
                {
                    ThingWithComps equippedThing = ThingMaker.MakeThing(spec.ItemDef, ResolveStuff(spec.ItemDef)) as ThingWithComps;
                    if (equippedThing == null)
                    {
                        errorMessage = "Thing could not be created as equippable weapon: " + spec.ItemDef.defName;
                        return false;
                    }

                    pawn.equipment?.AddEquipment(equippedThing);
                    if (pawn.equipment?.Primary != equippedThing)
                    {
                        errorMessage = "Pawn failed to hold weapon: " + spec.ItemDef.defName;
                        return false;
                    }
                }

                RefreshPawnGraphics(pawn);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static bool CanPawnKindWearApparel(PawnKindDef pawnKindDef, ThingDef apparelDef, BodyTypeDef bodyType, Gender? forcedGender)
        {
            if (pawnKindDef == null || apparelDef == null || !apparelDef.IsApparel)
            {
                return false;
            }

            Pawn pawn = null;
            Apparel apparel = null;
            try
            {
                if (!TryGenerateProbePawn(pawnKindDef, out pawn))
                {
                    return false;
                }

                if (forcedGender.HasValue)
                {
                    pawn.gender = forcedGender.Value;
                }

                if (bodyType != null && pawn.story != null)
                {
                    pawn.story.bodyType = bodyType;
                }

                apparel = ThingMaker.MakeThing(apparelDef, ResolveStuff(apparelDef)) as Apparel;
                if (apparel == null)
                {
                    return false;
                }

                return apparel.PawnCanWear(pawn) && (pawn.apparel == null || pawn.apparel.CanWearWithoutDroppingAnything(apparel.def));
            }
            catch
            {
                return false;
            }
            finally
            {
                try
                {
                    apparel?.Destroy(DestroyMode.Vanish);
                }
                catch
                {
                }

                try
                {
                    pawn?.Destroy(DestroyMode.Vanish);
                }
                catch
                {
                }
            }
        }

        private static bool CanPawnKindHoldWeapon(PawnKindDef pawnKindDef, ThingDef weaponDef, BodyTypeDef bodyType, Gender? forcedGender)
        {
            if (pawnKindDef == null || weaponDef == null || !weaponDef.IsWeapon)
            {
                return false;
            }

            Pawn pawn = null;
            ThingWithComps weapon = null;
            try
            {
                if (!TryGenerateProbePawn(pawnKindDef, out pawn))
                {
                    return false;
                }

                if (forcedGender.HasValue)
                {
                    pawn.gender = forcedGender.Value;
                }

                if (bodyType != null && pawn.story != null)
                {
                    pawn.story.bodyType = bodyType;
                }

                pawn.equipment?.DestroyAllEquipment();
                weapon = ThingMaker.MakeThing(weaponDef, ResolveStuff(weaponDef)) as ThingWithComps;
                if (weapon == null)
                {
                    return false;
                }

                pawn.equipment?.AddEquipment(weapon);
                return pawn.equipment?.Primary == weapon;
            }
            catch
            {
                return false;
            }
            finally
            {
                try
                {
                    weapon?.Destroy(DestroyMode.Vanish);
                }
                catch
                {
                }

                try
                {
                    pawn?.Destroy(DestroyMode.Vanish);
                }
                catch
                {
                }
            }
        }

        private static bool TryGenerateProbePawn(PawnKindDef pawnKindDef, out Pawn pawn)
        {
            pawn = null;
            if (pawnKindDef == null)
            {
                return false;
            }

            try
            {
                PawnGenerationRequest request = new PawnGenerationRequest(
                    pawnKindDef,
                    ResolveFaction(new AutoTestThreeViewTargetSpec
                    {
                        PawnKindDef = pawnKindDef,
                        RaceDef = pawnKindDef.race,
                        Kind = pawnKindDef.race?.race?.IsMechanoid == true
                            ? AutoTestKind.MechanoidThreeView
                            : AutoTestKind.HumanoidThreeView
                    }),
                    PawnGenerationContext.NonPlayer,
                    -1,
                    forceGenerateNewPawn: true,
                    canGeneratePawnRelations: false,
                    mustBeCapableOfViolence: false);

                pawn = PawnGenerator.GeneratePawn(request);
                if (pawn?.RaceProps?.Humanlike == true)
                {
                    pawn.apparel?.DestroyAll();
                    pawn.equipment?.DestroyAllEquipment();
                }

                return pawn != null;
            }
            catch
            {
                pawn = null;
                return false;
            }
        }

        private static bool ShouldStripGeneratedPawnLoadout(AutoTestThreeViewTargetSpec spec, Pawn pawn)
        {
            if (pawn?.RaceProps?.Humanlike != true)
            {
                return false;
            }

            if (spec == null)
            {
                return true;
            }

            if (spec.ForceStripLoadout)
            {
                return true;
            }

            if (spec.WearAsApparel || spec.HoldAsWeapon)
            {
                return true;
            }

            switch (spec.Kind)
            {
                case AutoTestKind.HumanoidThreeView:
                case AutoTestKind.AlienThreeView:
                    return false;
                default:
                    return true;
            }
        }

        private static bool EnsureTargetLifeStage(
            Pawn pawn,
            AutoTestThreeViewTargetSpec spec,
            out bool usedLockFallback,
            out string errorMessage)
        {
            usedLockFallback = false;
            errorMessage = null;
            if (spec?.TargetLifeStage == null || spec.TargetLifeStageIndex < 0)
            {
                return true;
            }

            if (pawn?.ageTracker == null)
            {
                errorMessage = "Generated pawn did not have an age tracker for target stage " + spec.TargetLifeStage.defName + ".";
                return false;
            }

            if (pawn.ageTracker.CurLifeStageIndex == spec.TargetLifeStageIndex && pawn.ageTracker.CurLifeStage == spec.TargetLifeStage)
            {
                return true;
            }

            if (spec.TargetBiologicalAgeYears >= 0f)
            {
                long targetTicks = Math.Max(0L, (long)(spec.TargetBiologicalAgeYears * 3600000f));
                pawn.ageTracker.AgeBiologicalTicks = targetTicks;
                pawn.ageTracker.AgeChronologicalTicks = Math.Max(pawn.ageTracker.AgeChronologicalTicks, targetTicks);
                RecalculateLifeStageIndexMethod?.Invoke(pawn.ageTracker, null);
            }

            if (pawn.ageTracker.CurLifeStageIndex != spec.TargetLifeStageIndex || pawn.ageTracker.CurLifeStage != spec.TargetLifeStage)
            {
                pawn.ageTracker.LockCurrentLifeStageIndex(spec.TargetLifeStageIndex);
                usedLockFallback = true;
            }

            if (pawn.ageTracker.CurLifeStageIndex != spec.TargetLifeStageIndex || pawn.ageTracker.CurLifeStage != spec.TargetLifeStage)
            {
                errorMessage =
                    "Generated pawn life stage mismatch. expected=" + spec.TargetLifeStage.defName +
                    "/" + spec.TargetLifeStageIndex +
                    " actual=" + (pawn.ageTracker.CurLifeStage?.defName ?? "null") +
                    "/" + pawn.ageTracker.CurLifeStageIndex + ".";
                return false;
            }

            return true;
        }

        private static ThingDef ResolveStuff(ThingDef def)
        {
            if (def == null || !def.MadeFromStuff)
            {
                return null;
            }

            if (def.defaultStuff != null)
            {
                return def.defaultStuff;
            }

            if (def.stuffCategories == null || def.stuffCategories.Count == 0)
            {
                return null;
            }

            return DefDatabase<ThingDef>.AllDefsListForReading
                .Where(candidate =>
                    candidate != null &&
                    candidate.IsStuff &&
                    candidate.stuffProps?.categories != null &&
                    candidate.stuffProps.categories.Any(category => def.stuffCategories.Contains(category)))
                .OrderBy(candidate => candidate.defName)
                .FirstOrDefault();
        }

        private static bool TryCreateReplicaPawn(AutoTestThreeViewTargetSpec spec, int tile, Pawn sourcePawn, out Pawn pawn, out string errorMessage)
        {
            pawn = null;
            errorMessage = null;
            if (sourcePawn == null)
            {
                errorMessage = "Source pawn was null.";
                return false;
            }

            if (!TryGeneratePawn(spec, tile, out pawn, out errorMessage))
            {
                return false;
            }

            try
            {
                AutoTestThreeViewAppearanceTemplate template = CaptureAppearanceTemplate(sourcePawn);
                ApplyAppearanceTemplate(pawn, template);
                if (!PreparePawnForSpec(pawn, spec, out errorMessage))
                {
                    throw new InvalidOperationException(errorMessage ?? "Failed to prepare replica pawn for three-view spec.");
                }

                RefreshPawnGraphics(pawn);
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    pawn?.Destroy(DestroyMode.Vanish);
                }
                catch
                {
                }

                pawn = null;
                errorMessage = ex.Message;
                return false;
            }
        }

        private static void StabilizePawnForSettle(Pawn pawn, IntVec3 cell, Rot4 rotation)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return;
            }

            try
            {
                if (pawn.Spawned)
                {
                    pawn.Position = cell;
                }

                pawn.jobs?.StopAll();
                pawn.pather?.StopDead();
                pawn.Rotation = rotation;
                bool immobile = pawn.Downed || pawn.ageTracker?.CurLifeStage?.alwaysDowned == true;
                if (!immobile)
                {
                    pawn.rotationTracker?.FaceCell(cell + rotation.FacingCell);
                    if (pawn.drafter != null)
                    {
                        pawn.drafter.Drafted = true;
                    }
                }
            }
            catch
            {
            }
        }

        private static bool IsAlienHumanlike(ThingDef raceDef)
        {
            if (raceDef == null || raceDef == ThingDefOf.Human)
            {
                return false;
            }

            if (raceDef.modContentPack == null)
            {
                return false;
            }

            if (!(raceDef.race?.Humanlike ?? false))
            {
                return false;
            }

            string packageId = raceDef.modContentPack.PackageIdPlayerFacing ?? string.Empty;
            if (packageId.StartsWith("ludeon.", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static AutoTestThreeViewAppearanceTemplate CaptureAppearanceTemplate(Pawn pawn)
        {
            AutoTestThreeViewAppearanceTemplate template = new AutoTestThreeViewAppearanceTemplate
            {
                Gender = pawn.gender,
                BiologicalAgeTicks = pawn.ageTracker?.AgeBiologicalTicks ?? 0L,
                ChronologicalAgeTicks = pawn.ageTracker?.AgeChronologicalTicks ?? 0L,
                Name = CloneName(pawn?.Name)
            };

            if (pawn?.story != null)
            {
                template.BodyType = pawn.story.bodyType;
                template.HeadType = pawn.story.headType;
                template.HairDef = pawn.story.hairDef;
                template.HairColor = pawn.story.HairColor;
                template.Melanin = ReadStoryMelanin(pawn.story);
                template.SkinColorBase = pawn.story.SkinColorBase;
                template.SkinColorOverride = pawn.story.skinColorOverride;
            }

            if (pawn?.style != null)
            {
                template.BeardDef = pawn.style.beardDef;
                template.FaceTattoo = pawn.style.FaceTattoo;
                template.BodyTattoo = pawn.style.BodyTattoo;
            }

            if (pawn?.genes != null)
            {
                template.Xenotype = pawn.genes.Xenotype;
                template.Endogenes.AddRange(pawn.genes.Endogenes.Select(gene => gene?.def).Where(def => def != null));
                template.Xenogenes.AddRange(pawn.genes.Xenogenes.Select(gene => gene?.def).Where(def => def != null));
            }

            return template;
        }

        private static void ApplyAppearanceTemplate(Pawn pawn, AutoTestThreeViewAppearanceTemplate template)
        {
            if (pawn == null || template == null)
            {
                return;
            }

            try
            {
                pawn.gender = template.Gender;
                if (pawn.ageTracker != null)
                {
                    pawn.ageTracker.AgeBiologicalTicks = template.BiologicalAgeTicks;
                    pawn.ageTracker.AgeChronologicalTicks = template.ChronologicalAgeTicks;
                }

                if (pawn.story != null)
                {
                    if (template.BodyType != null)
                    {
                        pawn.story.bodyType = template.BodyType;
                    }

                    if (template.HairDef != null)
                    {
                        pawn.story.hairDef = template.HairDef;
                    }

                    if (template.HeadType != null)
                    {
                        pawn.story.headType = template.HeadType;
                    }

                    pawn.story.HairColor = template.HairColor;
                    WriteStoryMelanin(pawn.story, template.Melanin);
                    if (template.SkinColorBase.HasValue)
                    {
                        pawn.story.SkinColorBase = template.SkinColorBase.Value;
                    }

                    pawn.story.skinColorOverride = template.SkinColorOverride;
                }

                if (pawn.style != null)
                {
                    pawn.style.beardDef = template.BeardDef;
                    pawn.style.FaceTattoo = template.FaceTattoo;
                    pawn.style.BodyTattoo = template.BodyTattoo;
                    pawn.style.Notify_StyleItemChanged();
                }

                if (pawn.genes != null)
                {
                    SyncGenes(pawn, template);
                }

                if (template.Name != null)
                {
                    pawn.Name = template.Name;
                }
            }
            catch
            {
            }
        }

        private static void SyncGenes(Pawn pawn, AutoTestThreeViewAppearanceTemplate template)
        {
            if (pawn?.genes == null || template == null)
            {
                return;
            }

            if (template.Xenotype != null)
            {
                pawn.genes.SetXenotypeDirect(template.Xenotype);
            }

            foreach (Gene gene in pawn.genes.Xenogenes.ToList())
            {
                pawn.genes.RemoveGene(gene);
            }

            foreach (GeneDef geneDef in template.Xenogenes)
            {
                if (pawn.genes.GetGene(geneDef) == null)
                {
                    pawn.genes.AddGene(geneDef, xenogene: true);
                }
            }

            foreach (Gene gene in pawn.genes.Endogenes.ToList())
            {
                if (!template.Endogenes.Contains(gene.def))
                {
                    pawn.genes.RemoveGene(gene);
                }
            }

            foreach (GeneDef geneDef in template.Endogenes)
            {
                if (pawn.genes.GetGene(geneDef) == null)
                {
                    pawn.genes.AddGene(geneDef, xenogene: false);
                }
            }
        }

        private static Name CloneName(Name sourceName)
        {
            if (sourceName is NameSingle single)
            {
                return new NameSingle(single.Name);
            }

            if (sourceName is NameTriple triple)
            {
                return new NameTriple(triple.First, triple.Nick, triple.Last);
            }

            return sourceName;
        }

        private static bool TryEvaluateHarStyleCompatibility(
            Pawn pawn,
            HairDef hairDef,
            out bool compatible,
            out string source)
        {
            compatible = true;
            source = "VanillaStyleRules";
            if (pawn?.def == null || hairDef == null || !string.Equals(pawn.def.GetType().FullName, "AlienRace.ThingDef_AlienRace", StringComparison.Ordinal))
            {
                return true;
            }

            source = "HARFallbackDynamic";
            try
            {
                object alienSettings = ReadMemberValue(pawn.def, "alienRace");
                object styleSettingsMap = ReadMemberValue(alienSettings, "styleSettings");
                if (!(styleSettingsMap is IDictionary dictionary) || !dictionary.Contains(typeof(HairDef)))
                {
                    return true;
                }

                object styleSettings = dictionary[typeof(HairDef)];
                MethodInfo method = styleSettings?.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(candidate => string.Equals(candidate.Name, "IsValidStyle", StringComparison.Ordinal))
                    .OrderBy(candidate => candidate.GetParameters().Length)
                    .FirstOrDefault();
                if (method == null)
                {
                    return true;
                }

                ParameterInfo[] parameters = method.GetParameters();
                object[] arguments = parameters.Length >= 3
                    ? new object[] { hairDef, pawn, false }
                    : new object[] { hairDef, pawn };
                object result = method.Invoke(styleSettings, arguments);
                if (result is bool valid)
                {
                    compatible = valid;
                    source = "HAR.StyleSettings.IsValidStyle";
                }

                return true;
            }
            catch
            {
                compatible = true;
                source = "HARFallbackDynamic";
                return true;
            }
        }

        private static bool TryEvaluateHarHeadCompatibility(
            Pawn pawn,
            HeadTypeDef headTypeDef,
            out bool compatible,
            out string source)
        {
            compatible = true;
            source = "VanillaHeadRules";
            if (pawn?.def == null || headTypeDef == null || !string.Equals(pawn.def.GetType().FullName, "AlienRace.ThingDef_AlienRace", StringComparison.Ordinal))
            {
                return true;
            }

            source = "HARDynamicHeadProbe";
            try
            {
                object alienSettings = ReadMemberValue(pawn.def, "alienRace");
                object generalSettings = ReadMemberValue(alienSettings, "generalSettings");
                object partGenerator = ReadMemberValue(generalSettings, "alienPartGenerator") ?? ReadMemberValue(alienSettings, "alienPartGenerator");
                object headTypes = ReadMemberValue(partGenerator, "HeadTypes") ?? ReadMemberValue(partGenerator, "headTypes");
                if (headTypes is IEnumerable enumerable)
                {
                    List<HeadTypeDef> allowed = enumerable.Cast<object>().OfType<HeadTypeDef>().ToList();
                    if (allowed.Count > 0)
                    {
                        compatible = allowed.Contains(headTypeDef);
                        source = "HAR.AlienPartGenerator.HeadTypes";
                    }
                }

                return true;
            }
            catch
            {
                compatible = true;
                source = "HARDynamicHeadProbe";
                return true;
            }
        }

        private static object ReadMemberValue(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
            {
                return null;
            }

            Type type = instance.GetType();
            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                return field.GetValue(instance);
            }

            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                return property.GetValue(instance, null);
            }

            return null;
        }

        private static string BuildPawnVisualSignature(Pawn pawn)
        {
            if (pawn == null)
            {
                return "pawn:null";
            }

            RefreshPawnGraphics(pawn);
            RefreshOptionalAlienGraphics(pawn);
            StringBuilder builder = new StringBuilder();
            builder.Append("race=").Append(pawn.def?.defName ?? "null");
            builder.Append("|gender=").Append(pawn.gender);
            if (pawn.story != null)
            {
                builder.Append("|body=").Append(pawn.story.bodyType?.defName ?? "null");
                builder.Append("|head=").Append(pawn.story.headType?.defName ?? "null");
                builder.Append("|hair=").Append(pawn.story.hairDef?.defName ?? "null");
                builder.Append("|hairColor=").Append(pawn.story.HairColor);
                builder.Append("|skinBase=").Append(pawn.story.SkinColorBase);
                builder.Append("|skinOverride=").Append(pawn.story.skinColorOverride?.ToString() ?? "null");
            }

            PawnRenderer renderer = pawn.Drawer?.renderer;
            try
            {
                AppendGraphicSignature(builder, "bodyGraphic", renderer?.BodyGraphic);
                AppendGraphicSignature(builder, "headGraphic", renderer?.HeadGraphic);
            }
            catch
            {
            }

            try
            {
                object renderTree = ReadMemberValue(renderer, "renderTree");
                object rootNode = ReadMemberValue(renderTree, "rootNode");
                Queue<object> queue = new Queue<object>();
                HashSet<object> visited = new HashSet<object>();
                if (rootNode != null)
                {
                    queue.Enqueue(rootNode);
                }

                int index = 0;
                while (queue.Count > 0 && index < 256)
                {
                    object node = queue.Dequeue();
                    if (node == null || !visited.Add(node))
                    {
                        continue;
                    }

                    builder.Append("|node").Append(index).Append('=').Append(node.GetType().FullName);
                    object props = ReadMemberValue(node, "Props") ?? ReadMemberValue(node, "props");
                    builder.Append(':').Append(props?.GetType().FullName ?? "null");
                    AppendGraphicSignature(builder, "primary", ReadMemberValue(node, "PrimaryGraphic") as Graphic ?? ReadMemberValue(node, "primaryGraphic") as Graphic);
                    object graphics = ReadMemberValue(node, "Graphics") ?? ReadMemberValue(node, "graphics");
                    if (graphics is IEnumerable graphicEnumerable)
                    {
                        int graphicIndex = 0;
                        foreach (Graphic graphic in graphicEnumerable.Cast<object>().OfType<Graphic>())
                        {
                            AppendGraphicSignature(builder, "graphic" + graphicIndex++, graphic);
                        }
                    }

                    object children = ReadMemberValue(node, "children");
                    if (children is IEnumerable childEnumerable)
                    {
                        foreach (object child in childEnumerable)
                        {
                            if (child != null)
                            {
                                queue.Enqueue(child);
                            }
                        }
                    }

                    index++;
                }
            }
            catch
            {
            }

            return builder.ToString();
        }

        private static void AppendGraphicSignature(StringBuilder builder, string key, Graphic graphic)
        {
            if (builder == null)
            {
                return;
            }

            if (graphic == null)
            {
                builder.Append('|').Append(key).Append("=null");
                return;
            }

            string path = ReadMemberValue(graphic, "path") as string ?? string.Empty;
            string maskPath = ReadMemberValue(graphic, "maskPath") as string ?? string.Empty;
            builder.Append('|').Append(key).Append('=').Append(graphic.GetType().FullName)
                .Append(':').Append(path)
                .Append(':').Append(maskPath)
                .Append(':').Append(graphic.Color)
                .Append(':').Append(graphic.ColorTwo);
        }

        private static void RefreshOptionalAlienGraphics(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            try
            {
                foreach (ThingComp comp in pawn.AllComps ?? new List<ThingComp>())
                {
                    if (comp == null || !string.Equals(comp.GetType().FullName, "AlienRace.AlienPartGenerator+AlienComp", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (string methodName in new[] { "RegenerateAddonsForced", "RegenerateColorChannelLinks", "UpdateColors" })
                    {
                        MethodInfo method = comp.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                        method?.Invoke(comp, null);
                    }
                }
            }
            catch
            {
            }

            try
            {
                Type patches = AccessTools.TypeByName("AlienRace.AlienRenderTreePatches");
                MethodInfo regenerate = patches?.GetMethod("RegenerateResolveData", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Pawn) }, null);
                regenerate?.Invoke(null, new object[] { pawn });
            }
            catch
            {
            }
        }

        private static string AppendCompatibilitySource(string existing, string next)
        {
            if (string.IsNullOrWhiteSpace(existing))
            {
                return next ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(next) || existing.IndexOf(next, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return existing;
            }

            return existing + ";" + next;
        }

        private static void RefreshPawnGraphics(Pawn pawn)
        {
            try
            {
                pawn?.style?.Notify_StyleItemChanged();
            }
            catch
            {
            }

            try
            {
                pawn?.Drawer?.renderer?.SetAllGraphicsDirty();
            }
            catch
            {
            }
        }

        private static IntVec3 ResolveWalkTarget(IntVec3 cell, Rot4 rotation)
        {
            return cell + rotation.FacingCell;
        }

        private static float ReadStoryMelanin(Pawn_StoryTracker story)
        {
            if (story == null || StoryMelaninField == null)
            {
                return 0f;
            }

            try
            {
                return (float)StoryMelaninField.GetValue(story);
            }
            catch
            {
                return 0f;
            }
        }

        private static void WriteStoryMelanin(Pawn_StoryTracker story, float melanin)
        {
            if (story == null || StoryMelaninField == null)
            {
                return;
            }

            try
            {
                StoryMelaninField.SetValue(story, melanin);
            }
            catch
            {
            }
        }
    }
}
