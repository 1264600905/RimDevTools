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
        private static readonly FieldInfo QuickStartedField = typeof(QuickStarter).GetField("quickStarted", BindingFlags.NonPublic | BindingFlags.Static);
        private static bool startupReady;
        private static bool waitingForMapLogged;
        private static bool quickStartAttempted;
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
                runner.Tick();
                if (runner.Completed)
                {
                    runner = null;
                    settings.autoTestEnabled = false;
                    settings.Write();
                }
                return;
            }

            string packageId = (settings.autoTestTargetPackageId ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(packageId))
            {
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
                if (!waitingForMapLogged)
                {
                    Log.Message("[LogSyncAutoTest] Waiting for a playable map.");
                    waitingForMapLogged = true;
                }
                return;
            }

            waitingForMapLogged = false;
            runner = new AutoTestRunner(settings, map);
            runner.Initialize();
        }

        public static void OnGui()
        {
            runner?.ProcessUi();
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
}
