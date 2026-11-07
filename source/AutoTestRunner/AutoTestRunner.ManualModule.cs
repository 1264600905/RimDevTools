using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed class ManualRunnerOptions
    {
        internal AutoTestRunArtifacts Artifacts;
        internal ManualTestChoice Choice;
        internal TemporaryPawnSnapshot PawnSnapshot;
        internal bool InstancePawn;
        internal ManualThingSnapshot ThingSnapshot;
        internal bool BuiltinUsesPawnSnapshot;
        internal Action<List<AutoTestResult>, AutoTestEvidenceManifest> Completed;
        internal readonly List<Thing> GeneratedObjects = new List<Thing>();
        internal readonly HashSet<Pawn> GeneratedPawns = new HashSet<Pawn>();
    }

    internal sealed partial class AutoTestRunner
    {
        internal static LogSyncSettings ManualSettings(string packageId, IEnumerable<string> targets, string id, string output)
        {
            return new LogSyncSettings {
                autoTestEnabled = false, autoTestScenarioPlan = "", autoTestProjectRoot = "", autoTestRunId = id,
                autoTestTargetPackageId = packageId, autoTestOutputPath = output,
                autoTestScopes = new List<AutoTestScope> { AutoTestScope.UI, AutoTestScope.Combat, AutoTestScope.View, AutoTestScope.CombatPawns, AutoTestScope.SkillPocketMaps },
                autoTestTargetDefNames = targets.ToList(), autoTestCaptureScreenshots = true, autoTestBatchSize = int.MaxValue,
                autoTestPocketMapMaxConcurrent = 1, autoTestUseFastSpeed = false
            };
        }

        internal static List<AutoTestManifestEntry> DescribeManualCases(ModContentPack mod, Map map)
        {
            var settings = ManualSettings(mod.PackageIdPlayerFacing, Array.Empty<string>(), "catalog", "");
            var probe = new AutoTestRunner(settings, map) { targetMod = mod, targetPackageId = mod.PackageIdPlayerFacing, discoveryOnly = true };
            probe.DiscoverOrdinaryCases();
            return probe.discoveredManifest;
        }

        private void RetainManualCases()
        {
            var choice = manualOptions.Choice;
            var selected = new HashSet<string>(discoveredManifest.Where(e => e.CaseType == choice.CaseType && e.DefName == choice.NativeDefName).Select(e => e.CaseId), StringComparer.Ordinal);
            RetainScenarioOrdinaryQueues(selected);
            discoveredManifest.RemoveAll(e => !selected.Contains(e.CaseId));
            if (selected.Count == 0) evidenceBuilder.AddError(new AutoTestOperationError { Code = "manual.capability_changed", Message = "Frozen built-in capability is no longer discoverable: " + choice.CaseType + "/" + choice.NativeDefName });
        }

        internal IDisposable ManualPawnScope() => ManualPawnGeneration.Use(manualOptions);

        private void InitializeManualLoadout()
        {
            var kind = DefDatabase<PawnKindDef>.GetNamed(manualOptions.Choice.NativeDefName);
            var template = manualOptions.PawnSnapshot == null ? null : XElement.Parse(manualOptions.PawnSnapshot.TemplateXml);
            manualOptions.BuiltinUsesPawnSnapshot = template?.Element("basePawnKind")?.Value.Trim() == kind.defName;
            if (!manualOptions.BuiltinUsesPawnSnapshot) template = new XElement("PawnTemplate", new XElement("basePawnKind", kind.defName));
            var actor = new XElement(template); actor.Name = "Pawn"; actor.SetAttributeValue("id", "presenter"); actor.SetAttributeValue("x", 25); actor.SetAttributeValue("z", 25);
            var testName = "Manual_Loadout_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(targetPackageId.ToLowerInvariant() + "|PawnKindDef|" + kind.defName))).Substring(0, 20);
            var xml = new XElement("Case", new XAttribute("id", "loadout"), new XAttribute("caseType", "LoadoutView"), new XAttribute("timeoutRealSeconds", 90), new XElement("testDefName", testName), new XElement("Scene", new XAttribute("type", "studio")), new XElement("Actors", actor),
                new XElement("Steps", new XElement("Require", new XAttribute("id", "spawned"), new XElement("ActorState", new XAttribute("actor", "presenter"), new XAttribute("state", "spawned"))), new XElement("View", new XAttribute("id", "loadout"), new XAttribute("actor", "presenter"), new XAttribute("pose", "auto"), new XAttribute("loadout", "preserve"))),
                new XElement("Success", new XElement("StepSucceeded", new XAttribute("ref", "loadout"))));
            var definition = new ScenarioCase { CaseId = BuildCaseId(AutoTestCaseType.LoadoutView, kind.defName), CaseType = "LoadoutView", PackageId = targetPackageId, TestDefName = testName, Name = kind.LabelCap + " 整套配装", Source = "builtin:LoadoutView", InputHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(xml.ToString()))).ToLowerInvariant(), Xml = xml.ToString() };
            scenarioQueue = new Queue<ScenarioCase>(new[] { definition });
            var entry = RegisterDiscoveredCase(AutoTestCaseType.LoadoutView, "PawnKindDef", kind.defName, definition.Name); AddPlannedManifestEntry(entry);
        }
    }
}
