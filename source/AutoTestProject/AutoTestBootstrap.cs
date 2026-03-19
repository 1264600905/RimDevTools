using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine.SceneManagement;
using Verse;

namespace LogSyncMod.AutoTest
{
    [StaticConstructorOnStartup]
    public static class AutoTestBootstrap
    {
        private const string HarmonyId = "trigger.logsyncmod.autotest";
        private const int StartupMapSettleTicks = 10;
        private static readonly FieldInfo QuickStartedField = typeof(QuickStarter).GetField("quickStarted", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly FieldInfo LogAutoOpenField = AccessTools.Field("LudeonTK.EditWindow_Log:canAutoOpen");
        private static bool startupReady;
        private static bool waitingForMapLogged;
        private static bool quickStartAttempted;
        private static int playableMapReadyTick = -1;
        private static bool? previousLogAutoOpenState;
        private static TimeSpeed? previousTimeSpeed;
        private static bool runtimeGuardsArmed;
        private static bool runtimeGuardsUsingSmartSpeed;
        private static TimeSpeed? lastObservedGuardSpeed;
        private static bool? lastObservedPausedState;
        private static bool? lastObservedForcePausedState;
        private static string lastObservedSmartSpeedState;
        private static bool temporaryPauseBypass;
        private static readonly AutoTestSmartSpeedBridge SmartSpeedBridge = new AutoTestSmartSpeedBridge();
        private static AutoTestRunner runner;

        static AutoTestBootstrap()
        {
            new Harmony(HarmonyId).PatchAll();
            LongEventHandler.ExecuteWhenFinished(() => startupReady = true);
        }

        public static void Tick()
        {
            if (!startupReady) return;

            LogSyncSettings settings = global::LogSyncMod.LogSyncMod.settings;
            if (settings == null) return;

            bool runRequested = settings.ConsumeAutoTestRunRequest();
            bool shouldAutoStart = settings.autoTestEnabled;
            if (!shouldAutoStart && !runRequested && runner == null) return;

            if (runner != null)
            {
                MaintainRuntimeGuards(settings, "Tick");
                runner.Tick();
                if (runner.Completed)
                {
                    RestoreLogAutoOpen();
                    RestoreRuntimeGuards();
                    runner = null;
                    settings.autoTestEnabled = false;
                    settings.Write();
                }
                return;
            }

            string packageId = (settings.autoTestTargetPackageId ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(packageId))
            {
                RestoreLogAutoOpen();
                Log.Warning("[LogSyncAutoTest] Missing target packageId. Auto test aborted.");
                settings.autoTestEnabled = false;
                settings.Write();
                return;
            }

            EnsureDeveloperMode();

            if (Current.ProgramState != ProgramState.Playing)
            {
                TryStartQuickTest();
                return;
            }

            Map map = Find.CurrentMap ?? Find.Maps.FirstOrDefault();
            if (map == null)
            {
                TryStartQuickTest();
                playableMapReadyTick = -1;
                if (!waitingForMapLogged)
                {
                    Log.Message("[LogSyncAutoTest] Waiting for a playable map.");
                    waitingForMapLogged = true;
                }
                return;
            }

            waitingForMapLogged = false;
            if (playableMapReadyTick < 0)
            {
                playableMapReadyTick = Find.TickManager.TicksGame;
                Log.Message("[LogSyncAutoTest] Playable map detected. Waiting " + StartupMapSettleTicks + " ticks before starting auto test.");
                return;
            }

            if (Find.TickManager.TicksGame - playableMapReadyTick < StartupMapSettleTicks)
            {
                return;
            }

            DisableLogAutoOpenForAutoTest();
            ArmRuntimeGuards(settings);
            runner = new AutoTestRunner(settings, map);
            runner.Initialize();
            playableMapReadyTick = -1;
        }

        public static void OnGui()
        {
            LogSyncSettings settings = global::LogSyncMod.LogSyncMod.settings;
            if (settings != null)
            {
                MaintainRuntimeGuards(settings, "OnGUI");
            }

            runner?.ProcessUi();
        }

        public static bool ShouldForceAbilityForAutoTest(RimWorld.Ability ability)
        {
            if (runner == null || ability == null)
            {
                return false;
            }

            try
            {
                return runner.ShouldForceAbilityForSkillTest(ability);
            }
            catch (Exception ex)
            {
                Log.Warning("[LogSyncAutoTest] Failed to evaluate force-ability state: " + ex.Message);
                return false;
            }
        }

        private static void EnsureDeveloperMode()
        {
            if (!Prefs.DevMode)
            {
                Prefs.DevMode = true;
                Log.Message("[LogSyncAutoTest] Enabled developer mode.");
            }
        }

        private static void TryStartQuickTest()
        {
            if (quickStartAttempted) return;

            quickStartAttempted = true;
            bool started = false;

            try
            {
                if (GenScene.InEntryScene)
                {
                    QuickStartedField?.SetValue(null, true);
                    SceneManager.LoadScene("Play");
                    started = true;
                }
                else
                {
                    started = QuickStarter.CheckQuickStart();
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning("[LogSyncAutoTest] QuickStart failed: " + ex);
            }

            Log.Message("[LogSyncAutoTest] QuickStart requested. Result=" + started);
        }

        private static void DisableLogAutoOpenForAutoTest()
        {
            if (previousLogAutoOpenState.HasValue || LogAutoOpenField == null)
            {
                return;
            }

            try
            {
                previousLogAutoOpenState = (bool)LogAutoOpenField.GetValue(null);
                LogAutoOpenField.SetValue(null, false);
                Log.Message("[LogSyncAutoTest] Disabled debug log auto-open during auto test.");
            }
            catch (System.Exception ex)
            {
                previousLogAutoOpenState = null;
                Log.Warning("[LogSyncAutoTest] Failed to disable debug log auto-open: " + ex.Message);
            }
        }

        private static void RestoreLogAutoOpen()
        {
            if (!previousLogAutoOpenState.HasValue || LogAutoOpenField == null)
            {
                return;
            }

            try
            {
                LogAutoOpenField.SetValue(null, previousLogAutoOpenState.Value);
            }
            catch (System.Exception ex)
            {
                Log.Warning("[LogSyncAutoTest] Failed to restore debug log auto-open: " + ex.Message);
            }
            finally
            {
                previousLogAutoOpenState = null;
            }
        }

        private static void ArmRuntimeGuards(LogSyncSettings settings)
        {
            if (runtimeGuardsArmed)
            {
                return;
            }

            TickManager tickManager = Find.TickManager;
            if (tickManager == null)
            {
                return;
            }

            runtimeGuardsUsingSmartSpeed = false;
            if (SmartSpeedBridge.TryArm(out string smartSpeedDetails))
            {
                runtimeGuardsArmed = true;
                runtimeGuardsUsingSmartSpeed = true;
                previousTimeSpeed = null;
                lastObservedGuardSpeed = null;
                lastObservedPausedState = null;
                lastObservedForcePausedState = null;
                lastObservedSmartSpeedState = null;
                Log.Message("[LogSyncAutoTest] Runtime guards armed via SmartSpeed. " + smartSpeedDetails);
                return;
            }

            previousTimeSpeed = tickManager.CurTimeSpeed;
            runtimeGuardsArmed = settings.autoTestPreventPause || settings.autoTestUseFastSpeed;
            lastObservedGuardSpeed = null;
            lastObservedPausedState = null;
            lastObservedForcePausedState = null;

            if (runtimeGuardsArmed)
            {
                Log.Warning("[LogSyncAutoTest] SmartSpeed bridge unavailable, falling back to local runtime guards.");
                Log.Message("[LogSyncAutoTest] Runtime guards armed. preventPause=" + settings.autoTestPreventPause +
                    " useFastSpeed=" + settings.autoTestUseFastSpeed +
                    " previousSpeed=" + previousTimeSpeed.Value);
            }
        }

        private static void RestoreRuntimeGuards()
        {
            if (!runtimeGuardsArmed)
            {
                previousTimeSpeed = null;
                return;
            }

            if (runtimeGuardsUsingSmartSpeed)
            {
                SmartSpeedBridge.Restore(out string smartSpeedDetails);
                Log.Message("[LogSyncAutoTest] Runtime guards restored SmartSpeed state: " + smartSpeedDetails);
                previousTimeSpeed = null;
                runtimeGuardsArmed = false;
                runtimeGuardsUsingSmartSpeed = false;
                temporaryPauseBypass = false;
                SmartSpeedBridge.SetPauseBypass(false);
                lastObservedGuardSpeed = null;
                lastObservedPausedState = null;
                lastObservedForcePausedState = null;
                lastObservedSmartSpeedState = null;
                return;
            }

            try
            {
                TickManager tickManager = Find.TickManager;
                if (tickManager != null && previousTimeSpeed.HasValue)
                {
                    tickManager.CurTimeSpeed = previousTimeSpeed.Value;
                    Log.Message("[LogSyncAutoTest] Runtime guards restored previous speed: " + previousTimeSpeed.Value);
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning("[LogSyncAutoTest] Failed to restore runtime guard state: " + ex.Message);
            }
            finally
            {
                previousTimeSpeed = null;
                runtimeGuardsArmed = false;
                temporaryPauseBypass = false;
                lastObservedGuardSpeed = null;
                lastObservedPausedState = null;
                lastObservedForcePausedState = null;
                lastObservedSmartSpeedState = null;
            }
        }

        public static void SetTemporaryPauseBypass(bool active)
        {
            temporaryPauseBypass = active;
            SmartSpeedBridge.SetPauseBypass(active);
        }

        private static void MaintainRuntimeGuards(LogSyncSettings settings, string source)
        {
            if (!runtimeGuardsArmed || runner == null)
            {
                return;
            }

            if (runtimeGuardsUsingSmartSpeed)
            {
                bool adjusted = SmartSpeedBridge.TryMaintain(out string smartSpeedDetails);
                if (adjusted)
                {
                    Log.Message("[LogSyncAutoTest] Runtime guard adjusted state via SmartSpeed/" + source + ": " + smartSpeedDetails);
                }
                else if (!string.Equals(smartSpeedDetails, lastObservedSmartSpeedState, StringComparison.Ordinal))
                {
                    Log.Message("[LogSyncAutoTest] Runtime guard observed state via SmartSpeed/" + source + ": " + smartSpeedDetails);
                }

                TickManager smartSpeedTickManager = Find.TickManager;
                lastObservedGuardSpeed = smartSpeedTickManager?.CurTimeSpeed;
                lastObservedPausedState = smartSpeedTickManager?.Paused;
                lastObservedForcePausedState = smartSpeedTickManager?.ForcePaused;
                lastObservedSmartSpeedState = smartSpeedDetails;
                return;
            }

            TickManager tickManager = Find.TickManager;
            if (tickManager == null)
            {
                return;
            }

            bool paused = tickManager.Paused;
            bool forcePaused = tickManager.ForcePaused;
            TimeSpeed currentSpeed = tickManager.CurTimeSpeed;
            TimeSpeed desiredSpeed = settings.autoTestUseFastSpeed ? TimeSpeed.Fast : TimeSpeed.Normal;

            if (temporaryPauseBypass)
            {
                lastObservedPausedState = paused;
                lastObservedForcePausedState = forcePaused;
                lastObservedGuardSpeed = currentSpeed;
                return;
            }

            bool shouldAdjustSpeed = false;
            if (settings.autoTestPreventPause && paused)
            {
                shouldAdjustSpeed = true;
            }

            if (settings.autoTestUseFastSpeed && currentSpeed != desiredSpeed)
            {
                shouldAdjustSpeed = true;
            }

            if (shouldAdjustSpeed)
            {
                tickManager.CurTimeSpeed = desiredSpeed;
                TimeSpeed updatedSpeed = tickManager.CurTimeSpeed;
                bool updatedPaused = tickManager.Paused;
                bool updatedForcePaused = tickManager.ForcePaused;
                Log.Message("[LogSyncAutoTest] Runtime guard adjusted state via " + source +
                    ": paused=" + paused +
                    ", forcePaused=" + forcePaused +
                    ", speed=" + currentSpeed +
                    " -> paused=" + updatedPaused +
                    ", forcePaused=" + updatedForcePaused +
                    ", speed=" + updatedSpeed);
                paused = updatedPaused;
                forcePaused = updatedForcePaused;
                currentSpeed = updatedSpeed;
            }
            else if (paused != lastObservedPausedState ||
                     forcePaused != lastObservedForcePausedState ||
                     currentSpeed != lastObservedGuardSpeed)
            {
                Log.Message("[LogSyncAutoTest] Runtime guard observed state via " + source +
                    ": paused=" + paused +
                    ", forcePaused=" + forcePaused +
                    ", speed=" + currentSpeed);
            }

            lastObservedPausedState = paused;
            lastObservedForcePausedState = forcePaused;
            lastObservedGuardSpeed = currentSpeed;
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.DoSingleTick))]
    public static class TickManager_DoSingleTick_AutoTestPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            AutoTestBootstrap.Tick();
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.WindowStackOnGUI))]
    public static class WindowStack_WindowStackOnGUI_AutoTestPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            AutoTestBootstrap.OnGui();
        }
    }

    [HarmonyPatch(typeof(WindowStack), "CloseWindowsBecauseClicked")]
    public static class WindowStack_CloseWindowsBecauseClicked_AutoTestTracePatch
    {
        [HarmonyPostfix]
        public static void Postfix(bool __result)
        {
            if (__result)
            {
                InfoCardCloseTrace.AddReason("CloseWindowsBecauseClicked");
            }
        }
    }

    [HarmonyPatch(typeof(Window), nameof(Window.OnCancelKeyPressed))]
    public static class Window_OnCancelKeyPressed_AutoTestTracePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Window __instance)
        {
            if (InfoCardCloseTrace.Matches(__instance))
            {
                InfoCardCloseTrace.AddReason("OnCancelKeyPressed");
            }
        }
    }

    [HarmonyPatch(typeof(Window), nameof(Window.OnAcceptKeyPressed))]
    public static class Window_OnAcceptKeyPressed_AutoTestTracePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Window __instance)
        {
            if (InfoCardCloseTrace.Matches(__instance))
            {
                InfoCardCloseTrace.AddReason("OnAcceptKeyPressed");
            }
        }
    }

    [HarmonyPatch(typeof(Window), nameof(Window.Close))]
    public static class Window_Close_AutoTestTracePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Window __instance)
        {
            if (InfoCardCloseTrace.Matches(__instance))
            {
                InfoCardCloseTrace.AddReason("Window.Close");
            }
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.TryRemove), new[] { typeof(Window), typeof(bool) })]
    public static class WindowStack_TryRemove_AutoTestTracePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Window window)
        {
            if (InfoCardCloseTrace.Matches(window))
            {
                InfoCardCloseTrace.AddReason("WindowStack.TryRemove");
            }
        }
    }

    [HarmonyPatch(typeof(WindowStack), "RemoveWindowsOfType")]
    public static class WindowStack_RemoveWindowsOfType_AutoTestTracePatch
    {
        [HarmonyPrefix]
        public static void Prefix(System.Type type)
        {
            if (type == typeof(Dialog_InfoCard) && InfoCardCloseTrace.IsTracking)
            {
                InfoCardCloseTrace.AddReason("WindowStack.RemoveWindowsOfType(Dialog_InfoCard)");
            }
        }
    }

    [HarmonyPatch(typeof(RimWorld.Ability), nameof(RimWorld.Ability.GizmoDisabled))]
    public static class Ability_GizmoDisabled_AutoTestForcePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(RimWorld.Ability __instance, ref bool __result, ref string reason)
        {
            if (!AutoTestBootstrap.ShouldForceAbilityForAutoTest(__instance))
            {
                return true;
            }

            __result = false;
            reason = string.Empty;
            return false;
        }
    }

    [HarmonyPatch(typeof(RimWorld.Ability), "get_CanCast")]
    public static class Ability_CanCast_AutoTestForcePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(RimWorld.Ability __instance, ref AcceptanceReport __result)
        {
            if (!AutoTestBootstrap.ShouldForceAbilityForAutoTest(__instance))
            {
                return true;
            }

            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(RimWorld.Ability), "get_CanQueueCast")]
    public static class Ability_CanQueueCast_AutoTestForcePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(RimWorld.Ability __instance, ref bool __result)
        {
            if (!AutoTestBootstrap.ShouldForceAbilityForAutoTest(__instance))
            {
                return true;
            }

            __result = __instance.pawn?.jobs != null;
            return false;
        }
    }
}
