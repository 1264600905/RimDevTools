using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        public PawnKindDef PawnKindDef;
        public ThingDef RaceDef;
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

        public List<AutoTestThreeViewTargetSpec> ResolveDefaultTargets(Func<AutoTestCaseType, string, string, string, AutoTestManifestEntry> registerDiscoveredCase)
        {
            List<AutoTestThreeViewTargetSpec> targets = new List<AutoTestThreeViewTargetSpec>();
            if (registerDiscoveredCase == null)
            {
                return targets;
            }

            AddIfResolved(
                targets,
                registerDiscoveredCase,
                AutoTestKind.HumanoidThreeView,
                AutoTestCaseType.HumanoidThreeView,
                ResolvePawnKind(new[] { "Colonist" }, def => def?.race == ThingDefOf.Human),
                AutoTestScreenshotProfiles.ThreeViewHumanoid);

            AddIfResolved(
                targets,
                registerDiscoveredCase,
                AutoTestKind.AnimalThreeView,
                AutoTestCaseType.AnimalThreeView,
                ResolvePawnKind(new[] { "Capybara", "Muffalo", "LabradorRetriever", "Gazelle" }, def => def?.race?.race?.Animal == true),
                AutoTestScreenshotProfiles.ThreeViewAnimal);

            AddIfResolved(
                targets,
                registerDiscoveredCase,
                AutoTestKind.MechanoidThreeView,
                AutoTestCaseType.MechanoidThreeView,
                ResolvePawnKind(new[] { "Mech_Scyther", "Mech_Lancer", "Mech_Pikeman", "Mech_Termite" }, def => def?.race?.race?.IsMechanoid == true),
                AutoTestScreenshotProfiles.ThreeViewMechanoid);

            AddIfResolved(
                targets,
                registerDiscoveredCase,
                AutoTestKind.AlienThreeView,
                AutoTestCaseType.AlienThreeView,
                ResolvePawnKind(new[] { "Axolotl_Colonist", "Axolotl_PlayerFaction", "Axolotl_Refugee" }, def => IsAlienHumanlike(def?.race)),
                AutoTestScreenshotProfiles.ThreeViewAlien);

            return targets;
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
                    pawn.rotationTracker?.FaceCell(walkTarget);
                    pawn.Rotation = context.Rotations[i];
                    if (pawn.drafter != null)
                    {
                        pawn.drafter.Drafted = true;
                    }

                    if (walkTarget != origin)
                    {
                        pawn.pather?.StartPath(walkTarget, Verse.AI.PathEndMode.OnCell);
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
                    pawn.rotationTracker?.FaceCell(walkTarget);
                    if (context.DirectionalWalkPrimed && walkTarget != pawn.Position && !(pawn.pather?.Moving ?? false))
                    {
                        pawn.pather?.StartPath(walkTarget, Verse.AI.PathEndMode.OnCell);
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
                bool movementReady = walkTarget == pawn.Position || (pawn.pather?.Moving ?? false) || (pawn.pather?.MovingNow ?? false);
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
                PawnKindDef = pawnKindDef,
                RaceDef = pawnKindDef.race,
                DefName = pawnKindDef.defName,
                Label = pawnKindDef.label,
                Profile = profile
            });
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
                        canGeneratePawnRelations: false,
                        mustBeCapableOfViolence: false);
                    Pawn candidate = PawnGenerator.GeneratePawn(request);
                    if (candidate == null)
                    {
                        continue;
                    }

                    if (candidate.RaceProps?.Humanlike == true)
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
                pawn.rotationTracker?.FaceCell(cell + rotation.FacingCell);
                pawn.Rotation = rotation;
                if (pawn.drafter != null)
                {
                    pawn.drafter.Drafted = true;
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
