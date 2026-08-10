using System;
using System.Linq;
using System.Collections.Generic;
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
        private const int StartupReadinessTimeoutMs = 120000;
        private static readonly FieldInfo QuickStartedField = typeof(QuickStarter).GetField("quickStarted", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly FieldInfo LogAutoOpenField = AccessTools.Field("LudeonTK.EditWindow_Log:canAutoOpen");
        private static bool startupReady;
        private static bool waitingForMapLogged;
        private static bool quickStartAttempted;
        private static AutoTestWaitHandle readinessWait;
        private static AutoTestOperationJournal readinessJournal;
        private static AutoTestGameClock readinessClock;
        private static AutoTestReadinessSnapshot readinessSnapshot;
        private static readonly AutoTestRuntimeHistory RuntimeHistory = new AutoTestRuntimeHistory();
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
                    RuntimeHistory.Retain(runner.RuntimeRunId, runner.GetRuntimeEvidenceSnapshot());
                    RestoreLogAutoOpen();
                    RestoreRuntimeGuards();
                    runner = null;
                    settings.autoTestEnabled = false;
                    settings.Write();
                    readinessWait = null;
                    readinessJournal = null;
                    readinessClock = null;
                    readinessSnapshot = null;
                }
                return;
            }

            string packageId = (settings.autoTestTargetPackageId ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(packageId))
            {
                RestoreLogAutoOpen();
                global::LogSyncMod.TestAudioController.Restore();
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
                readinessWait = null;
                if (!waitingForMapLogged)
                {
                    Log.Message("[LogSyncAutoTest] Waiting for a playable map.");
                    waitingForMapLogged = true;
                }
                return;
            }

            waitingForMapLogged = false;
            if (readinessWait == null)
            {
                readinessClock = new AutoTestGameClock();
                readinessJournal = new AutoTestOperationJournal();
                readinessWait = new AutoTestWaitHandle(
                    new AutoTestWaitOptions { ConditionId = "automation.ready", TimeoutMs = StartupReadinessTimeoutMs, MinimumPollIntervalTicks = 1, RequiredStablePolls = 3 },
                    delegate
                    {
                        readinessSnapshot = AutoTestReadinessProbe.Capture();
                        return new AutoTestProbeResult { Satisfied = readinessSnapshot.AutomationReady, Snapshot = readinessSnapshot.Summary, Message = readinessSnapshot.Summary };
                    },
                    readinessClock,
                    readinessJournal);
            }

            AutoTestWaitOutcome readinessOutcome = readinessWait.Poll(Find.TickManager?.TicksGame ?? 0);
            if (!readinessOutcome.Terminal)
            {
                if (readinessOutcome.Attempts == 1 || readinessOutcome.Attempts % 60 == 0)
                    Log.Message("[LogSyncAutoTest] Waiting for automation.ready: " + readinessOutcome.LastSnapshot);
                return;
            }

            if (!readinessOutcome.Satisfied)
            {
                readinessSnapshot = AutoTestReadinessProbe.Capture();
                Log.Error("[LogSyncAutoTest] automation.ready timed out: " + AutoTestWaitHandle.Describe(readinessOutcome));
                AutoTestRunner timedOutRunner = new AutoTestRunner(settings, map, readinessSnapshot);
                timedOutRunner.Initialize();
                RuntimeHistory.Retain(timedOutRunner.RuntimeRunId, timedOutRunner.GetRuntimeEvidenceSnapshot());
                settings.autoTestEnabled = false;
                settings.Write();
                readinessWait = null;
                readinessJournal = null;
                readinessClock = null;
                return;
            }

            DisableLogAutoOpenForAutoTest();
            ArmRuntimeGuards(settings);
            runner = new AutoTestRunner(settings, map, readinessSnapshot);
            runner.Initialize();
            readinessWait = null;
            readinessJournal = null;
            readinessClock = null;
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

        internal static AutoTestRuntimeStatusDto GetRuntimeStatus()
        {
            if (runner != null)
            {
                return new AutoTestRuntimeStatusDto { State = runner.Completed ? "completed" : "running", RunId = runner.RuntimeRunId, PackageId = runner.RuntimePackageId, Completed = runner.Completed, ResultCount = runner.RuntimeResultCount, Summary = "active run" };
            }
            LogSyncSettings settings = global::LogSyncMod.LogSyncMod.settings;
            if (settings != null && settings.autoTestEnabled)
            {
                return new AutoTestRuntimeStatusDto { State = "queued", RunId = settings.autoTestRunId, PackageId = settings.autoTestTargetPackageId, Summary = "run requested but not started" };
            }
            AutoTestEvidenceSnapshotDto lastEvidenceSnapshot = RuntimeHistory.LastSnapshot;
            string lastRuntimeRunId = lastEvidenceSnapshot?.RunId;
            return new AutoTestRuntimeStatusDto { State = string.IsNullOrEmpty(lastRuntimeRunId) ? "idle" : "completed", RunId = lastRuntimeRunId, Completed = lastEvidenceSnapshot?.Found == true, ResultCount = lastEvidenceSnapshot?.CaseCount ?? 0, Summary = "no active run" };
        }

        internal static AutoTestRunRequestResultDto RequestRuntimeRun(AutoTestRunRequestDto request)
        {
            request = request ?? new AutoTestRunRequestDto();
            if (runner != null || (global::LogSyncMod.LogSyncMod.settings?.autoTestEnabled ?? false))
                return new AutoTestRunRequestResultDto { Accepted = false, ErrorCode = "autotest.run_already_active", Message = "An auto-test run is already active or queued." };
            string packageId = (request.PackageId ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(packageId)) return new AutoTestRunRequestResultDto { Accepted = false, ErrorCode = "autotest.package_id_required", Message = "packageId is required." };
            LogSyncSettings settings = global::LogSyncMod.LogSyncMod.settings;
            if (settings == null) return new AutoTestRunRequestResultDto { Accepted = false, ErrorCode = "autotest.settings_unavailable", Message = "LogSync settings are unavailable." };
            List<AutoTestScope> scopes = new List<AutoTestScope>();
            foreach (string scopeText in request.Scopes ?? new List<string>())
                if (Enum.TryParse(scopeText, true, out AutoTestScope scope) && !scopes.Contains(scope)) scopes.Add(scope);
            if (scopes.Count == 0) scopes.Add(AutoTestScope.Standard);
            settings.autoTestTargetPackageId = packageId;
            settings.autoTestRunId = string.IsNullOrWhiteSpace(request.RunId) ? Guid.NewGuid().ToString("N") : request.RunId.Trim();
            settings.autoTestScopes = scopes;
            settings.autoTestScope = scopes[0];
            settings.autoTestTargetDefNames = (request.DefNames ?? new List<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            settings.autoTestTargetDefNamesInput = string.Join(",", settings.autoTestTargetDefNames.ToArray());
            settings.autoTestCaptureScreenshots = request.CaptureScreenshots;
            settings.autoTestMode = string.Equals(request.Mode, "xml", StringComparison.OrdinalIgnoreCase) ? AutoTestMode.XmlDriven : AutoTestMode.Automatic;
            if (!string.IsNullOrWhiteSpace(request.OutputPath)) settings.autoTestOutputPath = request.OutputPath.Trim();
            settings.autoTestEnabled = true;
            settings.RequestAutoTestRun();
            settings.Write();
            return new AutoTestRunRequestResultDto { Accepted = true, RunId = settings.autoTestRunId, Message = "Run queued for the next game tick." };
        }

        internal static AutoTestCancelResultDto RequestRuntimeCancel(string runId)
        {
            if (runner != null && string.Equals(runner.RuntimeRunId, runId, StringComparison.OrdinalIgnoreCase))
            {
                runner.RequestCancel(runId);
                return new AutoTestCancelResultDto { Accepted = true, RunId = runId, Message = "Cancellation queued on the game thread." };
            }
            LogSyncSettings settings = global::LogSyncMod.LogSyncMod.settings;
            if (settings != null && AutoTestRuntimeQueue.MatchesQueuedRun(runId, settings.autoTestRunId, settings.autoTestEnabled))
            {
                settings.ConsumeAutoTestRunRequest();
                settings.autoTestEnabled = false;
                settings.Write();
                readinessWait = null;
                readinessJournal = null;
                readinessClock = null;
                readinessSnapshot = null;
                return new AutoTestCancelResultDto { Accepted = true, RunId = runId, Message = "Queued run cancelled before it started." };
            }
            return new AutoTestCancelResultDto { Accepted = false, RunId = runId, ErrorCode = "autotest.run_not_found", Message = "No active run matches runId." };
        }

        internal static AutoTestEvidenceSnapshotDto GetRuntimeEvidence(string runId)
        {
            if (runner != null && string.Equals(runner.RuntimeRunId, runId, StringComparison.OrdinalIgnoreCase)) return runner.GetRuntimeEvidenceSnapshot();
            AutoTestEvidenceSnapshotDto retained = RuntimeHistory.GetEvidence(runId);
            if (retained != null) return retained;
            return new AutoTestEvidenceSnapshotDto { Found = false, RunId = runId, ErrorCode = "autotest.run_not_found" };
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
