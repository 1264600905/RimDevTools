using System;
using System.Collections;
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
            Postfix();
        }

        internal static void End()
        {
            if (combatConfig != null && Find.CameraDriver != null) Find.CameraDriver.config = previousConfig;
            combatConfig = null; previousConfig = null;
            CameraPlusCombatCompatibility.ClearCache();
        }

        // Apply after the native camera update so manual dolly, selection and zoom cannot
        // displace the dedicated view. This does not consume mouse events or pause keys.
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            if (!Active || CombatFightService.Map == null || Find.CurrentMap != CombatFightService.Map) return;
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
            var low = .01f; var high = Mathf.Max(60, projection * 2);
            combatConfig.sizeRange = new FloatRange(.01f, high + 10);
            for (var i = 0; i < 18; i++)
            {
                var candidate = (low + high) / 2;
                Find.CameraDriver.SetRootPosAndSize(position, candidate);
                if (Find.CameraDriver.MyCamera.orthographicSize <= projection) low = candidate; else high = candidate;
            }
            return low;
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
                var inset = CombatFightService.OverlayEnabled ? (GroupCombatPresentation.PanelWidth(UI.screenWidth) + 16) / UI.screenWidth : 0;
                var markerCount = showMarker == null || !Active ? 0 : CombatFightService.CameraPawns.Count(p => (bool)showMarker.Invoke(null, new object[] { p, null }));
                return new { active = Active, rootSize = Find.CameraDriver.RootSize, targetRootSize = rootSize, orthographicSize = camera.orthographicSize, maximumProjectionSize, maximumObservedProjectionSize, markerThreshold, cameraPlusMarkers = markerCount, cameraPlusEdgeMarkers = CameraPlusCombatCompatibility.EdgeMarkers(CombatFightService.CameraPawns), cameraPlusThresholdPatchReads = CameraPlusCombatCompatibility.ThresholdReads, cameraPlusEdgePatchReads = CameraPlusCombatCompatibility.EdgeReads, vanillaHighlightEnabled = SilhouetteUtility.CanHighlightAny(), uiCellSize = UI.CurUICellSize(), cameraFrames, limitViolations, x = camera.transform.position.x, z = camera.transform.position.z, living = CombatFightService.Pawns.Values.Count(p => p.Spawned && p.Map == CombatFightService.Map && !p.Dead), tracked = points.Count, visible = points.Count(p => p.z > 0 && p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1), betweenPanels = points.Count(p => p.z > 0 && p.x >= inset && p.x <= 1 - inset && p.y >= 90f / UI.screenHeight && p.y <= 1 - (GroupCombatPresentation.PanelTop + 6) / UI.screenHeight) };
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
