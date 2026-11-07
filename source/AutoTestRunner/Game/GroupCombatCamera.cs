using System.Linq;
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
        internal static bool Active => combatConfig != null;

        internal static void Begin()
        {
            End();
            previousConfig = Find.CameraDriver.config;
            combatConfig = new CameraMapConfig_Normal { dollyRateKeys = 0, dollyRateScreenEdge = 0, zoomSpeed = 0, moveSpeedScale = 0 };
            Find.CameraDriver.config = combatConfig;
            targetProjectionSize = 0; rootSize = 0;
            motion = new GroupCombatCameraMotion(); previousFrameTime = Time.realtimeSinceStartup;
            Postfix();
        }

        internal static void End()
        {
            if (combatConfig != null && Find.CameraDriver != null) Find.CameraDriver.config = previousConfig;
            combatConfig = null; previousConfig = null;
        }

        // Apply after the native camera update so manual dolly, selection and zoom cannot
        // displace the dedicated view. This does not consume mouse events or pause keys.
        private static void Postfix()
        {
            if (!Active || GroupCombatService.Map == null || Find.CurrentMap != GroupCombatService.Map) return;
            var pawns = GroupCombatService.Pawns.Values.Where(p => p.Spawned && p.Map == GroupCombatService.Map && !p.Dead).ToList();
            if (pawns.Count == 0) return; // Hold the last view for mutual elimination/final capture.
            var frame = GroupCombatPresentation.Frame(pawns.Select(p => (p.DrawPos.x, p.DrawPos.z)), UI.screenWidth, UI.screenHeight, GroupCombatService.OverlayEnabled);
            var position = new Vector3(frame.x, 0, frame.z);
            var nextProjectionSize = GroupCombatPresentation.ZoomTarget(targetProjectionSize, frame.size);
            if (rootSize == 0 || nextProjectionSize != targetProjectionSize)
            {
                targetProjectionSize = nextProjectionSize;
                // Camera mods can map RootSize nonlinearly to the actual projection. Fit
                // the real orthographic size instead of assuming RootSize is world units.
                var low = .01f; var high = Mathf.Max(60, targetProjectionSize * 2);
                combatConfig.sizeRange = new FloatRange(.01f, high + 10);
                for (var i = 0; i < 18; i++)
                {
                    var candidate = (low + high) / 2;
                    Find.CameraDriver.SetRootPosAndSize(position, candidate);
                    if (Find.CameraDriver.MyCamera.orthographicSize < targetProjectionSize) low = candidate; else high = candidate;
                }
                rootSize = high;
            }
            var now = Time.realtimeSinceStartup;
            motion.Step(frame.x, frame.z, rootSize, now - previousFrameTime);
            previousFrameTime = now;
            Find.CameraDriver.SetRootPosAndSize(new Vector3(motion.X, 0, motion.Z), motion.Size);
            // Weapon impacts must not shake this dedicated observation view.
            var camera = Find.CameraDriver.MyCamera;
            var stablePosition = camera.transform.position;
            stablePosition.x = motion.X; stablePosition.z = motion.Z;
            camera.transform.position = stablePosition;
        }

        internal static object Status
        {
            get
            {
                var camera = Find.CameraDriver.MyCamera;
                var points = GroupCombatService.Pawns.Values.Where(p => p.Spawned && p.Map == GroupCombatService.Map && !p.Dead).Select(p => camera.WorldToViewportPoint(p.DrawPos)).ToList();
                var inset = GroupCombatService.OverlayEnabled ? (GroupCombatPresentation.PanelWidth(UI.screenWidth) + 16) / UI.screenWidth : 0;
                return new { active = Active, rootSize = Find.CameraDriver.RootSize, targetRootSize = rootSize, orthographicSize = camera.orthographicSize, x = camera.transform.position.x, z = camera.transform.position.z, living = points.Count, visible = points.Count(p => p.z > 0 && p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1), betweenPanels = points.Count(p => p.z > 0 && p.x >= inset && p.x <= 1 - inset && p.y >= 90f / UI.screenHeight && p.y <= 1 - (GroupCombatPresentation.PanelTop + 6) / UI.screenHeight) };
            }
        }
    }

    [HarmonyPatch(typeof(CameraDriver), nameof(CameraDriver.CameraDriverOnGUI))]
    internal static class GroupCombatCameraInput
    {
        private static bool Prefix() => !GroupCombatCamera.Active;
    }
}
