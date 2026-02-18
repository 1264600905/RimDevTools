using UnityEngine;
using Verse;
using RimWorld;
using System;
using System.Collections.Generic;
using HarmonyLib;

namespace LogSyncMod
{
    public class LogSyncMod : Mod
    {
        public static LogSyncSettings settings;

        public LogSyncMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<LogSyncSettings>();
            var harmony = new Harmony("trigger.logsyncmod");
            harmony.PatchAll();
            LogPatches.InitializeFile();
        }

        public override string SettingsCategory()
        {
            return "Log Sync Mod";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);

            listingStandard.Label("日志输出路径 (支持 .log 或 .txt 绝对路径):");
            settings.logPath = listingStandard.TextEntry(settings.logPath);
            
            if (listingStandard.ButtonText("验证路径合法性"))
            {
                ValidateLogPath();
            }
            listingStandard.Gap();

            listingStandard.Label("监控的 Mod PackageID (逗号分隔):");
            settings.targetModIdsInput = listingStandard.TextEntry(settings.targetModIdsInput);
            
            if (listingStandard.ButtonText("从列表中选择 Mod..."))
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
            
            if (listingStandard.ButtonText("清空已选 Mod 列表"))
            {
                settings.targetModIds.Clear();
                settings.targetModIdsInput = "";
                settings.Write();
            }
            listingStandard.Gap();

            listingStandard.Label("强制记录的关键词 (逗号分隔):");
            settings.keywordsInput = listingStandard.TextEntry(settings.keywordsInput);
            listingStandard.Gap();

            listingStandard.CheckboxLabeled("同步普通日志 (Message)", ref settings.syncMessage);
            listingStandard.CheckboxLabeled("同步警告日志 (Warning)", ref settings.syncWarning);
            listingStandard.CheckboxLabeled("同步错误日志 (Error)", ref settings.syncError);

            listingStandard.Gap();
            if (listingStandard.ButtonText("应用并保存设置"))
            {
                settings.ApplyInputs();
                settings.Write();
                Messages.Message("设置已应用", MessageTypeDefOf.PositiveEvent, false);
            }

            if (listingStandard.ButtonText("打开日志所在文件夹"))
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
                        Messages.Message("目录不存在", MessageTypeDefOf.RejectInput, false);
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
                    Messages.Message("路径不能为空", MessageTypeDefOf.RejectInput, false);
                    return;
                }

                string dir = System.IO.Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir))
                {
                    Messages.Message("无效的绝对路径", MessageTypeDefOf.RejectInput, false);
                    return;
                }

                if (!System.IO.Directory.Exists(dir))
                {
                    Messages.Message("父级目录不存在，将尝试在应用时自动创建", MessageTypeDefOf.CautionInput, false);
                }

                // 尝试进行模拟写入测试
                string testFile = System.IO.Path.Combine(dir, "access_test.tmp");
                System.IO.File.WriteAllText(testFile, "test");
                System.IO.File.Delete(testFile);

                Messages.Message("路径验证成功：该位置可写", MessageTypeDefOf.PositiveEvent, false);
            }
            catch (Exception ex)
            {
                Messages.Message("验证失败: " + ex.Message, MessageTypeDefOf.RejectInput, false);
            }
        }
    }
}
