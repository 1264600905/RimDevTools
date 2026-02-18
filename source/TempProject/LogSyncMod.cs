using UnityEngine;
using Verse;
using System;
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
            listingStandard.Gap();

            listingStandard.Label("监控的 Mod PackageID (逗号分隔):");
            settings.targetModIdsInput = listingStandard.TextEntry(settings.targetModIdsInput);
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
                LogUtils.Message("设置已应用");
            }

            listingStandard.End();
        }
    }
}
