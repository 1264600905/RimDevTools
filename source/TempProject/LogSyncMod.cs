using UnityEngine;
using Verse;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using HarmonyLib;

namespace LogSyncMod
{
    [StaticConstructorOnStartup]
    public class LogSyncMod : Mod
    {
        public static LogSyncSettings settings;
        private static bool hasLoggedLoadedMods;
        private static System.Timers.Timer windowModeTimer;
        private const int GwlStyle = -16;
        private const long WsCaption = 0x00C00000L;
        private const long WsThickFrame = 0x00040000L;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        static LogSyncMod()
        {
            // 在所有加载完成（进入主菜单）时执行
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                TestAudioController.ArmIfRequested();
                Log.Message("[LogSyncMod] INITIALIZATION COMPLETED.");
                LogLoadedMods();
                ScheduleWindowModeLog();

                // Auto/XML 由 AutoTestRunner 在完成信号前恢复；只有 quick 使用 ready timer。
                if (settings == null || !settings.autoTestEnabled)
                {
                    var timer = new System.Timers.Timer(10000);
                    timer.Elapsed += (s, e) =>
                    {
                        TestAudioController.Restore("quick_ready");
                        Log.Message("[LogSyncMod] RUNTIME_READY_TO_CLOSE");
                        timer.Stop();
                        timer.Dispose();
                    };
                    timer.Start();
                }
            });
        }

        public LogSyncMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<LogSyncSettings>();
            Log.Message("[LogSyncMod] STARTING INITIALIZATION...");
            var harmony = new Harmony("trigger.logsyncmod");
            harmony.PatchAll();
            LogPatches.InitializeFile();
            // 内置冒烟测试默认停用，保留代码仅用于后续手动调试。
            // LogSyncSmokeTest.Schedule();
        }

        private static void ScheduleWindowModeLog()
        {
            windowModeTimer = new System.Timers.Timer(3000) { AutoReset = false };
            windowModeTimer.Elapsed += (sender, args) =>
            {
                try
                {
                    LogWindowMode();
                }
                finally
                {
                    windowModeTimer.Dispose();
                    windowModeTimer = null;
                }
            };
            windowModeTimer.Start();
        }

        private static void LogWindowMode()
        {
            IntPtr windowHandle = Process.GetCurrentProcess().MainWindowHandle;
            if (windowHandle == IntPtr.Zero)
            {
                Log.Warning("[LogSyncMod] WindowMode: unable to resolve the RimWorld main window handle.");
                return;
            }

            long style = GetWindowLongPtr(windowHandle, GwlStyle).ToInt64();
            bool hasCaption = (style & WsCaption) != 0;
            bool hasThickFrame = (style & WsThickFrame) != 0;
            bool borderless = !hasCaption && !hasThickFrame;
            Log.Message(
                "[LogSyncMod] WindowMode: " +
                "resolution=" + Screen.width + "x" + Screen.height +
                " fullscreen=" + Screen.fullScreen +
                " fullscreenMode=" + Screen.fullScreenMode +
                " caption=" + hasCaption +
                " thickFrame=" + hasThickFrame +
                " borderless=" + borderless + ".");
        }

        private static void LogLoadedMods()
        {
            if (hasLoggedLoadedMods)
            {
                return;
            }

            hasLoggedLoadedMods = true;

            StringBuilder builder = new StringBuilder();
            builder.Append("Loaded mods:");

            int index = 1;
            foreach (ModContentPack mod in LoadedModManager.RunningMods)
            {
                string modName = string.IsNullOrWhiteSpace(mod?.Name) ? "<unnamed mod>" : mod.Name;
                string packageId = string.IsNullOrWhiteSpace(mod?.PackageIdPlayerFacing) ? "<unknown packageId>" : mod.PackageIdPlayerFacing;
                builder.AppendLine();
                builder.Append(index);
                builder.Append(". ");
                builder.Append(modName);
                builder.Append(" | ");
                builder.Append(packageId);
                index++;
            }

            LogUtils.Message(builder.ToString());
        }

        public override string SettingsCategory()
        {
            return "LogSyncMod.SettingsCategory".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);

            listingStandard.Label("LogSyncMod.LogPathLabel".Translate());
            settings.logPath = listingStandard.TextEntry(settings.logPath);
            
            if (listingStandard.ButtonText("LogSyncMod.ValidatePathButton".Translate()))
            {
                ValidateLogPath();
            }
            listingStandard.Gap();

            listingStandard.Label("LogSyncMod.TargetModIdsLabel".Translate());
            settings.targetModIdsInput = listingStandard.TextEntry(settings.targetModIdsInput);
            
            if (listingStandard.ButtonText("LogSyncMod.SelectModButton".Translate()))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                foreach (var mod in LoadedModManager.RunningMods)
                {
                    string pId = mod.PackageIdPlayerFacing;
                    options.Add(new FloatMenuOption($"{mod.Name} ({pId})", () =>
                    {
                        string pIdLower = pId.ToLower();
                        if (!settings.targetModIds.Contains(pIdLower))
                        {
                            settings.targetModIds.Add(pIdLower);
                            settings.targetModIdsInput = string.Join(",", settings.targetModIds);
                            settings.Write();
                        }
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            
            if (listingStandard.ButtonText("LogSyncMod.ClearModListButton".Translate()))
            {
                settings.targetModIds.Clear();
                settings.targetModIdsInput = "";
                settings.Write();
            }
            listingStandard.Gap();

            listingStandard.Label("LogSyncMod.KeywordsLabel".Translate());
            settings.keywordsInput = listingStandard.TextEntry(settings.keywordsInput);
            listingStandard.Gap();

            listingStandard.CheckboxLabeled("LogSyncMod.SyncMessage".Translate(), ref settings.syncMessage);
            listingStandard.CheckboxLabeled("LogSyncMod.SyncWarning".Translate(), ref settings.syncWarning);
            listingStandard.CheckboxLabeled("LogSyncMod.SyncError".Translate(), ref settings.syncError);
            listingStandard.Gap();
            listingStandard.CheckboxLabeled("LogSyncMod.EnableVerboseDebug".Translate(), ref settings.enableVerboseDebug);
            listingStandard.Gap();

            listingStandard.Label("LogSyncMod.AutoTestSection".Translate());
            listingStandard.CheckboxLabeled("LogSyncMod.AutoTestEnabled".Translate(), ref settings.autoTestEnabled);
            listingStandard.Label("LogSyncMod.AutoTestTargetPackageId".Translate());
            settings.autoTestTargetPackageId = listingStandard.TextEntry(settings.autoTestTargetPackageId);
            listingStandard.Label("LogSyncMod.AutoTestOutputPath".Translate());
            settings.autoTestOutputPath = listingStandard.TextEntry(settings.autoTestOutputPath);
            string modeLabel = GetAutoTestModeLabel(settings.autoTestMode);
            listingStandard.Label("LogSyncMod.AutoTestMode".Translate(modeLabel));
            if (listingStandard.ButtonText("LogSyncMod.AutoTestModeSelect".Translate(modeLabel)))
            {
                List<FloatMenuOption> modeOptions = new List<FloatMenuOption>
                {
                    new FloatMenuOption("LogSyncMod.AutoTestModeAutomatic".Translate(), () => settings.autoTestMode = AutoTestMode.Automatic),
                    new FloatMenuOption("LogSyncMod.AutoTestModeXmlDriven".Translate(), () => settings.autoTestMode = AutoTestMode.XmlDriven)
                };
                Find.WindowStack.Add(new FloatMenu(modeOptions));
            }
            listingStandard.Label("LogSyncMod.AutoTestScopes".Translate());
            DrawAutoTestScopeCheckbox(listingStandard, AutoTestScope.Standard, "LogSyncMod.AutoTestScopeStandard".Translate());
            DrawAutoTestScopeCheckbox(listingStandard, AutoTestScope.UI, "LogSyncMod.AutoTestScopeUI".Translate());
            DrawAutoTestScopeCheckbox(listingStandard, AutoTestScope.Combat, "LogSyncMod.AutoTestScopeCombat".Translate());
            DrawAutoTestScopeCheckbox(listingStandard, AutoTestScope.View, "LogSyncMod.AutoTestScopeView".Translate());
            DrawAutoTestScopeCheckbox(listingStandard, AutoTestScope.Misc, "LogSyncMod.AutoTestScopeMisc".Translate());
            listingStandard.Label("LogSyncMod.AutoTestTargetDefNames".Translate());
            bool targetNamesEnabled = settings.autoTestMode == AutoTestMode.Automatic;
            GUI.enabled = targetNamesEnabled;
            settings.autoTestTargetDefNamesInput = listingStandard.TextEntry(settings.autoTestTargetDefNamesInput);
            GUI.enabled = true;
            if (!targetNamesEnabled)
            {
                listingStandard.Label("LogSyncMod.AutoTestTargetDefNamesXmlHint".Translate());
            }
            listingStandard.CheckboxLabeled("LogSyncMod.AutoTestCaptureScreenshots".Translate(), ref settings.autoTestCaptureScreenshots);
            listingStandard.CheckboxLabeled("LogSyncMod.AutoTestPreventPause".Translate(), ref settings.autoTestPreventPause);
            listingStandard.CheckboxLabeled("LogSyncMod.AutoTestUseFastSpeed".Translate(), ref settings.autoTestUseFastSpeed);
            listingStandard.Label("LogSyncMod.AutoTestBatchSize".Translate(settings.autoTestBatchSize));
            string batchSizeInput = listingStandard.TextEntry(settings.autoTestBatchSize.ToString());
            if (int.TryParse(batchSizeInput, out int parsedBatchSize))
            {
                settings.autoTestBatchSize = parsedBatchSize;
            }
            listingStandard.Label("LogSyncMod.AutoTestPocketMapMaxConcurrent".Translate(settings.autoTestPocketMapMaxConcurrent));
            string pocketMapMaxConcurrentInput = listingStandard.TextEntry(settings.autoTestPocketMapMaxConcurrent.ToString());
            if (int.TryParse(pocketMapMaxConcurrentInput, out int parsedPocketMapMaxConcurrent))
            {
                settings.autoTestPocketMapMaxConcurrent = parsedPocketMapMaxConcurrent;
            }
            if (listingStandard.ButtonText("LogSyncMod.AutoTestRunNow".Translate()))
            {
                settings.ApplyInputs();
                settings.RequestAutoTestRun();
                settings.Write();
                Messages.Message("LogSyncMod.AutoTestRunRequested".Translate(), MessageTypeDefOf.PositiveEvent, false);
            }
            listingStandard.Gap();

            listingStandard.Gap();
            if (listingStandard.ButtonText("LogSyncMod.ApplyAndSave".Translate()))
            {
                settings.ApplyInputs();
                settings.Write();
                Messages.Message("LogSyncMod.SettingsApplied".Translate(), MessageTypeDefOf.PositiveEvent, false);
            }

            if (listingStandard.ButtonText("LogSyncMod.OpenLogFolder".Translate()))
            {
                if (!string.IsNullOrEmpty(settings.logPath))
                {
                    string dir = System.IO.Path.GetDirectoryName(settings.logPath);
                    if (System.IO.Directory.Exists(dir))
                    {
                        Application.OpenURL("file://" + dir);
                    }
                    else
                    {
                        Messages.Message("LogSyncMod.DirectoryNotFound".Translate(), MessageTypeDefOf.RejectInput, false);
                    }
                }
            }

            listingStandard.End();
        }

        private void ValidateLogPath()
        {
            try
            {
                string path = settings.logPath;
                if (string.IsNullOrEmpty(path))
                {
                    Messages.Message("LogSyncMod.PathEmpty".Translate(), MessageTypeDefOf.RejectInput, false);
                    return;
                }

                string dir = System.IO.Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir))
                {
                    Messages.Message("LogSyncMod.InvalidAbsolutePath".Translate(), MessageTypeDefOf.RejectInput, false);
                    return;
                }

                if (!System.IO.Directory.Exists(dir))
                {
                    Messages.Message("LogSyncMod.ParentDirectoryMissing".Translate(), MessageTypeDefOf.CautionInput, false);
                }

                // 尝试进行模拟写入测试
                string testFile = System.IO.Path.Combine(dir, "access_test.tmp");
                System.IO.File.WriteAllText(testFile, "test");
                System.IO.File.Delete(testFile);

                Messages.Message("LogSyncMod.PathValidationSuccess".Translate(), MessageTypeDefOf.PositiveEvent, false);
            }
            catch (Exception ex)
            {
                Messages.Message("LogSyncMod.PathValidationFailed".Translate(ex.Message), MessageTypeDefOf.RejectInput, false);
            }
        }

        private static string GetAutoTestScopeLabel(AutoTestScope scope)
        {
            switch (scope)
            {
                case AutoTestScope.UI:
                    return "LogSyncMod.AutoTestScopeUI".Translate();
                case AutoTestScope.Combat:
                    return "LogSyncMod.AutoTestScopeCombat".Translate();
                case AutoTestScope.View:
                    return "LogSyncMod.AutoTestScopeView".Translate();
                case AutoTestScope.Misc:
                    return "LogSyncMod.AutoTestScopeMisc".Translate();
                case AutoTestScope.Content:
                    return "LogSyncMod.AutoTestScopeContent".Translate();
                case AutoTestScope.Recipes:
                    return "LogSyncMod.AutoTestScopeRecipes".Translate();
                case AutoTestScope.Hediffs:
                    return "LogSyncMod.AutoTestScopeHediffs".Translate();
                case AutoTestScope.Turrets:
                    return "LogSyncMod.AutoTestScopeTurrets".Translate();
                case AutoTestScope.RangedWeapons:
                    return "LogSyncMod.AutoTestScopeRangedWeapons".Translate();
                case AutoTestScope.MeleeWeapons:
                    return "LogSyncMod.AutoTestScopeMeleeWeapons".Translate();
                case AutoTestScope.ThreeViews:
                    return "LogSyncMod.AutoTestScopeThreeViews".Translate();
                case AutoTestScope.CombatPawns:
                    return "LogSyncMod.AutoTestScopeCombatPawns".Translate();
                case AutoTestScope.SkillPocketMaps:
                    return "LogSyncMod.AutoTestScopeSkillPocketMaps".Translate();
                case AutoTestScope.Standard:
                default:
                    return "LogSyncMod.AutoTestScopeStandard".Translate();
            }
        }

        private static void DrawAutoTestScopeCheckbox(Listing_Standard listing, AutoTestScope scope, string label)
        {
            bool selected = settings.HasScope(scope);
            listing.CheckboxLabeled(label, ref selected);
            if (selected)
            {
                if (!settings.autoTestScopes.Contains(scope)) settings.autoTestScopes.Add(scope);
                settings.autoTestScope = scope;
            }
            else
            {
                settings.autoTestScopes.Remove(scope);
            }
        }

        private static string GetAutoTestModeLabel(AutoTestMode mode)
        {
            switch (mode)
            {
                case AutoTestMode.XmlDriven:
                    return "LogSyncMod.AutoTestModeXmlDriven".Translate();
                default:
                    return "LogSyncMod.AutoTestModeAutomatic".Translate();
            }
        }
    }
}
