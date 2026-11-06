#nullable disable
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace LogSyncMod.AutoTest.Scenarios
{
    // Game-independent, frozen inputs. These definitions never enter DefDatabase.
    public sealed class ScenarioInput
    {
        public string Path { get; set; }
        public string Sha256 { get; set; }
        public string Xml { get; set; }
    }

    public sealed class ScenarioPlan
    {
        public int SchemaVersion { get; set; } = 1;
        public List<ScenarioInput> Inputs { get; set; } = new List<ScenarioInput>();
        public List<ScenarioCase> Cases { get; set; } = new List<ScenarioCase>();
    }

    public sealed class ScenarioCase
    {
        public string Name { get; set; }
        public string TestDefName { get; set; }
        public string CaseId { get; set; }
        public string Source { get; set; }
        public string InputHash { get; set; }
        public string PackageId { get; set; }
        public string Xml { get; set; }
        public Dictionary<string, string> SourceLocations { get; set; } = new Dictionary<string, string>();
    }

    public sealed class ScenarioConfigurationException : Exception
    {
        public ScenarioConfigurationException(string message) : base(message) { }
    }

    public sealed class ScenarioProbeException : Exception
    {
        public ScenarioProbeException(string message, Exception inner) : base(message, inner) { }
    }

    public sealed class ScenarioSample
    {
        public int TargetCount;
        public HashSet<string> Matches = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> Existing = new HashSet<string>(StringComparer.Ordinal);
        public Dictionary<string, string> Owners = new Dictionary<string, string>(StringComparer.Ordinal);
        public string Details;
    }

    public interface IScenarioBackend
    {
        void Prepare(ScenarioCase definition);
        ScenarioSample Sample(XElement condition);
        // true means completed; a native cast/attack can remain in progress.
        bool Execute(XElement step, bool firstPoll);
        void Cleanup();
    }

    public sealed class ScenarioTrace
    {
        public int Tick { get; set; }
        public string Stage { get; set; }
        public string Details { get; set; }
    }

    public sealed class ScenarioOutcome
    {
        public string TestDefName { get; set; }
        public string CaseId { get; set; }
        public string Status { get; set; }
        public string ErrorCode { get; set; }
        public string Error { get; set; }
        public string Source { get; set; }
        public string InputHash { get; set; }
        public List<string> TargetDefNames { get; set; } = new List<string>();
        public List<ScenarioTrace> Trace { get; set; } = new List<ScenarioTrace>();
    }

    public static class ScenarioTypes
    {
        // Shared contract inventory; dedicated default-policy adapters land in batches 2–4.
        public static readonly string[] NonUi = {
            "TurretPocketMap", "RangedWeaponPocketMap", "MeleeWeaponPocketMap", "ApparelCombat",
            "AnimalCombat", "MechanoidCombat", "AlienCombat", "SkillPocketMap", "HumanoidThreeView",
            "AnimalThreeView", "MechanoidThreeView", "AlienThreeView", "ApparelThreeView", "WeaponThreeView",
            "PlantGrowthView", "BuildingView", "TerrainPatchView", "PawnLifeStageThreeView", "HairThreeView",
            "HeadTypeThreeView", "BodyTypeThreeView", "GeneThreeView", "XenotypeThreeView", "RecipeExecution",
            "HediffEffect", "CorpseLifecycle", "ThingBehavior", "LoadoutView", "GroupCombat"
        };
    }
}
