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
        Plant,
        Terrain,
        Recipe,
        Hediff,
        AnimalInfoCard,
        MechanoidInfoCard,
        PawnInfoCard,
        GeneInfoCard,
        XenotypeInfoCard,
        TurretPocketMap,
        RangedWeaponPocketMap,
        MeleeWeaponPocketMap,
        SkillPocketMap,
        HumanoidThreeView,
        AnimalThreeView,
        MechanoidThreeView,
        AlienThreeView,
        ApparelThreeView,
        WeaponThreeView,
        PlantGrowthView,
        BuildingView,
        TerrainPatchView,
        PawnLifeStageThreeView,
        HairThreeView,
        HeadTypeThreeView,
        BodyTypeThreeView,
        GeneThreeView,
        XenotypeThreeView,
        ApparelCombat,
        AnimalCombat,
        MechanoidCombat,
        AlienCombat
    }

    internal enum AutoTestCaseType
    {
        BuildingInfoCard,
        ItemInfoCard,
        PlantInfoCard,
        TerrainInfoCard,
        RecipeInfoCard,
        RecipeBillUi,
        HediffInfoCard,
        AnimalInfoCard,
        MechanoidInfoCard,
        PawnInfoCard,
        GeneInfoCard,
        XenotypeInfoCard,
        TurretPocketMap,
        RangedWeaponPocketMap,
        MeleeWeaponPocketMap,
        SkillPocketMap,
        HumanoidThreeView,
        AnimalThreeView,
        MechanoidThreeView,
        AlienThreeView,
        ApparelThreeView,
        WeaponThreeView,
        PlantGrowthView,
        BuildingView,
        TerrainPatchView,
        PawnLifeStageThreeView,
        HairThreeView,
        HeadTypeThreeView,
        BodyTypeThreeView,
        GeneThreeView,
        XenotypeThreeView,
        ApparelCombat,
        AnimalCombat,
        MechanoidCombat,
        AlienCombat
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
        public PawnKindDef PawnKindDef;
        public GeneDef GeneDef;
        public XenotypeDef XenotypeDef;
        public LogSyncAutoTestSpecDef SpecDef;
        public AutoTestPawnFactionMode PawnFactionMode = AutoTestPawnFactionMode.Native;
        public bool CompatibilityObservation;
        public AutoTestManifestEntry ManifestEntry;
    }

    internal sealed class AutoTestDeferredResult
    {
        public AutoTestManifestEntry ManifestEntry;
        public string DefType;
        public string DefName;
        public string Label;
        public string Status;
        public string Stage;
        public string ErrorMessage;
        public string Notes;
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
        public int PassedAfterRetryCount;
        public int UnsupportedCount;
        public int CompatibilityWarningCount;
        public int MissingCount;
        public int BatchSize;
        public bool BatchLimitApplied;
        public List<string> MissingCaseIds = new List<string>();
        public List<string> MissingDefNames = new List<string>();
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
        public string ResultReasonCode;
        public string ScreenshotPath;
        public List<string> ScreenshotPaths = new List<string>();
        public string Notes;
        public string OverlayLine1;
        public string OverlayLine2;
        public string OverlayLine3;
        public string OverlayLine4;
        public int AssertionCount;
        public List<string> FailedAssertionIds = new List<string>();
        public List<string> OperationIds = new List<string>();
        public string EvidenceCaseKey;
        public int UnexpectedSevereLogCount;
        public string LastSnapshot;
    }
}
