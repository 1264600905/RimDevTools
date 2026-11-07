using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed partial class AutoTestRunner
    {
        private Queue<ScenarioCase> scenarioQueue;
        private readonly Queue<ScenarioCase> deferredScenarioQueue = new Queue<ScenarioCase>();
        private ScenarioExecution scenarioExecution;
        private ScenarioGameBackend scenarioBackend;
        private AutoTestResult scenarioResult;
        private XElement scenarioCapture;
        private int scenarioCaptureFrame;
        private bool scenarioCaptured;
        private bool pollingScenario;
        private ScenarioPlan externalPlan;
        private ScenarioSelection externalSelection;
        private bool HasPendingScenarios => scenarioQueue != null && (scenarioQueue.Count > 0 || scenarioExecution != null);

        private bool LoadScenarioPlan()
        {
            if (string.IsNullOrWhiteSpace(settings.autoTestScenarioPlan)) return true;
            try
            {
                var frozen = JsonSerializer.Deserialize<ScenarioPlan>(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(settings.autoTestScenarioPlan)));
                if (frozen == null || frozen.SchemaVersion != 1) throw new ScenarioConfigurationException("Invalid frozen scenario plan");
                externalPlan = ScenarioLoader.Compile(frozen.Inputs);
                ScenarioLoader.SelectRules(externalPlan, frozen.SelectedTestDefNames ?? new List<string>());
                externalPlan.ScenarioOnly = frozen.ScenarioOnly;
                foreach (var definition in externalPlan.Cases) ScenarioGameBackend.ValidateCoverage(definition);
                return true;
            }
            catch (Exception ex)
            {
                evidenceBuilder.AddError(new AutoTestOperationError { Code = "scenario.configuration", Message = ex.ToString() });
                Finish(); return false;
            }
        }

        private void InitializeScenarios()
        {
            scenarioQueue = new Queue<ScenarioCase>();
            evidenceManifest.Suite = externalPlan.ScenarioOnly ? "ExternalScenario" : "MixedScenario";
            try
            {
                BuildPlannedManifest();
                var ordinary = plannedManifest.Select(e => new ScenarioOrdinaryCandidate { CaseId = e.CaseId, Target = OrdinaryTarget(e) }).ToList();
                externalSelection = ScenarioPlanner.Select(externalPlan.Cases, ordinary, Math.Max(1, settings.autoTestBatchSize), externalPlan.ScenarioOnly);
                var range = new HashSet<string>(ordinary.Select(c => c.Target.Key), StringComparer.OrdinalIgnoreCase);
                var selected = new HashSet<string>(externalSelection.OrdinaryCaseIds, StringComparer.OrdinalIgnoreCase);
                RetainScenarioOrdinaryQueues(selected);
                BuildPlannedManifest();
                foreach (var definition in externalPlan.Cases)
                {
                    bool include = externalSelection.Cases.Any(c => c.CaseId == definition.CaseId);
                    if (include)
                    {
                        if (externalPlan.ScenarioOnly || ScenarioPlanner.InRange(definition, range)) scenarioQueue.Enqueue(definition);
                        else deferredScenarioQueue.Enqueue(definition);
                    }
                    var entry = new AutoTestManifestEntry { CaseId = definition.CaseId, CaseType = definition.CaseType, Scope = "ExternalScenario", Category = "Scenario", DefType = "TestRule", DefName = definition.TestDefName, Label = definition.Name, DiscoverOrder = discoveredManifest.Count + 1 };
                    discoveredManifest.Add(entry);
                    if (include) AddPlannedManifestEntry(entry);
                }
                batchLimitApplied = externalSelection.Decisions.Any(d => d.Reason == "batch-limit");
                if (selected.Count == 0) TryActivateDeferredScenarios();
                Log.Message("[LogSyncAutoTest] Frozen XML plan: scenarios=" + (scenarioQueue.Count + deferredScenarioQueue.Count) + "; ordinary=" + selected.Count + "; replaced=" + externalSelection.Replacements.Count + "; replacementLimitExceeded=" + externalSelection.ReplacementLimitExceeded);
            }
            catch (Exception ex)
            {
                evidenceBuilder.AddError(new AutoTestOperationError { Code = "scenario.configuration", Message = ex.ToString() });
                Finish();
            }
        }

        private bool TryActivateDeferredScenarios()
        {
            if (deferredScenarioQueue.Count == 0) return false;
            while (deferredScenarioQueue.Count > 0) scenarioQueue.Enqueue(deferredScenarioQueue.Dequeue());
            return true;
        }

        private ScenarioCoverageTarget OrdinaryTarget(AutoTestManifestEntry e)
        {
            // Existing expanded targets use baseDef@variant. Keep that variant exact.
            var split = (e.DefName ?? "").Split(new[] { '@' }, 2);
            return new ScenarioCoverageTarget { PackageId = targetPackageId, DefType = e.DefType, DefName = split[0], CaseType = e.CaseType, Variant = split.Length == 2 ? split[1] : "" };
        }

        private void RetainScenarioOrdinaryQueues(HashSet<string> selected)
        {
            void Keep<T>(Queue<T> queue, Func<T, string> id)
            { var keep = queue.Where(t => selected.Contains(id(t))).ToList(); queue.Clear(); foreach (var t in keep) queue.Enqueue(t); }
            Keep(pendingItems, t => t.ManifestEntry.CaseId);
            Keep(pendingImmediateResults, t => t.ManifestEntry.CaseId);
            Keep(pendingTurretDefs, t => BuildCaseId(AutoTestCaseType.TurretPocketMap, t.defName));
            Keep(pendingRangedWeaponDefs, t => BuildCaseId(AutoTestCaseType.RangedWeaponPocketMap, t.defName));
            Keep(pendingMeleeWeaponDefs, t => BuildCaseId(AutoTestCaseType.MeleeWeaponPocketMap, t.defName));
            Keep(pendingSkillAbilityDefs, t => BuildCaseId(AutoTestCaseType.SkillPocketMap, t.defName));
            Keep(pendingAbilityGizmoDefs, t => BuildCaseId(AutoTestCaseType.AbilityGizmoUi, t.defName));
            Keep(pendingStaticViewTargets, t => t.ManifestEntry.CaseId);
            Keep(pendingThreeViewTargets, t => t.ManifestEntry.CaseId);
            Keep(pendingCombatPawnTargets, t => t.ManifestEntry.CaseId);
        }

        private void TickScenarios()
        {
            if (pollingScenario || completed) return;
            pollingScenario = true;
            try
            {
                if (scenarioExecution == null)
                {
                    if (pocketMapService.PendingCleanupCount != 0) return;
                    if (scenarioQueue.Count == 0) return;
                    var definition = scenarioQueue.Dequeue();
                    scenarioResult = new AutoTestResult { PackageId = targetPackageId, CaseId = definition.CaseId, CaseType = definition.CaseType, Scope = "ExternalScenario", Category = "Scenario", DefType = "TestRule", DefName = definition.TestDefName, Label = definition.Name, Status = "running", Stage = "prepare" };
                    RegisterSemanticCase(scenarioResult);
                    scenarioBackend = new ScenarioGameBackend(map, pocketMapService, QueueScenarioScreenshot,
                        (stage, details) => scenarioExecution?.Mark(stage, Find.TickManager.TicksGame, details));
                    scenarioExecution = new ScenarioExecution(definition, scenarioBackend, evidenceClock);
                    ScenarioGameBackend.NativeObservation = () => {
                        try { scenarioExecution?.Observe(Find.TickManager.TicksGame); }
                        catch (Exception ex) { scenarioObservationError = ex; }
                    };
                    ScenarioGameBackend.AbilityActivation = ability => scenarioBackend?.ObserveAbilityActivation(ability);
                    Log.Message("[LogSyncAutoTest] Scenario started " + definition.TestDefName + " source=" + definition.Source + " hash=" + definition.InputHash);
                }
                using (AutoTestOperationContext.PushCase(scenarioResult.CaseId))
                {
                    if (scenarioObservationError != null)
                    {
                        // Probe errors are failures, never converted into false conditions or swallowed.
                        scenarioExecution.Mark("probe.error", Find.TickManager.TicksGame, scenarioObservationError.ToString());
                        scenarioExecution.Abort("scenario.probe", scenarioObservationError.ToString(), Find.TickManager.TicksGame);
                        scenarioObservationError = null;
                    }
                    else scenarioExecution.Poll(Find.TickManager.TicksGame, cancelRequested);
                    if (!scenarioExecution.Completed) return;
                    ScenarioGameBackend.NativeObservation = null;
                    ScenarioGameBackend.AbilityActivation = null;
                    var outcome = scenarioExecution.Outcome;
                    scenarioResult.Scenario = outcome;
                    scenarioResult.Status = outcome.Status;
                    scenarioResult.Stage = outcome.Status == "passed" ? "completed" : outcome.ErrorCode;
                    scenarioResult.ErrorMessage = outcome.Error;
                    scenarioResult.ResultReasonCode = outcome.ErrorCode;
                    scenarioResult.Notes = "testDefName=" + outcome.TestDefName + "; inputHash=" + outcome.InputHash + "; source=" + outcome.Source;
                    if (outcome.Status == "failed") Log.Warning("[LogSyncAutoTest] External scenario failed: " + (outcome.Error ?? outcome.ErrorCode ?? "unknown").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault());
                    SemanticAssert(scenarioResult, "scenario.success", outcome.Status == "passed", "all scenario steps and success predicates", outcome.Status, outcome.Error);
                    CompleteSemanticCase(scenarioResult);
                    outcome.Status = scenarioResult.Status;
                    outcome.ErrorCode = scenarioResult.ResultReasonCode;
                    outcome.Error = scenarioResult.ErrorMessage;
                    results.Add(scenarioResult);
                    Log.Message("[LogSyncAutoTest] Scenario finished " + outcome.TestDefName + " status=" + outcome.Status + " code=" + outcome.ErrorCode);
                    scenarioExecution = null; scenarioBackend = null; scenarioCapture = null; scenarioCaptured = false;
                    if (cancelRequested)
                    {
                        foreach (var remaining in scenarioQueue.Concat(deferredScenarioQueue))
                            results.Add(new AutoTestResult { PackageId = targetPackageId, CaseId = remaining.CaseId, CaseType = remaining.CaseType, Scope = "ExternalScenario", Category = "Scenario", DefType = "TestRule", DefName = remaining.TestDefName, Label = remaining.TestDefName, Status = "cancelled", Stage = "cancel", ErrorMessage = "Explicit cancellation before scene preparation" });
                        scenarioQueue.Clear(); cancelRequested = false;
                        deferredScenarioQueue.Clear();
                        CancelActiveSessions();
                    }
                }
            }
            finally { pollingScenario = false; }
        }
        private Exception scenarioObservationError;

        private bool QueueScenarioScreenshot(XElement step)
        {
            if (scenarioCapture == null)
            {
                scenarioCapture = step;
                scenarioCaptureFrame = Time.frameCount;
                scenarioCaptured = false;
                var focus = ScenarioLoader.A(step, "focus");
                Current.Game.CurrentMap = scenarioBackend.Map;
                Find.World.renderer.wantedMode = WorldRenderMode.None;
                var cell = focus == null ? scenarioBackend.Map.Center : scenarioBackend.One(focus).Position;
                Find.CameraDriver.SetRootPosAndSize(new Vector3(cell.x + .5f, 0, cell.z + .5f), 12f);
                return false;
            }
            if (!scenarioCaptured) return false;
            scenarioCapture = null;
            return true;
        }

        private void CaptureScenarioUi()
        {
            if (scenarioCapture == null || scenarioCaptured || Time.frameCount - scenarioCaptureFrame < 3 || Event.current.type != EventType.Repaint) return;
            if (Find.CurrentMap != scenarioBackend.Map) return;
            if (!AutoTestScreenshotService.PrepareCaptureFrame()) return;
            var focus = ScenarioLoader.A(scenarioCapture, "focus");
            if (focus != null)
            {
                var target = scenarioBackend.One(focus);
                if (!target.Spawned || target.Position.Fogged(target.Map)) throw new InvalidOperationException("Screenshot target is not visible");
            }
            scenarioCaptured = screenshotService.TryCaptureMap(scenarioResult, true, true, results.Count, ScenarioLoader.A(scenarioCapture, "id"), null);
            if (scenarioCaptured) scenarioExecution.Mark("screenshot.captured", Find.TickManager.TicksGame, scenarioResult.ScreenshotPath);
        }
    }
}
