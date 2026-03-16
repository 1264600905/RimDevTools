using UnityEngine;
using Verse;
using RimWorld;
using System;
using System.Collections.Generic;
using HarmonyLib;

namespace LogSyncMod
{
    [StaticConstructorOnStartup]
    public class LogSyncMod : Mod
    {
        public static LogSyncSettings settings;

        static LogSyncMod()
        {
            // 在所有加载完成（进入主菜单）时执行
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                // 创建一个 10 秒的定时器
                var timer = new System.Timers.Timer(10000);
                timer.Elapsed += (s, e) =>
                {
                    Log.Message("[LogSyncMod] RUNTIME_READY_TO_CLOSE");
                    timer.Stop();
                    timer.Dispose();
                };
                timer.Start();
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
            Log.Message("[LogSyncMod] INITIALIZATION COMPLETED.");
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
            string scopeLabel = GetAutoTestScopeLabel(settings.autoTestScope);
            listingStandard.Label("LogSyncMod.AutoTestScope".Translate(scopeLabel));
            if (listingStandard.ButtonText("LogSyncMod.AutoTestScopeSelect".Translate(scopeLabel)))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>
                {
                    new FloatMenuOption("LogSyncMod.AutoTestScopeAll".Translate(), () => settings.autoTestScope = AutoTestScope.All),
                    new FloatMenuOption("LogSyncMod.AutoTestScopeContent".Translate(), () => settings.autoTestScope = AutoTestScope.Content),
                    new FloatMenuOption("LogSyncMod.AutoTestScopeRecipes".Translate(), () => settings.autoTestScope = AutoTestScope.Recipes)
                };
                Find.WindowStack.Add(new FloatMenu(options));
            }
            listingStandard.CheckboxLabeled("LogSyncMod.AutoTestCaptureScreenshots".Translate(), ref settings.autoTestCaptureScreenshots);
            listingStandard.Label("LogSyncMod.AutoTestBatchSize".Translate(settings.autoTestBatchSize));
            string batchSizeInput = listingStandard.TextEntry(settings.autoTestBatchSize.ToString());
            if (int.TryParse(batchSizeInput, out int parsedBatchSize))
            {
                settings.autoTestBatchSize = parsedBatchSize;
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
                case AutoTestScope.Content:
                    return "LogSyncMod.AutoTestScopeContent".Translate();
                case AutoTestScope.Recipes:
                    return "LogSyncMod.AutoTestScopeRecipes".Translate();
                default:
                    return "LogSyncMod.AutoTestScopeAll".Translate();
            }
        }
    }
}
