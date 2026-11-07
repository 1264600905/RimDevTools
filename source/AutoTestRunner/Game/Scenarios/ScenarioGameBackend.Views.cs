using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace LogSyncMod.AutoTest
{
    internal sealed class ScenarioViewStudio
    {
        internal Map Map;
        internal IntVec3 Root;
        internal int Width;
        internal int Height;
        internal Action Restore;
    }

    internal sealed partial class ScenarioGameBackend
    {
        private static readonly string[] ScenarioViewConditions = { "PawnAppearance", "HasGene", "HasXenotype", "PlantGrowth", "TerrainState", "ThingStyle" };
        private Thing viewTarget;
        private XElement viewStep;
        private string viewIdentity;
        private IntVec3 viewPosition;
        private TimeSpeed viewPreviousSpeed;
        private bool viewPaused;
        internal string ViewEquipmentSummary { get; private set; }
        private readonly AutoTestThreeViewService configuredViewService = new AutoTestThreeViewService();
        private AutoTestThreeViewTargetContext configuredViewContext;
        private int viewChangedTick;
        internal bool CanBeginView => Find.TickManager.TicksGame > viewChangedTick;

        internal Rect? CompositePawnViewport()
        {
            if (!(viewTarget is Pawn pawn)) return null;
            var camera = Find.CameraDriver.GetComponent<Camera>();
            var minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            void Include(Vector3 world)
            {
                var point = camera.WorldToViewportPoint(world);
                if (point.z <= 0) throw new InvalidOperationException("Pawn composite geometry is behind the camera");
                minimum.x = Math.Min(minimum.x, point.x); minimum.y = Math.Min(minimum.y, point.y);
                maximum.x = Math.Max(maximum.x, point.x); maximum.y = Math.Max(maximum.y, point.y);
            }

            // Frame preparation invalidates the render tree. Resolve native draw geometry again
            // without drawing another pawn or relying on a previous direction's render requests.
            var renderer = pawn.Drawer.renderer;
            renderer.EnsureGraphicsInitialized();
            HarmonyLib.AccessTools.Method(typeof(PawnRenderer), "ParallelGetPreRenderResults")
                .Invoke(renderer, new object[] { pawn.DrawPos, (Rot4?)pawn.Rotation, false, true });
            // Mesh transforms include HAR sizes, hair, apparel, tails and attachments.
            var tree = renderer.renderTree;
            var requests = tree == null ? null : HarmonyLib.AccessTools.Field(typeof(PawnRenderTree), "drawRequests")?.GetValue(tree) as List<PawnGraphicDrawRequest>;
            var hasGeometry = false;
            foreach (var request in requests ?? Enumerable.Empty<PawnGraphicDrawRequest>())
            {
                if (request.mesh == null || request.material == null) continue;
                foreach (var vertex in request.mesh.vertices)
                {
                    Include(request.preDrawnComputedMatrix.MultiplyPoint3x4(vertex));
                    hasGeometry = true;
                }
            }
            if (!hasGeometry) throw new InvalidOperationException("Pawn composite has no settled render geometry");

            // Wait_Combat presents a carried weapon outside the render tree. Match its native
            // directional offset and rotated quad instead of reserving a large circular area.
            if (configuredViewContext.DisplayWeapon && pawn.equipment?.Primary is Thing weapon)
            {
                var facing = pawn.Rotation;
                var direction = new[] { "North", "East", "South", "West" }[facing.AsInt];
                var offset = (Vector3)HarmonyLib.AccessTools.Field(typeof(PawnRenderUtility), "EqLoc" + direction).GetValue(null);
                var position = pawn.DrawPos + offset * pawn.ageTracker.CurLifeStage.equipmentDrawDistanceFactor;
                position.y += PawnRenderUtility.AltitudeForLayer(facing == Rot4.North ? -10f : 90f);
                var aimAngle = facing == Rot4.West ? 217f : 143f;
                var angle = facing == Rot4.West ? aimAngle - 270f - weapon.def.equippedAngleOffset : aimAngle - 90f + weapon.def.equippedAngleOffset;
                var equippable = weapon.TryGetComp<CompEquippable>();
                if (equippable != null)
                {
                    EquipmentUtility.Recoil(weapon.def, EquipmentUtility.GetRecoilVerb(equippable.AllVerbs), out var recoilOffset, out var recoilAngle, aimAngle);
                    position += recoilOffset; angle += recoilAngle;
                }
                var size = weapon.Graphic.drawSize;
                var matrix = Matrix4x4.TRS(position, Quaternion.AngleAxis(angle, Vector3.up), new Vector3(size.x, 0, size.y));
                foreach (var vertex in MeshPool.plane10.vertices) Include(matrix.MultiplyPoint3x4(vertex));
            }
            if (minimum.x < 0 || minimum.y < 0 || maximum.x > 1 || maximum.y > 1) throw new InvalidOperationException("Pawn composite geometry extends outside the original image");
            return new Rect(minimum.x, minimum.y, maximum.x - minimum.x, maximum.y - minimum.y);
        }

        private IEnumerable<IntVec3> SceneCells => viewStudio != null ? new CellRect(viewStudio.Root.x, viewStudio.Root.z, viewStudio.Width, viewStudio.Height).Cells : Map.AllCells;
        private IntVec3 SceneCenter => viewStudio != null ? viewStudio.Root + new IntVec3(viewStudio.Width / 2, 0, viewStudio.Height / 2) : Map.Center;

        private XElement TranslateStudioCoordinates(XElement node)
        {
            if (viewStudio == null) return node;
            var result = new XElement(node);
            foreach (var element in result.DescendantsAndSelf())
            foreach (var attr in element.Attributes().ToList())
            {
                if (new[] { "x", "originX", "minX", "maxX", "fromX", "toX", "targetX" }.Contains(attr.Name.LocalName)) attr.Value = (int.Parse(attr.Value, CultureInfo.InvariantCulture) + viewStudio.Root.x).ToString(CultureInfo.InvariantCulture);
                if (new[] { "z", "originZ", "minZ", "maxZ", "fromZ", "toZ", "targetZ" }.Contains(attr.Name.LocalName)) attr.Value = (int.Parse(attr.Value, CultureInfo.InvariantCulture) + viewStudio.Root.z).ToString(CultureInfo.InvariantCulture);
            }
            // Defaults are local scene coordinates too. Materialize them before actor creation.
            foreach (var actor in result.Element("Actors")?.Elements() ?? Enumerable.Empty<XElement>())
            {
                if (actor.Name == "PawnGroup") { if (actor.Attribute("originX") == null) actor.SetAttributeValue("originX", 20 + viewStudio.Root.x); if (actor.Attribute("originZ") == null) actor.SetAttributeValue("originZ", 20 + viewStudio.Root.z); }
                else if (new[] { "Pawn", "Corpse", "Building", "Thing" }.Contains(actor.Name.LocalName)) { if (actor.Attribute("x") == null) actor.SetAttributeValue("x", 20 + viewStudio.Root.x); if (actor.Attribute("z") == null) actor.SetAttributeValue("z", 20 + viewStudio.Root.z); }
            }
            return result;
        }

        private static Rot4 ParseRotation(string value) => value switch { "North" => Rot4.North, "East" => Rot4.East, "South" => Rot4.South, "West" => Rot4.West, _ => throw new ScenarioConfigurationException("Invalid rotation: " + value) };

        private void ApplyAge(Pawn pawn, XElement age)
        {
            if (age == null) return;
            Need(pawn.ageTracker, "age");
            if (age.Attribute("biologicalYears") != null) pawn.ageTracker.AgeBiologicalTicks = checked((long)(ScenarioLoader.D(age, "biologicalYears") * GenDate.TicksPerYear));
            if (age.Attribute("chronologicalYears") != null) pawn.ageTracker.AgeChronologicalTicks = checked((long)(ScenarioLoader.D(age, "chronologicalYears") * GenDate.TicksPerYear));
            else pawn.ageTracker.AgeChronologicalTicks = Math.Max(pawn.ageTracker.AgeChronologicalTicks, pawn.ageTracker.AgeBiologicalTicks);
            // The native getter recalculates the cached life-stage index when it is invalidated.
            HarmonyLib.AccessTools.Method(typeof(Pawn_AgeTracker), "RecalculateLifeStageIndex").Invoke(pawn.ageTracker, null);
            trace("pawn.age", pawn.GetUniqueLoadID() + "; biologicalYears=" + pawn.ageTracker.AgeBiologicalYearsFloat + "; lifeStage=" + pawn.ageTracker.CurLifeStage.defName);
        }

        private static void ApplyColor(Thing thing, string value)
        {
            if (value == null) return;
            var colorable = thing.TryGetComp<CompColorable>() ?? throw new ScenarioConfigurationException("Thing has no color component: " + thing.def.defName);
            var color = ParseColor(value); colorable.SetColor(color);
            if (Vector4.Distance(colorable.Color, color) > .01f) throw new InvalidOperationException("Native color override rejected");
        }

        private void SetPlantGrowth(Thing thing, float growth)
        {
            var plant = thing as Plant ?? throw new ScenarioConfigurationException("Growth requires a Plant actor");
            plant.Growth = growth;
            if (Math.Abs(plant.Growth - growth) > .0001f) throw new InvalidOperationException("Native plant growth override rejected");
            if (plant.Spawned) Map.mapDrawer.MapMeshDirty(plant.Position, MapMeshFlagDefOf.Things);
            trace("plant.growth", plant.GetUniqueLoadID() + "; growth=" + plant.Growth);
            viewChangedTick = Find.TickManager.TicksGame;
        }

        private bool IsTypedView(string type, XElement view, XElement root)
        {
            var id = A(view, "actor");
            var actor = root.Element("Actors").Elements().FirstOrDefault(a => A(a, "id") == id);
            if (actor?.Name == "TerrainArea") return type == "TerrainPatchView";
            if (!actors.TryGetValue(id, out var list) || list.Count != 1) return false;
            var thing = list[0]; var pawn = thing as Pawn;
            return type switch {
                "HumanoidThreeView" => pawn?.def == ThingDefOf.Human,
                "AnimalThreeView" => pawn?.RaceProps.Animal == true,
                "MechanoidThreeView" => pawn?.RaceProps.IsMechanoid == true,
                "AlienThreeView" => pawn?.RaceProps.Humanlike == true && pawn.def != ThingDefOf.Human,
                "ApparelThreeView" => pawn?.apparel?.WornApparel.Any(a => A(view, "targetDef") == null || a.def.defName == A(view, "targetDef")) == true,
                "WeaponThreeView" => pawn?.equipment?.Primary != null && (A(view, "targetDef") == null || pawn.equipment.Primary.def.defName == A(view, "targetDef")) && A(view, "pose") == "weaponReady",
                "PlantGrowthView" => thing is Plant,
                "BuildingView" => thing is Building,
                "PawnLifeStageThreeView" => pawn != null && (actor.Element("Age") != null || actor.Element("minGenerationAge") != null),
                "HairThreeView" => pawn?.story?.hairDef != null && actor.Element("Appearance")?.Element("hair") != null,
                "HeadTypeThreeView" => pawn?.story?.headType != null && actor.Element("Appearance")?.Element("headType") != null,
                "BodyTypeThreeView" => pawn?.story?.bodyType != null && actor.Element("Appearance")?.Element("bodyType") != null,
                "GeneThreeView" => pawn?.genes?.GenesListForReading.Count > 0 && actor.Element("Genes") != null,
                "XenotypeThreeView" => pawn?.genes != null && actor.Element("Xenotype") != null,
                "LoadoutView" => pawn != null && A(view, "loadout", "preserve") == "preserve",
                _ => false };
        }

        internal IntVec3 CaptureCell(string id)
        {
            if (id == null) return SceneCenter;
            if (actors.ContainsKey(id)) return One(id).Position;
            var actor = runtimeRoot.Element("Actors").Elements().FirstOrDefault(a => A(a, "id") == id);
            if (actor?.Name != "TerrainArea") throw new ScenarioConfigurationException("Capture requires a spawned actor or terrain area: " + id);
            return new IntVec3(I(actor, "x") + I(actor, "width") / 2, 0, I(actor, "z") + I(actor, "height") / 2);
        }

        internal void BeginView(XElement step)
        {
            viewStep = step;
            var id = A(step, "actor");
            viewTarget = actors.ContainsKey(id) ? One(id) : null;
            if (A(step, "pose") == "auto") step.SetAttributeValue("pose", viewTarget is Pawn preparedPawn && preparedPawn.equipment?.Primary != null && !preparedPawn.Downed ? "weaponReady" : "standing");
            if (viewTarget == null && (!references.TryGetValue(id, out var terrain) || !(terrain is TerrainDef))) throw new ScenarioConfigurationException("View requires one scene object or a TerrainArea");
            if (!(viewTarget is Pawn) && viewTarget?.def.rotatable != true && ScenarioViews.Directions(step).Length > 1) throw new ScenarioConfigurationException("A static plant, terrain or non-rotatable Thing uses a single image direction");
            if (viewTarget != null && (!viewTarget.Spawned || viewTarget.Destroyed || viewTarget.Map != Map)) throw new ScenarioConfigurationException("View target is not spawned in this scene");
            if (A(step, "loadout") == "isolate")
            {
                var pawn = viewTarget as Pawn ?? throw new ScenarioConfigurationException("Isolated loadout requires Pawn");
                var target = A(step, "targetDef");
                var apparel = pawn.apparel?.WornApparel.FirstOrDefault(a => a.def.defName == target);
                if (apparel == null && pawn.equipment?.Primary?.def.defName != target) throw new ScenarioConfigurationException("Isolation target is not worn/equipped: " + target);
                foreach (var other in pawn.apparel?.WornApparel.Where(a => a != apparel).ToList() ?? new List<Apparel>()) other.Destroy();
                if (apparel != null) pawn.equipment?.DestroyAllEquipment();
                trace("view.isolated", "explicit target=" + target);
            }
            if (A(step, "pose", "standing") == "weaponReady" && (!(viewTarget is Pawn p) || p.equipment?.Primary == null || p.Downed)) throw new ScenarioConfigurationException("weaponReady requires a standing Pawn with a primary weapon");
            viewIdentity = ViewIdentity();
            ViewEquipmentSummary = viewTarget is Pawn equippedPawn ? string.Join(" / ", (equippedPawn.apparel?.WornApparel.Select(a => a.LabelCap.ToString()) ?? Enumerable.Empty<string>()).Concat(equippedPawn.equipment?.AllEquipmentListForReading.Select(e => e.LabelCap.ToString()) ?? Enumerable.Empty<string>())) : "";
            if (!string.IsNullOrWhiteSpace(ViewEquipmentSummary)) trace("view.equipment", ViewEquipmentSummary);
            viewPosition = CaptureCell(id);
            configuredViewContext = new AutoTestThreeViewTargetContext {
                Map = Map, AnchorCell = viewPosition,
                Profile = new AutoTestScreenshotProfile { ProfileId = "ConfiguredThreeView", FixedRootSize = (float)ScenarioLoader.D(step, "cameraSize", AutoTestScreenshotProfiles.ThreeViewHumanoid.FixedRootSize.Value), CameraHeight = AutoTestScreenshotProfiles.ThreeViewHumanoid.CameraHeight },
                DisplayWeapon = A(step, "pose", "standing") == "weaponReady"
            };
            if (viewTarget is Pawn presenter)
            {
                configuredViewContext.Pawns.Add(presenter); configuredViewContext.Cells.Add(viewPosition); configuredViewContext.Rotations.Add(presenter.Rotation);
            }
            if (viewTarget == null)
            {
                var area = runtimeRoot.Element("Actors").Elements().Single(a => A(a, "id") == id);
                configuredViewContext.AnchorWorldPosition = new Vector3(I(area, "x") + I(area, "width") / 2f, 0, I(area, "z") + I(area, "height") / 2f);
            }
            viewPreviousSpeed = Find.TickManager.CurTimeSpeed;
            if (viewTarget is Pawn photoPawn) AutoTestThreeViewRenderSuppression.SetStandingPresentation(photoPawn, true);
            viewPaused = true;
            AutoTestBootstrap.SetTemporaryPauseBypass(true);
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Find.Selector.ClearSelection();
            trace("view.loadout", viewIdentity);
        }

        internal void UpdateView(string direction)
        {
            if (viewStep == null) return;
            var rotation = ParseRotation(direction);
            if (viewTarget != null)
            {
                if (!viewTarget.Spawned || viewTarget.Position != viewPosition) throw new InvalidOperationException("View target moved or disappeared");
                if (viewTarget is Pawn pawn)
                {
                    configuredViewContext.Rotations[0] = rotation;
                    if (!configuredViewService.TryPrimeDirectionalFacingForPausedCapture(configuredViewContext, out var error)) throw new InvalidOperationException(error);
                }
                else if (viewTarget.def.rotatable) viewTarget.Rotation = rotation;
            }
            if (ViewIdentity() != viewIdentity) throw new InvalidOperationException("View changed the target identity, appearance or loadout between directions");
        }

        internal void MaintainViewFrame()
        {
            if (configuredViewContext == null) return;
            configuredViewService.PrepareForCapture(configuredViewContext);
            if (!configuredViewService.Focus(Map, configuredViewContext)) throw new InvalidOperationException("Existing three-view camera could not focus the configured presenter");
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
        }

        internal void ValidateViewFrame(string direction)
        {
            if (viewTarget != null && (viewTarget.Position.Fogged(Map) || !viewTarget.Spawned)) throw new InvalidOperationException("View target is not visible");
            if (viewTarget is Pawn pawn && pawn.Rotation != ParseRotation(direction)) throw new InvalidOperationException("Pawn direction did not settle");
            if (viewTarget?.def.rotatable == true && viewTarget.Rotation != ParseRotation(direction)) throw new InvalidOperationException("Thing direction did not settle");
            if (ViewIdentity() != viewIdentity) throw new InvalidOperationException("View identity or loadout changed during capture");
            var camera = Find.CameraDriver.GetComponent<Camera>();
            var point = camera.WorldToViewportPoint(viewTarget?.DrawPos ?? viewPosition.ToVector3Shifted());
            if (point.z <= 0 || point.x < .1f || point.x > .9f || point.y < .15f || point.y > .8f) throw new InvalidOperationException("Three-view presenter is outside the safe image area: " + point);
            if (viewTarget == null)
            {
                var area = runtimeRoot.Element("Actors").Elements().Single(a => A(a, "id") == A(viewStep, "actor"));
                foreach (var x in new[] { (float)I(area, "x"), I(area, "x") + I(area, "width") })
                foreach (var z in new[] { (float)I(area, "z"), I(area, "z") + I(area, "height") })
                {
                    var corner = camera.WorldToViewportPoint(new Vector3(x, 0, z));
                    if (corner.z <= 0 || corner.x < .06f || corner.x > .94f || corner.y < .12f || corner.y > .8f) throw new InvalidOperationException("Terrain view boundary would be clipped or covered by UI; increase cameraSize: " + corner);
                }
            }
            trace("view.frame", "direction=" + direction + "; pose=" + A(viewStep, "pose", "standing") + "; " + viewIdentity);
        }

        internal void CompleteView()
        {
            if (viewTarget is Pawn pawn)
            {
                foreach (var name in new[] { pawn.kindDef.defName, pawn.def.defName, pawn.story?.hairDef?.defName, pawn.story?.bodyType?.defName, pawn.story?.headType?.defName, pawn.genes?.Xenotype?.defName, pawn.equipment?.Primary?.def.defName }.Where(n => n != null)) MarkCoverage(currentDefinition.CaseType, name);
                foreach (var name in (pawn.apparel?.WornApparel.Select(a => a.def.defName) ?? Enumerable.Empty<string>()).Concat(pawn.genes?.GenesListForReading.Select(g => g.def.defName) ?? Enumerable.Empty<string>())) MarkCoverage(currentDefinition.CaseType, name);
            }
            else if (viewTarget != null) MarkCoverage(currentDefinition.CaseType, viewTarget.def.defName);
            else if (references.TryGetValue(A(viewStep, "actor"), out var terrain) && terrain is TerrainDef t) MarkCoverage(currentDefinition.CaseType, t.defName);
            trace("view.completed", viewIdentity);
            EndView();
        }

        private void EndView()
        {
            if (!viewPaused) return;
            if (viewTarget is Pawn photoPawn) AutoTestThreeViewRenderSuppression.SetStandingPresentation(photoPawn, false);
            Find.TickManager.CurTimeSpeed = viewPreviousSpeed;
            AutoTestBootstrap.SetTemporaryPauseBypass(false);
            viewPaused = false; viewStep = null; viewTarget = null; configuredViewContext = null;
        }

        private string ViewIdentity()
        {
            if (viewTarget == null) return "terrain=" + A(viewStep, "actor");
            if (!(viewTarget is Pawn p)) return viewTarget.GetUniqueLoadID() + "; def=" + viewTarget.def.defName + "; stuff=" + viewTarget.Stuff?.defName + "; color=" + viewTarget.DrawColor;
            string Item(Thing t) { t.TryGetQuality(out var quality); return t.GetUniqueLoadID() + "/" + t.def.defName + "/" + t.Stuff?.defName + "/" + quality + "/" + t.DrawColor; }
            return p.GetUniqueLoadID() + "; kind=" + p.kindDef.defName + "; race=" + p.def.defName + "; gender=" + p.gender + "; age=" + p.ageTracker.AgeBiologicalTicks + "; lifeStage=" + p.ageTracker.CurLifeStage.defName +
                "; hair=" + p.story?.hairDef?.defName + "; hairColor=" + p.story?.HairColor + "; skinColor=" + p.story?.SkinColor + "; head=" + p.story?.headType?.defName + "; body=" + p.story?.bodyType?.defName +
                "; genes=" + string.Join(",", p.genes?.GenesListForReading.Select(g => g.def.defName + ":" + g.Active) ?? Enumerable.Empty<string>()) +
                "; apparel=" + string.Join(",", p.apparel?.WornApparel.Select(Item) ?? Enumerable.Empty<string>()) + "; equipment=" + string.Join(",", p.equipment?.AllEquipmentListForReading.Select(Item) ?? Enumerable.Empty<string>());
        }

        private ScenarioSample SampleView(XElement condition)
        {
            var id = A(condition, "actor");
            var sample = new ScenarioSample();
            if (condition.Name == "TerrainState")
            {
                var actor = runtimeRoot.Element("Actors").Elements().FirstOrDefault(e => A(e, "id") == id);
                if (actor?.Name != "TerrainArea") throw new ScenarioConfigurationException("TerrainState requires TerrainArea");
                var cells = CellRect.FromLimits(I(actor, "x"), I(actor, "z"), I(actor, "x") + I(actor, "width") - 1, I(actor, "z") + I(actor, "height") - 1).ToList();
                sample.TargetCount = 1; sample.Details = "cells=" + cells.Count;
                if (cells.Count > 0 && cells.All(c => c.InBounds(Map) && Map.terrainGrid.TerrainAt(c).defName == A(condition, "def"))) sample.Matches.Add(id);
                return sample;
            }
            var things = Select(id); sample.TargetCount = things.Count;
            foreach (var thing in things)
            {
                bool match; var pawn = thing as Pawn;
                if (condition.Name == "PawnAppearance")
                {
                    if (pawn == null) throw new ScenarioConfigurationException("PawnAppearance requires Pawn");
                    string actual = A(condition, "property") switch {
                        "kind" => pawn.kindDef.defName, "race" => pawn.def.defName, "gender" => pawn.gender.ToString(), "hair" => pawn.story?.hairDef?.defName,
                        "bodyType" => pawn.story?.bodyType?.defName, "headType" => pawn.story?.headType?.defName, "hairColor" => ColorUtility.ToHtmlStringRGBA(pawn.story.HairColor),
                        "skinColor" => ColorUtility.ToHtmlStringRGBA(pawn.story.SkinColor), "lifeStage" => pawn.ageTracker.CurLifeStage.defName,
                        "biologicalYears" => pawn.ageTracker.AgeBiologicalYearsFloat.ToString("0.###", CultureInfo.InvariantCulture), "rotation" => pawn.Rotation.ToStringHuman(), _ => null };
                    var expected = A(condition, "value");
                    match = A(condition, "property").EndsWith("Color") ? string.Equals(actual, ColorUtility.ToHtmlStringRGBA(ParseColor(expected)), StringComparison.OrdinalIgnoreCase) : actual == expected;
                    sample.Details += A(condition, "property") + "=" + actual + "; ";
                }
                else if (condition.Name == "HasGene")
                {
                    var gene = pawn?.genes?.GenesListForReading.FirstOrDefault(g => g.def.defName == A(condition, "def") && (condition.Attribute("xenogene") == null || pawn.genes.Xenogenes.Contains(g) == bool.Parse(A(condition, "xenogene"))));
                    match = gene != null && (condition.Attribute("active") == null || gene.Active == bool.Parse(A(condition, "active")));
                    sample.Details += "gene=" + gene?.def.defName + "; active=" + gene?.Active + "; ";
                }
                else if (condition.Name == "HasXenotype") match = pawn?.genes?.Xenotype?.defName == A(condition, "def");
                else if (condition.Name == "PlantGrowth") { match = thing is Plant p && InRange(p.Growth, condition); sample.Details += "growth=" + (thing as Plant)?.Growth + "; "; }
                else
                {
                    var hasQuality = thing.TryGetQuality(out var quality);
                    match = (condition.Attribute("stuff") == null || thing.Stuff?.defName == A(condition, "stuff")) && (condition.Attribute("quality") == null || hasQuality && quality.ToString() == A(condition, "quality")) &&
                        (condition.Attribute("color") == null || Vector4.Distance(thing.DrawColor, ParseColor(A(condition, "color"))) < .01f) && (condition.Attribute("rotation") == null || thing.Rotation == ParseRotation(A(condition, "rotation")));
                    sample.Details += "stuff=" + thing.Stuff?.defName + "; quality=" + quality + "; color=" + thing.DrawColor + "; rotation=" + thing.Rotation + "; ";
                }
                if (match) sample.Matches.Add(thing.GetUniqueLoadID());
            }
            return sample;
        }
    }
}
