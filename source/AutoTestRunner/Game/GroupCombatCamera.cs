using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LogSyncMod.AutoTest.Scenarios;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    [HarmonyPatch(typeof(CameraDriver), nameof(CameraDriver.Update))]
    internal static class GroupCombatCamera
    {
        private static CameraMapConfig previousConfig;
        private static CameraMapConfig combatConfig;
        private static float targetProjectionSize;
        private static float rootSize;
        private static float previousFrameTime;
        private static GroupCombatCameraMotion motion;
        private static float maximumProjectionSize, maximumRootSize, markerThreshold;
        private static float maximumObservedProjectionSize;
        private static int cameraFrames, limitViolations;
        private static ArenaCameraDirector director;
        private static ArenaCameraMode arenaMode = ArenaCameraMode.Auto;
        private static (float x, float z, float size)? arenaFrame;
        private static int arenaWidth, arenaHeight;
        private static bool arenaCameraFailed, travelling;
        private static readonly ArenaCameraMode[] cameraModes = { ArenaCameraMode.Auto, ArenaCameraMode.Overview, ArenaCameraMode.Battle, ArenaCameraMode.Follow, ArenaCameraMode.Close };
        internal static bool CompactPanels => director?.Compact == true && (CombatFightService.Measuring || CombatFightService.CameraIntroElapsed.HasValue);
        internal static string FocusId => director?.FocusId;
        internal static string OpponentId => director?.OpponentId;
        internal static string ShotLabel => director?.Shot.ToString() ?? "Overview";
        internal static float PanelWidth(float width) => CompactPanels ? GroupCombatPresentation.CompactPanelWidth(width) : GroupCombatPresentation.PanelWidth(width);
        private static readonly Type cameraPlusMain = AccessTools.TypeByName("CameraPlus.CameraPlusMain");
        private static readonly Type cameraPlusSettings = AccessTools.TypeByName("CameraPlus.CameraSettings");
        private static readonly Type cameraPlusDotTools = AccessTools.TypeByName("CameraPlus.DotTools");
        private static readonly MethodInfo showMarker = cameraPlusDotTools == null ? null : AccessTools.Method(cameraPlusDotTools, "ShouldShowMarker");
        internal static bool Active => combatConfig != null;

        internal static void Begin()
        {
            End();
            previousConfig = Find.CameraDriver.config;
            combatConfig = new CameraMapConfig_Normal { dollyRateKeys = 0, dollyRateScreenEdge = 0, zoomSpeed = 0, moveSpeedScale = 0 };
            Find.CameraDriver.config = combatConfig;
            CameraPlusCombatCompatibility.ClearCache();
            targetProjectionSize = 0; rootSize = 0;
            maximumProjectionSize = maximumRootSize = maximumObservedProjectionSize = 0;
            cameraFrames = limitViolations = 0;
            motion = new GroupCombatCameraMotion(); previousFrameTime = Time.realtimeSinceStartup;
            arenaCameraFailed = travelling = false; arenaFrame = null;
            arenaWidth = arenaHeight = 0;
            director = CombatFightService.IsArena ? new ArenaCameraDirector() : null;
            director?.SetMode(arenaMode);
            Postfix();
        }

        internal static void End()
        {
            if (combatConfig != null && Find.CameraDriver != null) Find.CameraDriver.config = previousConfig;
            combatConfig = null; previousConfig = null;
            director = null; arenaFrame = null;
            CameraPlusCombatCompatibility.ClearCache();
        }

        // Apply after the native camera update so manual dolly, selection and zoom cannot
        // displace the dedicated view. This does not consume mouse events or pause keys.
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            if (!Active || CombatFightService.Map == null || Find.CurrentMap != CombatFightService.Map) return;
            if (director != null && !arenaCameraFailed)
            {
                try { UpdateArena(); return; }
                catch (Exception ex)
                {
                    // Presentation failure must never cancel a match or affect damage capture.
                    arenaCameraFailed = true; director = null; rootSize = targetProjectionSize = 0;
                    motion = new GroupCombatCameraMotion();
                    Log.Warning("[LogSyncArena] Camera director unavailable; using the overview: " + ex.Message);
                }
            }
            var pawns = CombatFightService.CameraPawns.ToList();
            if (pawns.Count == 0) return; // Hold the last view for mutual elimination/final capture.
            var frame = GroupCombatPresentation.Frame(pawns.Select(p => (p.DrawPos.x, p.DrawPos.z)), UI.screenWidth, UI.screenHeight, CombatFightService.OverlayEnabled);
            var position = new Vector3(frame.x, 0, frame.z);
            markerThreshold = MarkerThreshold();
            var limit = GroupCombatPresentation.CameraSizeLimit(UI.screenHeight, markerThreshold);
            if (maximumProjectionSize != limit)
            {
                maximumProjectionSize = limit;
                maximumRootSize = RootForProjection(position, limit);
            }
            var nextProjectionSize = GroupCombatPresentation.ZoomTarget(targetProjectionSize, frame.size, maximumProjectionSize);
            if (rootSize == 0 || nextProjectionSize != targetProjectionSize)
            {
                targetProjectionSize = nextProjectionSize;
                // Camera mods can map RootSize nonlinearly to the actual projection. Fit
                // the real orthographic size instead of assuming RootSize is world units.
                rootSize = Math.Min(maximumRootSize, RootForProjection(position, targetProjectionSize));
            }
            var now = Time.realtimeSinceStartup;
            motion.Step(frame.x, frame.z, rootSize, now - previousFrameTime);
            previousFrameTime = now;
            combatConfig.sizeRange = new FloatRange(.01f, maximumRootSize);
            Find.CameraDriver.SetRootPosAndSize(new Vector3(motion.X, 0, motion.Z), Math.Min(motion.Size, maximumRootSize));
            // Weapon impacts must not shake this dedicated observation view.
            var camera = Find.CameraDriver.MyCamera;
            camera.orthographicSize = Math.Min(camera.orthographicSize, maximumProjectionSize);
            maximumObservedProjectionSize = Math.Max(maximumObservedProjectionSize, camera.orthographicSize);
            cameraFrames++;
            if (camera.orthographicSize > maximumProjectionSize + .001f) limitViolations++;
            var stablePosition = camera.transform.position;
            stablePosition.x = motion.X; stablePosition.z = motion.Z;
            camera.transform.position = stablePosition;
        }

        private static float RootForProjection(Vector3 position, float projection)
        {
            // Keep the native camera above Pawn render layers even in a close-up.
            // Arena sets its actual projection separately, so zoom need not lower that camera.
            var low = director != null ? 11f : .01f; var high = Mathf.Max(60, projection * 2);
            combatConfig.sizeRange = new FloatRange(.01f, high + 10);
            for (var i = 0; i < 18; i++)
            {
                var candidate = (low + high) / 2;
                Find.CameraDriver.SetRootPosAndSize(position, candidate);
                if (Find.CameraDriver.MyCamera.orthographicSize <= projection) low = candidate; else high = candidate;
            }
            return low;
        }

        private static void UpdateArena()
        {
            var now = Time.realtimeSinceStartup;
            var elapsed = Math.Max(0, now - previousFrameTime);
            previousFrameTime = now;
            var oldIntroductionStage = director.IntroductionStage;
            director.SetIntroduction(CombatFightService.CameraIntroElapsed);
            // Cut directly to each portrait and back to the countdown overview.
            if (oldIntroductionStage != director.IntroductionStage)
                motion = new GroupCombatCameraMotion();
            var observing = director.Advance(elapsed, CombatFightService.Measuring, Find.TickManager.Paused);
            if (observing)
            {
                var display = CombatFightService.Display?.Units.ToDictionary(u => u.Id, StringComparer.Ordinal);
                var values = new List<ArenaCameraSubject>();
                foreach (var pair in CombatFightService.Pawns)
                {
                    var pawn = pair.Value;
                    // Recovery and resurrection restore arena camera eligibility.
                    if (!pawn.Spawned || pawn.Map != CombatFightService.Map || pawn.Dead || pawn.Downed) continue;
                    var unit = display != null && display.TryGetValue(pair.Key, out var found) ? found : null;
                    var team = unit?.Team ?? CombatFightService.Team(pawn.Faction);
                    if (team == null) continue;
                    var target = pawn.CurJob?.targetA.Thing;
                    values.Add(new ArenaCameraSubject { Id = pair.Key, Team = team,
                        TargetId = CombatFightService.RosterId(target), X = pawn.DrawPos.x, Z = pawn.DrawPos.z,
                        Health = unit?.HealthPercent ?? 1, DamageDealt = unit?.EnemyDamage ?? 0, DamageTaken = unit?.DamageTaken ?? 0 });
                }
                director.Observe(values);
            }
            if (observing || arenaWidth != UI.screenWidth || arenaHeight != UI.screenHeight)
            {
                arenaFrame = director.Frame(UI.screenWidth, UI.screenHeight, CombatFightService.OverlayEnabled) ?? arenaFrame;
                arenaWidth = UI.screenWidth; arenaHeight = UI.screenHeight;
            }
            if (!arenaFrame.HasValue) return;
            var frame = arenaFrame.Value;
            markerThreshold = MarkerThreshold();
            maximumProjectionSize = GroupCombatPresentation.CameraSizeLimit(UI.screenHeight, markerThreshold);
            var nextProjection = GroupCombatPresentation.ZoomTarget(targetProjectionSize, frame.size, Math.Min(maximumProjectionSize, director.MaximumSize));
            var distance = Vector2.Distance(new Vector2(motion.X, motion.Z), new Vector2(frame.x, frame.z));
            travelling = director.IntroductionStage < 0 && cameraFrames > 0 && (distance > 22 || travelling && distance > 7);
            var travelSize = Math.Min(22, maximumProjectionSize);
            if (travelling) nextProjection = Math.Max(nextProjection, travelSize);
            var position = new Vector3(frame.x, 0, frame.z);
            if (rootSize == 0 || Math.Abs(nextProjection - targetProjectionSize) > .1f)
            {
                targetProjectionSize = nextProjection;
                rootSize = RootForProjection(position, targetProjectionSize);
            }
            var waitForWideView = travelling && motion.Size < travelSize - .75f;
            // Across distant engagements, widen first, travel second, then move back in.
            motion.Step(waitForWideView ? motion.X : frame.x, waitForWideView ? motion.Z : frame.z,
                nextProjection, elapsed);
            // Native culling may use RootSize rather than the overridden projection.
            // Keep the previous wider root until a zoom-in has almost finished.
            maximumRootSize = Math.Max(Math.Max(11, rootSize), motion.Size > nextProjection + .2f ? maximumRootSize : 11);
            combatConfig.sizeRange = new FloatRange(11, Math.Max(60, maximumRootSize));
            Find.CameraDriver.SetRootPosAndSize(new Vector3(motion.X, 0, motion.Z), maximumRootSize);
            var camera = Find.CameraDriver.MyCamera;
            camera.orthographicSize = Math.Min(motion.Size, maximumProjectionSize);
            var stable = camera.transform.position; stable.x = motion.X; stable.z = motion.Z;
            stable.y = Math.Max(65, stable.y);
            camera.transform.position = stable;
            maximumObservedProjectionSize = Math.Max(maximumObservedProjectionSize, camera.orthographicSize);
            cameraFrames++;
            if (camera.orthographicSize > maximumProjectionSize + .001f) limitViolations++;
        }

        internal static void DrawArenaControls()
        {
            if (director != null && CombatFightService.CameraIntroElapsed.HasValue)
            {
                GroupCombatOverlay.DrawText(new Rect(12, UI.screenHeight - 118, UI.screenWidth - 24, 26), director.Reason, 18, TextAnchor.MiddleCenter);
                return;
            }
            if (director == null || !CombatFightService.Measuring) return;
            var width = UI.screenWidth;
            var buttonWidth = width < 1000 ? 54 : 64;
            var total = buttonWidth * cameraModes.Length + 4 * (cameraModes.Length - 1);
            var left = (width - total) / 2f;
            var top = UI.screenHeight - 150;
            var previous = GUI.color;
            for (var i = 0; i < cameraModes.Length; i++)
            {
                var mode = cameraModes[i];
                GUI.color = director.Mode == mode ? new Color(1, .83f, .4f) : Color.white;
                if (Widgets.ButtonText(new Rect(left + i * (buttonWidth + 4), top, buttonWidth, 28), ArenaCameraDirector.Label(mode)))
                { arenaMode = mode; director.SetMode(mode); }
            }
            GUI.color = previous;
            var focusName = director.FocusId != null && CombatFightService.Pawns.TryGetValue(director.FocusId, out var pawn) ? pawn.LabelShort : null;
            var description = ArenaCameraDirector.Label(director.Mode) + " · " + ArenaCameraDirector.Label(director.Shot)
                + (director.Compact && focusName != null ? " · " + focusName : "") + " · " + director.Reason;
            GroupCombatOverlay.DrawText(new Rect(12, top + 32, width - 24, 23), description, width < 1000 ? 11 : 13, TextAnchor.MiddleCenter);
        }

        internal static void ConfigureArena(string mode, string focusId)
        {
            if (director == null || !CombatFightService.IsArena || !CombatFightService.Measuring)
                throw new InvalidOperationException("Camera control requires a running arena fight");
            if (mode != null)
            {
                if (!Enum.TryParse<ArenaCameraMode>(mode, true, out var value) || !Enum.IsDefined(typeof(ArenaCameraMode), value))
                    throw new ArgumentException("Unknown arena camera mode: " + mode);
                arenaMode = value; director.SetMode(value);
            }
            if (focusId != null)
            {
                if (!CombatFightService.Pawns.TryGetValue(focusId, out var pawn) || !pawn.Spawned || pawn.Map != CombatFightService.Map || pawn.Dead || pawn.Downed)
                    throw new ArgumentException("Camera focus must be a standing arena participant");
                if (director.Mode is not (ArenaCameraMode.Follow or ArenaCameraMode.Close))
                    throw new InvalidOperationException("Choosing a participant requires Follow or Close mode");
                director.Select(focusId);
            }
        }

        internal static void HandleArenaInput()
        {
            if (director == null || !CombatFightService.Measuring || director.Mode is not (ArenaCameraMode.Follow or ArenaCameraMode.Close)
                || Event.current.type != EventType.MouseDown || Event.current.button != 0) return;
            var mouse = Event.current.mousePosition;
            var inset = CombatFightService.OverlayEnabled ? PanelWidth(UI.screenWidth) + 22 : 16;
            if (mouse.x < inset || mouse.x > UI.screenWidth - inset || mouse.y < GroupCombatPresentation.PanelTop + 6 || mouse.y > UI.screenHeight - 160) return;
            var camera = Find.CameraDriver.MyCamera;
            string nearest = null; var closest = 28f * 28;
            foreach (var pair in CombatFightService.Pawns)
            {
                var pawn = pair.Value;
                if (!pawn.Spawned || pawn.Map != CombatFightService.Map || pawn.Dead || pawn.Downed) continue;
                var point = camera.WorldToViewportPoint(pawn.DrawPos);
                if (point.z <= 0) continue;
                var screen = new Vector2(point.x * UI.screenWidth, (1 - point.y) * UI.screenHeight);
                var distance = (screen - mouse).sqrMagnitude;
                if (distance < closest) { closest = distance; nearest = pair.Key; }
            }
            if (nearest != null) { director.Select(nearest); Event.current.Use(); }
        }

        private static float MarkerThreshold()
        {
            // Read Camera+'s current global and per-rule thresholds without changing
            // player settings. The two-pixel reserve keeps real models above them.
            var settings = cameraPlusMain == null ? null : AccessTools.Field(cameraPlusMain, "Settings")?.GetValue(null);
            float Read(object value, string name) => value == null ? 0 : Convert.ToSingle(AccessTools.Field(value.GetType(), name)?.GetValue(value) ?? 0);
            var threshold = Math.Max(Read(settings, "dotSize"), Read(settings, "hideDeadPawnsBelow"));
            var worldSettings = cameraPlusSettings == null ? null : AccessTools.Field(cameraPlusSettings, "settings")?.GetValue(null);
            if (worldSettings != null && AccessTools.Field(cameraPlusSettings, "dotConfigs")?.GetValue(worldSettings) is IEnumerable rules)
                foreach (var rule in rules) threshold = Math.Max(threshold, Read(rule, "showBelowPixels"));
            return threshold;
        }

        internal static object Status
        {
            get
            {
                var camera = Find.CameraDriver.MyCamera;
                var points = CombatFightService.CameraPawns.Select(p => camera.WorldToViewportPoint(p.DrawPos)).ToList();
                var inset = CombatFightService.OverlayEnabled ? (PanelWidth(UI.screenWidth) + 16) / UI.screenWidth : 0;
                var markerCount = showMarker == null || !Active ? 0 : CombatFightService.CameraPawns.Count(p => (bool)showMarker.Invoke(null, new object[] { p, null }));
                return new { active = Active, mode = director?.Mode.ToString(), shot = director?.Shot.ToString(), introductionStage = director?.IntroductionStage, focusId = director?.FocusId, opponentId = director?.OpponentId, reason = director?.Reason, switches = director?.Switches, compactPanels = CompactPanels, travelling, arenaCameraFailed, uiWidth = UI.screenWidth, uiHeight = UI.screenHeight, height = camera.transform.position.y, rootSize = Find.CameraDriver.RootSize, targetRootSize = rootSize, orthographicSize = camera.orthographicSize, maximumProjectionSize, maximumObservedProjectionSize, markerThreshold, cameraPlusMarkers = markerCount, cameraPlusEdgeMarkers = CameraPlusCombatCompatibility.EdgeMarkers(CombatFightService.CameraPawns), cameraPlusThresholdPatchReads = CameraPlusCombatCompatibility.ThresholdReads, cameraPlusEdgePatchReads = CameraPlusCombatCompatibility.EdgeReads, vanillaHighlightEnabled = SilhouetteUtility.CanHighlightAny(), uiCellSize = UI.CurUICellSize(), cameraFrames, limitViolations, x = camera.transform.position.x, z = camera.transform.position.z, living = CombatFightService.Pawns.Values.Count(p => p.Spawned && p.Map == CombatFightService.Map && !p.Dead), tracked = points.Count, visible = points.Count(p => p.z > 0 && p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1), betweenPanels = points.Count(p => p.z > 0 && p.x >= inset && p.x <= 1 - inset && p.y >= (director == null ? 90f : 160f) / UI.screenHeight && p.y <= 1 - (GroupCombatPresentation.PanelTop + 6) / UI.screenHeight) };
            }
        }
    }

    // Vanilla highlights start at 90% of config.sizeRange.max. A dedicated
    // camera at its own hard limit would otherwise highlight every pawn, even
    // with Camera+ markers disabled. This gates both vanilla dots and silhouettes.
    [HarmonyPatch(typeof(SilhouetteUtility), nameof(SilhouetteUtility.CanHighlightAny))]
    internal static class GroupCombatVanillaHighlights
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ref bool __result)
        {
            if (GroupCombatCamera.Active && Find.CurrentMap == CombatFightService.Map) __result = false;
        }
    }

    [HarmonyPatch(typeof(CameraDriver), nameof(CameraDriver.CameraDriverOnGUI))]
    internal static class GroupCombatCameraInput
    {
        private static bool Prefix() => !GroupCombatCamera.Active;
    }
}
