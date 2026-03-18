using System;
using System.Collections.Generic;
using System.Linq;
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
    }

    internal sealed class AutoTestThreeViewAppearanceTemplate
    {
        public BodyTypeDef BodyType;
        public HairDef HairDef;
        public string NameText;
    }

    internal sealed class AutoTestThreeViewService
    {
        private static readonly Rot4[] DefaultRotations = { Rot4.West, Rot4.South, Rot4.North };

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
            AutoTestThreeViewAppearanceTemplate appearanceTemplate = null;

            for (int i = 0; i < cells.Length; i++)
            {
                Pawn pawn;
                if (!TryGeneratePawn(spec, map.Tile, out pawn, out string pawnError))
                {
                    Cleanup(createdContext);
                    errorMessage = pawnError;
                    return false;
                }

                IntVec3 cell = cells[i];
                Rot4 rotation = DefaultRotations[i];
                try
                {
                    if (appearanceTemplate == null)
                    {
                        appearanceTemplate = CaptureAppearanceTemplate(pawn, spec);
                        ApplyAppearanceTemplate(pawn, appearanceTemplate);
                    }
                    else
                    {
                        ApplyAppearanceTemplate(pawn, appearanceTemplate);
                    }

                    GenSpawn.Spawn(pawn, cell, map, rotation, WipeMode.Vanish, false, false);
                    StabilizePawn(pawn, cell, rotation);
                    createdContext.Pawns.Add(pawn);
                    createdContext.Cells.Add(cell);
                    createdContext.Rotations.Add(rotation);
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

                StabilizePawn(pawn, context.Cells[i], context.Rotations[i]);
            }
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

        private static void StabilizePawn(Pawn pawn, IntVec3 cell, Rot4 rotation)
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

        private static AutoTestThreeViewAppearanceTemplate CaptureAppearanceTemplate(Pawn pawn, AutoTestThreeViewTargetSpec spec)
        {
            AutoTestThreeViewAppearanceTemplate template = new AutoTestThreeViewAppearanceTemplate
            {
                NameText = spec?.DefName ?? "ThreeView"
            };

            if (pawn?.story != null)
            {
                template.BodyType = pawn.story.bodyType;
                template.HairDef = DefDatabase<HairDef>.GetNamedSilentFail("Shaved") ?? pawn.story.hairDef;
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

                }

                if (!string.IsNullOrWhiteSpace(template.NameText))
                {
                    pawn.Name = new NameSingle(template.NameText);
                }
            }
            catch
            {
            }
        }
    }
}
