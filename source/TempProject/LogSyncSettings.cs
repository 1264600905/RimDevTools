using System.Collections.Generic;
using Verse;

namespace LogSyncMod
{
    public class LogSyncSettings : ModSettings
    {
        public string logPath = "D:/RimWorldLogs.log";
        public string targetModIdsInput = ""; // 用于在UI中编辑
        public List<string> targetModIds = new List<string>();
        
        public string keywordsInput = ""; // 用于在UI中编辑
        public List<string> keywords = new List<string>();

        public bool syncMessage = false;
        public bool syncWarning = true;
        public bool syncError = true;
        public bool enableVerboseDebug = false;
        public bool enableSmokeTest = false;
        public bool autoTestEnabled = false;
        public string autoTestTargetPackageId = "";
        public string autoTestOutputPath = "D:/RimWorldAutoTest";
        public bool autoTestBuildings = true;
        public bool autoTestItems = true;
        public bool autoTestTerrains = true;
        public bool autoTestCaptureScreenshots = false;
        public int autoTestBatchSize = 40;
        public bool autoTestRunRequested;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref logPath, "logPath", "D:/RimWorldLogs.log");
            Scribe_Collections.Look(ref targetModIds, "targetModIds", LookMode.Value);
            Scribe_Collections.Look(ref keywords, "keywords", LookMode.Value);
            Scribe_Values.Look(ref syncMessage, "syncMessage", false);
            Scribe_Values.Look(ref syncWarning, "syncWarning", true);
            Scribe_Values.Look(ref syncError, "syncError", true);
            Scribe_Values.Look(ref enableVerboseDebug, "enableVerboseDebug", false);
            Scribe_Values.Look(ref enableSmokeTest, "enableSmokeTest", false);
            Scribe_Values.Look(ref autoTestEnabled, "autoTestEnabled", false);
            Scribe_Values.Look(ref autoTestTargetPackageId, "autoTestTargetPackageId", "");
            Scribe_Values.Look(ref autoTestOutputPath, "autoTestOutputPath", "D:/RimWorldAutoTest");
            Scribe_Values.Look(ref autoTestBuildings, "autoTestBuildings", true);
            Scribe_Values.Look(ref autoTestItems, "autoTestItems", true);
            Scribe_Values.Look(ref autoTestTerrains, "autoTestTerrains", true);
            Scribe_Values.Look(ref autoTestCaptureScreenshots, "autoTestCaptureScreenshots", false);
            Scribe_Values.Look(ref autoTestBatchSize, "autoTestBatchSize", 40);

            if (targetModIds == null) targetModIds = new List<string>();
            if (keywords == null) keywords = new List<string>();
            if (autoTestTargetPackageId == null) autoTestTargetPackageId = "";
            if (autoTestOutputPath == null) autoTestOutputPath = "D:/RimWorldAutoTest";
            
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                targetModIdsInput = string.Join(",", targetModIds);
                keywordsInput = string.Join(",", keywords);
            }
        }

        public void ApplyInputs()
        {
            targetModIds.Clear();
            foreach (var s in targetModIdsInput.Split(','))
            {
                string trimmed = s.Trim().ToLower();
                if (!string.IsNullOrEmpty(trimmed)) targetModIds.Add(trimmed);
            }

            keywords.Clear();
            foreach (var s in keywordsInput.Split(','))
            {
                string trimmed = s.Trim();
                if (!string.IsNullOrEmpty(trimmed)) keywords.Add(trimmed);
            }

            autoTestTargetPackageId = (autoTestTargetPackageId ?? "").Trim().ToLowerInvariant();
            autoTestOutputPath = (autoTestOutputPath ?? "").Trim();
            if (autoTestBatchSize < 1) autoTestBatchSize = 1;
        }

        public void RequestAutoTestRun()
        {
            autoTestRunRequested = true;
        }

        public bool ConsumeAutoTestRunRequest()
        {
            if (!autoTestRunRequested) return false;
            autoTestRunRequested = false;
            return true;
        }
    }
}
