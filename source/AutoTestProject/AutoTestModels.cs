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
        TurretPocketMap
    }

    internal sealed class AutoTestWorkItem
    {
        public AutoTestKind Kind;
        public ThingDef ThingDef;
        public TerrainDef TerrainDef;
        public RecipeDef RecipeDef;
        public ThingDef WorktableDef;
        public HediffDef HediffDef;
        public LogSyncAutoTestSpecDef SpecDef;
    }

    internal sealed class AutoTestResult
    {
        public string PackageId;
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
