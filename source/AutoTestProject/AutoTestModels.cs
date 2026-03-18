using RimWorld;
using Verse;
using System.Collections.Generic;
using LogSyncMod;

namespace LogSyncMod.AutoTest
{
    internal enum AutoTestKind
    {
        Building,
        Item,
        Terrain,
        Recipe,
        Hediff,
        TurretPocketMap,
        RangedWeaponPocketMap,
        MeleeWeaponPocketMap,
        HumanoidThreeView,
        AnimalThreeView,
        MechanoidThreeView,
        AlienThreeView
    }

    internal enum AutoTestCaseType
    {
        BuildingInfoCard,
        ItemInfoCard,
        TerrainInfoCard,
        RecipeInfoCard,
        HediffInfoCard,
        TurretPocketMap,
        RangedWeaponPocketMap,
        MeleeWeaponPocketMap,
        HumanoidThreeView,
        AnimalThreeView,
        MechanoidThreeView,
        AlienThreeView
    }

    internal sealed class AutoTestWorkItem
    {
        public AutoTestKind Kind;
        public AutoTestCaseType CaseType;
        public string CaseId;
        public ThingDef ThingDef;
        public TerrainDef TerrainDef;
        public RecipeDef RecipeDef;
        public ThingDef WorktableDef;
        public HediffDef HediffDef;
        public LogSyncAutoTestSpecDef SpecDef;
        public AutoTestManifestEntry ManifestEntry;
    }

    internal sealed class AutoTestManifestEntry
    {
        public string CaseId;
        public string CaseType;
        public string Category;
        public string Scope;
        public string DefType;
        public string DefName;
        public string Label;
        public int DiscoverOrder;
        public bool Planned;
        public int PlannedOrder;
    }

    internal sealed class AutoTestRunSummary
    {
        public string RunMode;
        public bool Complete;
        public int DiscoveredCount;
        public int PlannedCount;
        public int ExecutedCount;
        public int PassedCount;
        public int FailedCount;
        public int SkippedCount;
        public int MissingCount;
        public List<string> MissingCaseIds = new List<string>();
    }

    internal sealed class AutoTestResult
    {
        public string PackageId;
        public string CaseId;
        public string CaseType;
        public string Scope;
        public string DefType;
        public string DefName;
        public string Label;
        public string Category;
        public string Status;
        public string Stage;
        public string ErrorMessage;
        public string ScreenshotPath;
        public List<string> ScreenshotPaths = new List<string>();
        public string Notes;
    }
}
