using System.Collections.Generic;
using Verse;

namespace LogSyncMod
{
    public enum AutoTestScope
    {
        Standard = 0,
        Content = 1,
        Recipes = 2,
        Hediffs = 3,
        Turrets = 4,
        RangedWeapons = 5,
        MeleeWeapons = 6,
        ThreeViews = 7,
        CombatPawns = 8,
        SkillPocketMaps = 9
    }

    public enum AutoTestMode
    {
        Automatic = 0,
        XmlDriven = 1
    }

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
        public int logMaxLines = 500;
        public bool enableVerboseDebug = false;
        public bool enableSmokeTest = false;
        public bool testMuteAudio = false;
        public bool autoTestEnabled = false;
        public string autoTestTargetPackageId = "";
        public string autoTestRunId = "";
        public string autoTestOutputPath = "D:/RimWorldAutoTest";
        public AutoTestMode autoTestMode = AutoTestMode.Automatic;
        public AutoTestScope autoTestScope = AutoTestScope.Standard;
        public bool autoTestBuildings = true;
        public bool autoTestItems = true;
        public bool autoTestTerrains = true;
        public bool autoTestCaptureScreenshots = false;
        public int autoTestBatchSize = 40;
        public bool autoTestPreventPause = true;
        public bool autoTestUseFastSpeed = true;
        public int autoTestPocketMapMaxConcurrent = 5;
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
            Scribe_Values.Look(ref logMaxLines, "logMaxLines", 500);
            Scribe_Values.Look(ref enableVerboseDebug, "enableVerboseDebug", false);
            Scribe_Values.Look(ref enableSmokeTest, "enableSmokeTest", false);
            Scribe_Values.Look(ref testMuteAudio, "testMuteAudio", false);
            Scribe_Values.Look(ref autoTestEnabled, "autoTestEnabled", false);
            Scribe_Values.Look(ref autoTestTargetPackageId, "autoTestTargetPackageId", "");
            Scribe_Values.Look(ref autoTestRunId, "autoTestRunId", "");
            Scribe_Values.Look(ref autoTestOutputPath, "autoTestOutputPath", "D:/RimWorldAutoTest");
            Scribe_Values.Look(ref autoTestMode, "autoTestMode", AutoTestMode.Automatic);
            Scribe_Values.Look(ref autoTestScope, "autoTestScope", AutoTestScope.Standard);
            Scribe_Values.Look(ref autoTestBuildings, "autoTestBuildings", true);
            Scribe_Values.Look(ref autoTestItems, "autoTestItems", true);
            Scribe_Values.Look(ref autoTestTerrains, "autoTestTerrains", true);
            Scribe_Values.Look(ref autoTestCaptureScreenshots, "autoTestCaptureScreenshots", false);
            Scribe_Values.Look(ref autoTestBatchSize, "autoTestBatchSize", 40);
            Scribe_Values.Look(ref autoTestPreventPause, "autoTestPreventPause", true);
            Scribe_Values.Look(ref autoTestUseFastSpeed, "autoTestUseFastSpeed", true);
            Scribe_Values.Look(ref autoTestPocketMapMaxConcurrent, "autoTestPocketMapMaxConcurrent", 5);

            if (targetModIds == null) targetModIds = new List<string>();
            if (keywords == null) keywords = new List<string>();
            if (autoTestTargetPackageId == null) autoTestTargetPackageId = "";
            if (autoTestRunId == null) autoTestRunId = "";
            if (autoTestOutputPath == null) autoTestOutputPath = "D:/RimWorldAutoTest";
            if (logMaxLines < 1) logMaxLines = 1;
            if (!System.Enum.IsDefined(typeof(AutoTestMode), autoTestMode)) autoTestMode = AutoTestMode.Automatic;
            if (!System.Enum.IsDefined(typeof(AutoTestScope), autoTestScope)) autoTestScope = AutoTestScope.Standard;
            
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
            autoTestRunId = (autoTestRunId ?? "").Trim();
            autoTestOutputPath = (autoTestOutputPath ?? "").Trim();
            if (logMaxLines < 1) logMaxLines = 1;
            if (autoTestBatchSize < 1) autoTestBatchSize = 1;
            if (autoTestPocketMapMaxConcurrent < 1) autoTestPocketMapMaxConcurrent = 1;
        }

        public int GetLogMaxLines()
        {
            return logMaxLines < 1 ? 1 : logMaxLines;
        }

        public bool ShouldRunStandardSuite()
        {
            return autoTestScope == AutoTestScope.Standard;
        }

        public bool ShouldTestContent()
        {
            return ShouldRunStandardSuite() || autoTestScope == AutoTestScope.Content;
        }

        public bool UseXmlDrivenAutoTest()
        {
            return autoTestMode == AutoTestMode.XmlDriven;
        }

        public bool ShouldTestRecipes()
        {
            return ShouldRunStandardSuite() || autoTestScope == AutoTestScope.Recipes;
        }

        public bool ShouldTestHediffs()
        {
            return ShouldRunStandardSuite() || autoTestScope == AutoTestScope.Hediffs;
        }

        public bool ShouldTestTurrets()
        {
            return ShouldRunStandardSuite() || autoTestScope == AutoTestScope.Turrets;
        }

        public bool ShouldTestRangedWeapons()
        {
            return ShouldRunStandardSuite() || autoTestScope == AutoTestScope.RangedWeapons;
        }

        public bool ShouldTestMeleeWeapons()
        {
            return ShouldRunStandardSuite() || autoTestScope == AutoTestScope.MeleeWeapons;
        }

        public bool ShouldTestThreeViews()
        {
            return ShouldRunStandardSuite() || autoTestScope == AutoTestScope.ThreeViews;
        }

        public bool ShouldTestCombatPawns()
        {
            return autoTestScope == AutoTestScope.CombatPawns;
        }

        public bool ShouldTestSkillPocketMaps()
        {
            return autoTestScope == AutoTestScope.SkillPocketMaps;
        }

        public int GetPocketMapMaxConcurrent()
        {
            return autoTestPocketMapMaxConcurrent < 1 ? 1 : autoTestPocketMapMaxConcurrent;
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
