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
        public List<ScenarioInput> Dependencies { get; set; } = new List<ScenarioInput>();
    }

    public sealed class ScenarioPlan
    {
        public int SchemaVersion { get; set; } = 1;
        public string Purpose { get; set; } = "regression";
        public bool ScenarioOnly { get; set; }
        public ArenaPlan Arena { get; set; }
        public List<string> SelectedTestDefNames { get; set; } = new List<string>();
        public List<string> SelectedCaseIds { get; set; } = new List<string>();
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
        public string CaseType { get; set; } = "ExternalScenario";
        public string CoverageMode { get; set; } = "supplement";
        public List<ScenarioCoverageTarget> Coverage { get; set; } = new List<ScenarioCoverageTarget>();
        public List<ScenarioCoverageTarget> SelectionTargets { get; set; } = new List<ScenarioCoverageTarget>();
    }

    public sealed class ScenarioCoverageTarget
    {
        public string PackageId { get; set; }
        public string DefType { get; set; }
        public string DefName { get; set; }
        public string CaseType { get; set; }
        // Empty means the unvaried case, never a wildcard.
        public string Variant { get; set; } = "";
        public List<string> EvidenceSteps { get; set; } = new List<string>();
        public string Key => string.Join("|", PackageId, DefType, DefName, CaseType, Variant).ToLowerInvariant();
    }

    public sealed class ScenarioOrdinaryCandidate
    {
        public string CaseId { get; set; }
        public ScenarioCoverageTarget Target { get; set; }
    }

    public sealed class ScenarioSelection
    {
        public List<ScenarioCase> Cases { get; set; } = new List<ScenarioCase>();
        public List<string> OrdinaryCaseIds { get; set; } = new List<string>();
        public List<ScenarioSelectionDecision> Decisions { get; set; } = new List<ScenarioSelectionDecision>();
        public List<ScenarioReplacement> Replacements { get; set; } = new List<ScenarioReplacement>();
        public bool ReplacementLimitExceeded { get; set; }
    }

    public sealed class ScenarioSelectionDecision
    {
        public string CaseId { get; set; }
        public string Reason { get; set; }
        public bool Selected { get; set; }
    }

    public sealed class ScenarioReplacement
    {
        public string OrdinaryCaseId { get; set; }
        public string ScenarioCaseId { get; set; }
        public string TestDefName { get; set; }
        public ScenarioCoverageTarget Target { get; set; }
    }

    public sealed class ScenarioConfigurationException : Exception
    {
        public string Code { get; }
        public ScenarioConfigurationException(string message, string code = null) : base(message) { Code = code; }
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

    public interface IScenarioCompletionBackend
    {
        void ValidateCompletion();
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
        public string CaseType { get; set; }
        public string CoverageMode { get; set; }
        public List<ScenarioCoverageTarget> Coverage { get; set; } = new List<ScenarioCoverageTarget>();
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
