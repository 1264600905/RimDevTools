using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal enum StaticViewTestPhase
    {
        Initializing,
        PreparingTarget,
        PausingForCapture,
        CleaningUp,
        Done
    }

    internal sealed class AutoTestStaticViewTargetSpec
    {
        public AutoTestKind Kind;
        public AutoTestCaseType CaseType;
        public AutoTestManifestEntry ManifestEntry;
        public Def TargetDef;
        public string DefName;
        public string Label;
        public string StageSuffix;
        public string RelativeScreenshotSubdirectory;
        public AutoTestScreenshotProfile Profile;
    }

    internal sealed partial class AutoTestRunner
    {
        private sealed class StaticViewCellSnapshot
        {
            public IntVec3 Cell;
            public TerrainDef Terrain;
            public RoofDef Roof;
            public bool Fogged;
            public bool Polluted;
        }

        private sealed class StaticViewThingSnapshot
        {
            public Thing Thing;
            public IntVec3 Position;
            public Rot4 Rotation;
        }

        private sealed class StaticViewContext
        {
            public Map Map;
            public IntVec3 RootCell;
            public IntVec3 AnchorCell;
            public AutoTestScreenshotProfile Profile;
            public readonly List<StaticViewCellSnapshot> Cells = new List<StaticViewCellSnapshot>();
            public readonly List<StaticViewThingSnapshot> DisplacedThings = new List<StaticViewThingSnapshot>();
            public readonly List<Thing> CreatedThings = new List<Thing>();
            public int AppliedCellCount;
            public int ReadbackCellCount;
            public bool TargetRemoved;
            public bool TerrainRestored;
            public bool RoofRestored;
            public bool FogRestored;
            public bool PollutionRestored;
            public bool DisplacedThingsRestored;
        }

        private sealed class StaticViewTestSession
        {
            private const int StudioWidth = 21;
            private const int StudioHeight = 21;
            private const float PausedCaptureSettleSeconds = 1f;
            private const int CaptureWindowTailTicks = 3;

            private readonly AutoTestRunner owner;

            public AutoTestStaticViewTargetSpec Spec;
            public AutoTestResult Result;
            public StaticViewContext Context;
            public bool AllowScreenshots;
            public int WaitTicks;
            public StaticViewTestPhase Phase;
            public TimeSpeed ResumeSpeed;
            public bool PauseBypassArmed;
            public float PausedCaptureStartRealtime;
            public bool PauseUiCaptureStarted;

            public StaticViewTestSession(AutoTestRunner owner)
            {
                this.owner = owner;
            }

            public void Update()
            {
                try
                {
                    switch (Phase)
                    {
                        case StaticViewTestPhase.PreparingTarget:
                            PrepareTarget();
                            break;
                        case StaticViewTestPhase.PausingForCapture:
                            PauseForCapture();
                            break;
                        case StaticViewTestPhase.CleaningUp:
                            Cleanup();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Fail("unexpected_error", ex.ToString());
                }
            }

            private void PrepareTarget()
            {
                if (Context?.Map == null || !Find.Maps.Contains(Context.Map))
                {
                    Fail("map_missing", "Static view map was missing before setup.");
                    return;
                }

                Result.Stage = "prepare_target";
                switch (Spec.Kind)
                {
                    case AutoTestKind.PlantGrowthView:
                        owner.PreparePlantGrowthView(this);
                        break;
                    case AutoTestKind.BuildingView:
                        owner.PrepareBuildingView(this);
                        break;
                    case AutoTestKind.TerrainPatchView:
                        owner.PrepareTerrainPatchView(this);
                        break;
                    default:
                        throw new InvalidOperationException("Unsupported static view kind: " + Spec.Kind);
                }

                owner.FocusStaticView(Context);
                Phase = StaticViewTestPhase.PausingForCapture;
            }

            private void PauseForCapture()
            {
                owner.PrepareStaticViewForCapture(Context);
                owner.FocusStaticView(Context);
                TickManager tickManager = Find.TickManager;
                if (tickManager == null)
                {
                    Fail("pause_failed", "TickManager missing before static view capture.");
                    return;
                }

                if (!PauseBypassArmed)
                {
                    ResumeSpeed = tickManager.CurTimeSpeed;
                    AutoTestBootstrap.SetTemporaryPauseBypass(true);
                    PauseBypassArmed = true;
                }

                tickManager.CurTimeSpeed = TimeSpeed.Paused;
                PausedCaptureStartRealtime = Time.realtimeSinceStartup;
                PauseUiCaptureStarted = true;
                Result.Notes = AppendNote(Result.Notes, "ViewPausedCaptureSettleSeconds=" + PausedCaptureSettleSeconds.ToString("0.0"));
                owner.activeStaticViewUiSession = this;
                Phase = StaticViewTestPhase.CleaningUp;
            }

            public bool ProcessUiFrame()
            {
                if (!PauseUiCaptureStarted)
                {
                    return true;
                }

                owner.PrepareStaticViewForCapture(Context);
                owner.FocusStaticView(Context);
                TickManager tickManager = Find.TickManager;
                if (tickManager == null)
                {
                    FailFromUi("pause_failed", "TickManager missing during static view capture.");
                    return true;
                }

                tickManager.CurTimeSpeed = TimeSpeed.Paused;
                if (Time.realtimeSinceStartup - PausedCaptureStartRealtime < PausedCaptureSettleSeconds)
                {
                    return false;
                }

                if (!AutoTestScreenshotService.PrepareCaptureFrame())
                {
                    return false;
                }

                Result.Stage = "capture";
                bool captureRequested = AllowScreenshots;
                bool captured = owner.TryCaptureStaticViewScreenshot(this, false);
                RestoreTimeAfterCapture();
                if (captureRequested && !captured)
                {
                    FailFromUi("capture_failed", "Static view screenshot was requested but not captured.");
                    return true;
                }

                Result.Status = "passed";
                WaitTicks = CaptureWindowTailTicks;
                PauseUiCaptureStarted = false;
                return true;
            }

            private void Cleanup()
            {
                if (PauseUiCaptureStarted)
                {
                    return;
                }

                if (WaitTicks > 0)
                {
                    owner.PrepareStaticViewForCapture(Context);
                    WaitTicks--;
                    return;
                }

                bool restored = owner.RestoreStaticViewContext(Context, Result);
                owner.RefreshThreeViewCooldown("static_view_cleanup");
                if (!restored && Result.Status == "passed")
                {
                    Result.Status = "failed";
                    Result.Stage = "cleanup";
                    Result.ErrorMessage = "Static view map state was not restored exactly.";
                }

                Phase = StaticViewTestPhase.Done;
            }

            private void Fail(string stage, string message)
            {
                RestoreTimeAfterCapture();
                Result.Status = "failed";
                Result.Stage = stage;
                Result.ErrorMessage = message ?? string.Empty;
                owner.TryCaptureStaticViewScreenshot(this, true);
                PauseUiCaptureStarted = false;
                Phase = StaticViewTestPhase.CleaningUp;
            }

            public void FailFromUi(string stage, string message)
            {
                Fail(stage, message);
            }

            private void RestoreTimeAfterCapture()
            {
                if (!PauseBypassArmed)
                {
                    return;
                }

                try
                {
                    if (Find.TickManager != null)
                    {
                        Find.TickManager.CurTimeSpeed = ResumeSpeed;
                    }
                }
                catch
                {
                }
                finally
                {
                    AutoTestBootstrap.SetTemporaryPauseBypass(false);
                    PauseBypassArmed = false;
                }
            }

            public static StaticViewTestSession Create(
                AutoTestRunner owner,
                AutoTestStaticViewTargetSpec spec,
                AutoTestResult result,
                bool allowScreenshots)
            {
                StaticViewTestSession session = new StaticViewTestSession(owner)
                {
                    Spec = spec,
                    Result = result,
                    AllowScreenshots = allowScreenshots,
                    Phase = StaticViewTestPhase.Initializing
                };

                if (!owner.TryAllocateStaticViewRoot(StudioWidth, StudioHeight, out IntVec3 rootCell))
                {
                    result.Status = "skipped";
                    result.Stage = "layout";
                    result.ErrorMessage = "No restorable 21x21 view studio area remained on the current map.";
                    session.Phase = StaticViewTestPhase.Done;
                    return session;
                }

                StaticViewContext context = new StaticViewContext
                {
                    Map = owner.map,
                    RootCell = rootCell,
                    AnchorCell = new IntVec3(rootCell.x + 10, 0, rootCell.z + 10),
                    Profile = spec.Profile
                };
                if (!owner.TrySnapshotAndClearStaticViewArea(context, StudioWidth, StudioHeight, out string errorMessage))
                {
                    result.Status = "failed";
                    result.Stage = "snapshot";
                    result.ErrorMessage = errorMessage ?? "Static view studio snapshot failed.";
                    owner.RestoreStaticViewContext(context, result);
                    session.Phase = StaticViewTestPhase.Done;
                    return session;
                }

                session.Context = context;
                session.Phase = StaticViewTestPhase.PreparingTarget;
                return session;
            }
        }

        private void BeginStaticViewTestSession()
        {
            foreach (ThingDef plantDef in DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => def != null && def.modContentPack == targetMod && def.plant != null && def.thingClass != null && MatchesSpecFilters(AutoTestKind.PlantGrowthView, def))
                .OrderBy(def => def.defName))
            {
                pendingStaticViewTargets.Enqueue(CreateStaticViewSpec(AutoTestKind.PlantGrowthView, AutoTestCaseType.PlantGrowthView, plantDef, AutoTestScreenshotProfiles.ThreeViewHumanoid, "growth_stages"));
            }

            foreach (ThingDef buildingDef in DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => def != null && def.modContentPack == targetMod && def.category == ThingCategory.Building && def.thingClass != null && !def.IsBlueprint && !def.IsFrame && MatchesSpecFilters(AutoTestKind.BuildingView, def))
                .OrderBy(def => def.defName))
            {
                pendingStaticViewTargets.Enqueue(CreateStaticViewSpec(AutoTestKind.BuildingView, AutoTestCaseType.BuildingView, buildingDef, AutoTestScreenshotProfiles.ThreeViewHumanoid, "view"));
            }

            foreach (TerrainDef terrainDef in DefDatabase<TerrainDef>.AllDefsListForReading
                .Where(def => def != null && def.modContentPack == targetMod && MatchesSpecFilters(AutoTestKind.TerrainPatchView, def))
                .OrderBy(def => def.defName))
            {
                pendingStaticViewTargets.Enqueue(CreateStaticViewSpec(AutoTestKind.TerrainPatchView, AutoTestCaseType.TerrainPatchView, terrainDef, AutoTestScreenshotProfiles.ThreeViewHumanoid, "9x9"));
            }
        }

        private AutoTestStaticViewTargetSpec CreateStaticViewSpec(
            AutoTestKind kind,
            AutoTestCaseType caseType,
            Def targetDef,
            AutoTestScreenshotProfile profile,
            string stageSuffix)
        {
            return new AutoTestStaticViewTargetSpec
            {
                Kind = kind,
                CaseType = caseType,
                ManifestEntry = RegisterDiscoveredCase(caseType, targetDef?.GetType().Name ?? nameof(Def), targetDef?.defName, targetDef?.label),
                TargetDef = targetDef,
                DefName = targetDef?.defName,
                Label = targetDef?.label,
                StageSuffix = stageSuffix,
                RelativeScreenshotSubdirectory = targetDef?.defName,
                Profile = profile
            };
        }

        private void StartNewStaticViewSession(AutoTestStaticViewTargetSpec spec)
        {
            if (spec == null)
            {
                return;
            }

            AutoTestResult result = CreateResult(spec.ManifestEntry, spec.TargetDef?.GetType().Name ?? nameof(Def), spec.DefName, spec.Label);
            result.Notes = AppendNote(result.Notes, "AutoTestMode=" + (settings.UseXmlDrivenAutoTest() ? "XmlDriven" : "Automatic"));
            result.Notes = AppendNote(result.Notes, "ViewProfile=" + (spec.Profile?.ProfileId ?? "Unknown"));
            LogSyncAutoTestSpecDef matchedSpec = ResolveBestMatchingSpec(spec.Kind, spec.TargetDef);
            StaticViewTestSession session = StaticViewTestSession.Create(this, spec, result, ShouldCaptureScreenshots(matchedSpec));
            if (session.Phase == StaticViewTestPhase.Done)
            {
                RecordResult(result);
                return;
            }

            activeStaticViewSessions.Add(session);
        }

        private bool TryAllocateStaticViewRoot(int width, int height, out IntVec3 rootCell)
        {
            int maxX = Math.Max(10, map.Size.x - width - 10);
            int maxZ = Math.Max(10, map.Size.z - height - 10);
            for (int z = 10; z <= maxZ; z += 2)
            {
                for (int x = 10; x <= maxX; x += 2)
                {
                    IntVec3 candidate = new IntVec3(x, 0, z);
                    if (IsStaticViewAreaRestorable(candidate, width, height))
                    {
                        rootCell = candidate;
                        return true;
                    }
                }
            }

            rootCell = IntVec3.Invalid;
            return false;
        }

        private bool IsStaticViewAreaRestorable(IntVec3 rootCell, int width, int height)
        {
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    IntVec3 cell = new IntVec3(rootCell.x + x, 0, rootCell.z + z);
                    if (!cell.InBounds(map))
                    {
                        return false;
                    }

                    foreach (Thing thing in cell.GetThingList(map))
                    {
                        if (thing == null || thing.Destroyed)
                        {
                            continue;
                        }

                        if (thing is Pawn || !(thing.def?.destroyable ?? false))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private bool TrySnapshotAndClearStaticViewArea(StaticViewContext context, int width, int height, out string errorMessage)
        {
            errorMessage = null;
            if (context?.Map == null)
            {
                errorMessage = "Static view context map was null.";
                return false;
            }

            try
            {
                HashSet<Thing> displaced = new HashSet<Thing>();
                for (int x = 0; x < width; x++)
                {
                    for (int z = 0; z < height; z++)
                    {
                        IntVec3 cell = new IntVec3(context.RootCell.x + x, 0, context.RootCell.z + z);
                        context.Cells.Add(new StaticViewCellSnapshot
                        {
                            Cell = cell,
                            Terrain = context.Map.terrainGrid.TerrainAt(cell),
                            Roof = context.Map.roofGrid.RoofAt(cell),
                            Fogged = cell.Fogged(context.Map),
                            Polluted = context.Map.pollutionGrid != null && context.Map.pollutionGrid.IsPolluted(cell)
                        });

                        foreach (Thing thing in cell.GetThingList(context.Map))
                        {
                            if (thing != null && !thing.Destroyed && thing.Spawned)
                            {
                                displaced.Add(thing);
                            }
                        }
                    }
                }

                foreach (Thing thing in displaced)
                {
                    context.DisplacedThings.Add(new StaticViewThingSnapshot
                    {
                        Thing = thing,
                        Position = thing.Position,
                        Rotation = thing.Rotation
                    });
                    thing.DeSpawn(DestroyMode.Vanish);
                }

                foreach (StaticViewCellSnapshot snapshot in context.Cells)
                {
                    context.Map.roofGrid.SetRoof(snapshot.Cell, null);
                    context.Map.fogGrid?.Unfog(snapshot.Cell);
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private void PreparePlantGrowthView(StaticViewTestSession session)
        {
            ThingDef plantDef = session.Spec.TargetDef as ThingDef;
            if (plantDef?.plant == null)
            {
                throw new InvalidOperationException("PlantGrowthView target was not a plant ThingDef.");
            }

            TerrainDef terrain = ResolveCompatiblePlantViewTerrain(plantDef);
            if (terrain == null)
            {
                throw new InvalidOperationException("No terrain with sufficient fertility was available for " + plantDef.defName + ".");
            }

            float harvestMinGrowth = plantDef.plant.harvestMinGrowth;
            float growingGrowth = harvestMinGrowth > 0.02f
                ? Mathf.Clamp(harvestMinGrowth * 0.5f, 0.01f, Math.Min(0.99f, harvestMinGrowth - 0.001f))
                : 0.5f;
            float[] growthValues = { 0.01f, growingGrowth, 1f };
            PlantLifeStage[] expectedStages = { PlantLifeStage.Growing, PlantLifeStage.Growing, PlantLifeStage.Mature };
            string[] names = { "Sowing", "Growing", "Mature" };
            IntVec3[] cells =
            {
                session.Context.AnchorCell + new IntVec3(-2, 0, 0),
                session.Context.AnchorCell,
                session.Context.AnchorCell + new IntVec3(2, 0, 0)
            };

            for (int i = 0; i < cells.Length; i++)
            {
                IntVec3 cell = cells[i];
                session.Context.Map.terrainGrid.SetTerrain(cell, terrain);
                if (session.Context.Map.pollutionGrid != null && (plantDef.plant.RequiresPollution || plantDef.plant.RequiresNoPollution))
                {
                    session.Context.Map.pollutionGrid.SetPolluted(cell, plantDef.plant.RequiresPollution, silent: true);
                }

                Plant plant = ThingMaker.MakeThing(plantDef) as Plant;
                if (plant == null)
                {
                    throw new InvalidOperationException("ThingMaker did not create a Plant for " + plantDef.defName + ".");
                }

                plant.HitPoints = plant.MaxHitPoints;
                plant.sown = true;
                plant.Growth = growthValues[i];
                GenSpawn.Spawn(plant, cell, session.Context.Map, WipeMode.Vanish);
                plant.HitPoints = plant.MaxHitPoints;
                plant.Growth = growthValues[i];
                plant.DirtyMapMesh(session.Context.Map);
                session.Context.Map.mapDrawer.MapMeshDirty(cell, MapMeshFlagDefOf.Things);
                session.Context.CreatedThings.Add(plant);

                session.Result.Notes = AppendNote(session.Result.Notes,
                    names[i] + ":Growth=" + plant.Growth.ToString("0.####") + ",PlantLifeStage=" + plant.LifeStage);
                if (plant.LifeStage != expectedStages[i])
                {
                    throw new InvalidOperationException(
                        names[i] + " plant stage mismatch. expected=" + expectedStages[i] + " actual=" + plant.LifeStage + ".");
                }
            }

            session.Result.Label = plantDef.label ?? plantDef.defName;
            session.Result.OverlayLine1 = session.Result.Label;
            session.Result.OverlayLine2 = plantDef.defName;
            session.Result.OverlayLine3 = "Sowing / Growing / Mature";
            session.Result.OverlayLine4 = "Growth: " + string.Join(" / ", growthValues.Select(value => value.ToString("0.###")).ToArray());
            session.Result.Notes = AppendNote(session.Result.Notes, "harvestMinGrowth=" + harvestMinGrowth.ToString("0.####"));
            session.Result.Notes = AppendNote(session.Result.Notes, "visualSizeRange=" + plantDef.plant.visualSizeRange);
            session.Result.Notes = AppendNote(session.Result.Notes, "maxMeshCount=" + plantDef.plant.maxMeshCount);
            session.Result.Notes = AppendNote(session.Result.Notes, "PlantViewTerrain=" + terrain.defName);
        }

        private TerrainDef ResolveCompatiblePlantViewTerrain(ThingDef plantDef)
        {
            float minimumFertility = plantDef?.plant?.fertilityMin ?? 0f;
            TerrainDef preferred = TerrainDefOf.SoilRich ?? TerrainDefOf.Soil;
            if (preferred != null && preferred.fertility >= minimumFertility)
            {
                return preferred;
            }

            return DefDatabase<TerrainDef>.AllDefsListForReading
                .Where(def => def != null && def.fertility >= minimumFertility)
                .OrderByDescending(def => def.fertility)
                .ThenBy(def => def.defName)
                .FirstOrDefault();
        }

        private void PrepareBuildingView(StaticViewTestSession session)
        {
            ThingDef buildingDef = session.Spec.TargetDef as ThingDef;
            if (buildingDef == null)
            {
                throw new InvalidOperationException("BuildingView target was not a ThingDef.");
            }

            Thing building = ThingMaker.MakeThing(buildingDef, ResolveStuff(buildingDef));
            if (building == null)
            {
                throw new InvalidOperationException("ThingMaker returned null for building " + buildingDef.defName + ".");
            }

            if (buildingDef.useHitPoints && building.HitPoints <= 0)
            {
                building.HitPoints = building.MaxHitPoints;
            }

            Rot4 rotation = buildingDef.rotatable ? Rot4.East : Rot4.North;
            LogSyncAutoTestSpecDef matchedSpec = ResolveBestMatchingSpec(session.Spec.Kind, buildingDef);
            PrepareBuildingFactionBeforeSpawn(building, AutoTestBuildingContext.BuildingStaticView, session.Result,
                ResolveFactionFromSpec(matchedSpec), matchedSpec?.buildingFactionless == true);
            GenSpawn.Spawn(building, session.Context.AnchorCell, session.Context.Map, rotation, WipeMode.Vanish, false, false);
            if (building.Destroyed || !building.Spawned)
            {
                throw new InvalidOperationException("Building was destroyed or not spawned after placement: " + buildingDef.defName + ".");
            }

            building.DirtyMapMesh(session.Context.Map);
            session.Context.CreatedThings.Add(building);
            int componentCount = (building as ThingWithComps)?.AllComps?.Count ?? 0;
            session.Result.Label = building.LabelCap;
            session.Result.OverlayLine1 = session.Result.Label;
            session.Result.OverlayLine2 = buildingDef.defName;
            session.Result.OverlayLine3 = "Size: " + buildingDef.Size.x + "x" + buildingDef.Size.z + " / Rotation: " + building.Rotation;
            session.Result.OverlayLine4 = "Graphic: " + (building.Graphic?.GetType().Name ?? "null") + " / Comps: " + componentCount;
            session.Result.Notes = AppendNote(session.Result.Notes, "BuildingSize=" + buildingDef.Size.x + "x" + buildingDef.Size.z);
            session.Result.Notes = AppendNote(session.Result.Notes, "BuildingRotation=" + building.Rotation);
            session.Result.Notes = AppendNote(session.Result.Notes, "GraphicClass=" + (building.Graphic?.GetType().FullName ?? "null"));
            session.Result.Notes = AppendNote(session.Result.Notes, "ComponentCount=" + componentCount);
        }

        private void PrepareTerrainPatchView(StaticViewTestSession session)
        {
            TerrainDef terrainDef = session.Spec.TargetDef as TerrainDef;
            if (terrainDef == null)
            {
                throw new InvalidOperationException("TerrainPatchView target was not a TerrainDef.");
            }

            IntVec3 patchRoot = session.Context.AnchorCell + new IntVec3(-4, 0, -4);
            int applied = 0;
            for (int x = 0; x < 9; x++)
            {
                for (int z = 0; z < 9; z++)
                {
                    IntVec3 cell = patchRoot + new IntVec3(x, 0, z);
                    session.Context.Map.terrainGrid.SetTerrain(cell, terrainDef);
                    session.Context.Map.roofGrid.SetRoof(cell, null);
                    session.Context.Map.fogGrid?.Unfog(cell);
                    applied++;
                }
            }

            int readback = 0;
            for (int x = 0; x < 9; x++)
            {
                for (int z = 0; z < 9; z++)
                {
                    IntVec3 cell = patchRoot + new IntVec3(x, 0, z);
                    if (session.Context.Map.terrainGrid.TerrainAt(cell) == terrainDef)
                    {
                        readback++;
                    }

                    session.Context.Map.mapDrawer.MapMeshDirty(cell, MapMeshFlagDefOf.Terrain);
                }
            }

            session.Context.AppliedCellCount = applied;
            session.Context.ReadbackCellCount = readback;
            session.Result.Label = terrainDef.label ?? terrainDef.defName;
            session.Result.OverlayLine1 = session.Result.Label;
            session.Result.OverlayLine2 = terrainDef.defName;
            session.Result.OverlayLine3 = "Terrain patch: 9x9";
            session.Result.OverlayLine4 = "Applied: " + applied + " / Readback: " + readback;
            session.Result.Notes = AppendNote(session.Result.Notes, "TerrainPatchAppliedCount=" + applied);
            session.Result.Notes = AppendNote(session.Result.Notes, "TerrainPatchReadbackCount=" + readback);
            if (applied != 81 || readback != 81)
            {
                throw new InvalidOperationException("Terrain 9x9 readback mismatch. applied=" + applied + " readback=" + readback + ".");
            }
        }

        private void PrepareStaticViewForCapture(StaticViewContext context)
        {
            if (context?.Map == null)
            {
                return;
            }

            foreach (Thing thing in context.CreatedThings)
            {
                if (thing != null && !thing.Destroyed && thing.Spawned)
                {
                    thing.DirtyMapMesh(context.Map);
                    context.Map.mapDrawer.MapMeshDirty(thing.Position, MapMeshFlagDefOf.Things);
                }
            }
        }

        private bool FocusStaticView(StaticViewContext context)
        {
            try
            {
                if (context?.Map == null || Find.CameraDriver == null)
                {
                    return false;
                }

                float rootSize = context.Profile?.FixedRootSize ?? context.Profile?.MinRootSize ?? 11f;
                float cameraHeight = context.Profile?.CameraHeight ?? 65f;
                Vector3 anchor = context.AnchorCell.ToVector3Shifted();
                Find.CameraDriver.SetRootPosAndSize(new Vector3(anchor.x, cameraHeight, anchor.z), rootSize);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryCaptureStaticViewScreenshot(StaticViewTestSession session, bool force)
        {
            if (session?.Context?.Map == null || screenshotService == null || !Find.Maps.Contains(session.Context.Map))
            {
                return false;
            }

            return screenshotService.TryCapture(new AutoTestScreenshotRequest
            {
                Result = session.Result,
                AllowCapture = session.AllowScreenshots,
                Force = force,
                IsFailureArtifact = force,
                ResultIndex = screenshotService.ResolveTurretResultIndex(results, activeStaticViewSessions.IndexOf(session)),
                StageSuffix = force ? (session.Result?.Stage ?? "failed") : session.Spec.StageSuffix,
                RelativeSubdirectory = session.Spec.RelativeScreenshotSubdirectory,
                Prepare = () =>
                {
                    PrepareStaticViewForCapture(session.Context);
                    FocusStaticView(session.Context);
                },
                Guard = () => session.Context?.Map != null && Find.Maps.Contains(session.Context.Map),
                Profile = session.Spec.Profile,
                SuccessNoteKey = "ViewScreenshotCaptured=True",
                FailureNoteKey = "ViewFailureScreenshotCaptured=True"
            });
        }

        private bool RestoreStaticViewContext(StaticViewContext context, AutoTestResult result)
        {
            if (context?.Map == null)
            {
                return false;
            }

            foreach (Thing thing in context.CreatedThings.ToList())
            {
                try
                {
                    if (thing != null && !thing.Destroyed)
                    {
                        thing.Destroy(DestroyMode.Vanish);
                    }
                }
                catch
                {
                }
            }

            context.TargetRemoved = context.CreatedThings.All(thing => thing == null || thing.Destroyed || !thing.Spawned);
            foreach (StaticViewCellSnapshot snapshot in context.Cells)
            {
                try
                {
                    if (snapshot.Terrain != null)
                    {
                        context.Map.terrainGrid.SetTerrain(snapshot.Cell, snapshot.Terrain);
                    }

                    context.Map.roofGrid.SetRoof(snapshot.Cell, snapshot.Roof);
                    if (context.Map.pollutionGrid != null)
                    {
                        context.Map.pollutionGrid.SetPolluted(snapshot.Cell, snapshot.Polluted, silent: true);
                    }

                    RestoreFogState(context.Map, snapshot.Cell, snapshot.Fogged);
                }
                catch
                {
                }
            }

            foreach (StaticViewThingSnapshot thingSnapshot in context.DisplacedThings)
            {
                try
                {
                    Thing thing = thingSnapshot.Thing;
                    if (thing != null && !thing.Destroyed && !thing.Spawned)
                    {
                        GenSpawn.Spawn(thing, thingSnapshot.Position, context.Map, thingSnapshot.Rotation, WipeMode.Vanish, false, false);
                    }
                }
                catch
                {
                }
            }

            context.TerrainRestored = context.Cells.All(snapshot => context.Map.terrainGrid.TerrainAt(snapshot.Cell) == snapshot.Terrain);
            context.RoofRestored = context.Cells.All(snapshot => context.Map.roofGrid.RoofAt(snapshot.Cell) == snapshot.Roof);
            context.PollutionRestored = context.Map.pollutionGrid == null || context.Cells.All(snapshot => context.Map.pollutionGrid.IsPolluted(snapshot.Cell) == snapshot.Polluted);
            context.FogRestored = context.Cells.All(snapshot => snapshot.Cell.Fogged(context.Map) == snapshot.Fogged);
            context.DisplacedThingsRestored = context.DisplacedThings.All(snapshot => snapshot.Thing == null || snapshot.Thing.Destroyed || (snapshot.Thing.Spawned && snapshot.Thing.Position == snapshot.Position));

            result.Notes = AppendNote(result.Notes, "TargetRemoved=" + context.TargetRemoved);
            result.Notes = AppendNote(result.Notes, "TerrainRestored=" + context.TerrainRestored);
            result.Notes = AppendNote(result.Notes, "RoofRestored=" + context.RoofRestored);
            result.Notes = AppendNote(result.Notes, "FogRestored=" + context.FogRestored);
            result.Notes = AppendNote(result.Notes, "PollutionRestored=" + context.PollutionRestored);
            result.Notes = AppendNote(result.Notes, "DisplacedThingsRestored=" + context.DisplacedThingsRestored);
            return context.TargetRemoved && context.TerrainRestored && context.RoofRestored && context.FogRestored && context.PollutionRestored && context.DisplacedThingsRestored;
        }

        private static void RestoreFogState(Map targetMap, IntVec3 cell, bool shouldBeFogged)
        {
            if (targetMap?.fogGrid == null)
            {
                return;
            }

            if (!shouldBeFogged)
            {
                targetMap.fogGrid.Unfog(cell);
                return;
            }

            try
            {
                targetMap.fogGrid.Refog(CellRect.SingleCell(cell));
            }
            catch
            {
            }
        }
    }
}
