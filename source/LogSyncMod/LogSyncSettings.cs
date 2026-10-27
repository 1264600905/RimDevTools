using System.Collections.Generic;
using System.Linq;
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
        SkillPocketMaps = 9,
        UI = 10,
        Combat = 11,
        View = 12,
        Misc = 13
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
        public List<AutoTestScope> autoTestScopes = new List<AutoTestScope>();
        public List<string> autoTestTargetDefNames = new List<string>();
        public string autoTestTargetDefNamesInput = "";
        public bool autoTestBuildings = true;
        public bool autoTestItems = true;
        public bool autoTestTerrains = true;
        public bool autoTestCaptureScreenshots = false;
        public int autoTestBatchSize = 40;
        public bool autoTestPreventPause = true;
        public bool autoTestUseFastSpeed = true;
        public int autoTestPocketMapMaxConcurrent = 5;
        public bool autoTestRunRequested;
        public List<DebugSupplyOverride> debugSupplyOverrides = new List<DebugSupplyOverride>();
        public List<DebugSupplyProfile> debugSupplyProfiles = new List<DebugSupplyProfile>();
        public string debugSupplyActiveProfileId = "";

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref debugSupplyOverrides, "debugSupplyOverrides", LookMode.Deep);
            if (debugSupplyOverrides == null) debugSupplyOverrides = new List<DebugSupplyOverride>();
            Scribe_Collections.Look(ref debugSupplyProfiles, "debugSupplyProfiles", LookMode.Deep);
            if (debugSupplyProfiles == null) debugSupplyProfiles = new List<DebugSupplyProfile>();
            Scribe_Values.Look(ref debugSupplyActiveProfileId, "debugSupplyActiveProfileId", "");
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
            Scribe_Collections.Look(ref autoTestScopes, "autoTestScopes", LookMode.Value);
            Scribe_Collections.Look(ref autoTestTargetDefNames, "autoTestTargetDefNames", LookMode.Value);
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
            if (autoTestScopes == null) autoTestScopes = new List<AutoTestScope>();
            if (autoTestTargetDefNames == null) autoTestTargetDefNames = new List<string>();
            if (logMaxLines < 1) logMaxLines = 1;
            if (!System.Enum.IsDefined(typeof(AutoTestMode), autoTestMode)) autoTestMode = AutoTestMode.Automatic;
            if (!System.Enum.IsDefined(typeof(AutoTestScope), autoTestScope)) autoTestScope = AutoTestScope.Standard;
            autoTestScopes = NormalizeScopes(autoTestScopes);
            autoTestTargetDefNames = NormalizeDefNames(autoTestTargetDefNames);
            
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                targetModIdsInput = string.Join(",", targetModIds);
                keywordsInput = string.Join(",", keywords);
                if (autoTestScopes.Count == 0) autoTestScopes.Add(autoTestScope);
                autoTestTargetDefNamesInput = string.Join(",", autoTestTargetDefNames);
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
            autoTestScopes = NormalizeScopes(autoTestScopes);
            autoTestTargetDefNames.Clear();
            foreach (var value in (autoTestTargetDefNamesInput ?? "").Split(','))
            {
                var trimmed = value.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !autoTestTargetDefNames.Contains(trimmed, System.StringComparer.OrdinalIgnoreCase))
                    autoTestTargetDefNames.Add(trimmed);
            }
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
            return HasScope(AutoTestScope.Standard);
        }

        public bool ShouldTestContent()
        {
            return HasAnyScope(AutoTestScope.UI, AutoTestScope.Content) || ShouldRunStandardSuite();
        }

        public bool UseXmlDrivenAutoTest()
        {
            return autoTestMode == AutoTestMode.XmlDriven;
        }

        public bool ShouldTestRecipes()
        {
            return HasAnyScope(AutoTestScope.UI, AutoTestScope.Recipes) || ShouldRunStandardSuite();
        }

        public bool ShouldTestHediffs()
        {
            return HasAnyScope(AutoTestScope.UI, AutoTestScope.Hediffs) || ShouldRunStandardSuite();
        }

        public bool ShouldTestTurrets()
        {
            return HasAnyScope(AutoTestScope.Combat, AutoTestScope.Turrets) || ShouldRunStandardSuite();
        }

        public bool ShouldTestRangedWeapons()
        {
            return HasAnyScope(AutoTestScope.Combat, AutoTestScope.RangedWeapons) || ShouldRunStandardSuite();
        }

        public bool ShouldTestMeleeWeapons()
        {
            return HasAnyScope(AutoTestScope.Combat, AutoTestScope.MeleeWeapons) || ShouldRunStandardSuite();
        }

        public bool ShouldTestThreeViews()
        {
            return HasAnyScope(AutoTestScope.View, AutoTestScope.ThreeViews) || ShouldRunStandardSuite();
        }

        public bool ShouldTestCombatPawns()
        {
            return HasAnyScope(AutoTestScope.Combat, AutoTestScope.CombatPawns);
        }

        public bool ShouldTestSkillPocketMaps()
        {
            return HasAnyScope(AutoTestScope.Misc, AutoTestScope.SkillPocketMaps);
        }

        public IReadOnlyList<AutoTestScope> EffectiveAutoTestScopes()
            => autoTestScopes != null && autoTestScopes.Count > 0 ? autoTestScopes : new List<AutoTestScope> { autoTestScope };

        public bool HasScope(AutoTestScope scope) => EffectiveAutoTestScopes().Contains(scope);

        public bool HasAnyScope(params AutoTestScope[] scopes)
            => scopes != null && scopes.Any(HasScope);

        public bool MatchesAutoTestTargetDefName(string defName)
        {
            if (autoTestTargetDefNames == null || autoTestTargetDefNames.Count == 0) return true;
            return autoTestTargetDefNames.Any(value => string.Equals(value, defName, System.StringComparison.OrdinalIgnoreCase));
        }

        private static List<AutoTestScope> NormalizeScopes(List<AutoTestScope> values)
        {
            var result = new List<AutoTestScope>();
            foreach (var value in values ?? new List<AutoTestScope>())
                if (System.Enum.IsDefined(typeof(AutoTestScope), value) && !result.Contains(value)) result.Add(value);
            return result;
        }

        private static List<string> NormalizeDefNames(List<string> values)
        {
            var result = new List<string>();
            foreach (var value in values ?? new List<string>())
            {
                var trimmed = (value ?? "").Trim();
                if (!string.IsNullOrEmpty(trimmed) && !result.Contains(trimmed, System.StringComparer.OrdinalIgnoreCase)) result.Add(trimmed);
            }
            return result;
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
