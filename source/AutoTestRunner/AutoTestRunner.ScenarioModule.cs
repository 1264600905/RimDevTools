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
        private ScenarioExecution scenarioExecution;
        private ScenarioGameBackend scenarioBackend;
        private AutoTestResult scenarioResult;
        private XElement scenarioCapture;
        private int scenarioCaptureFrame;
        private bool scenarioCaptured;
        private bool pollingScenario;

        private void InitializeScenarios()
        {
            scenarioQueue = new Queue<ScenarioCase>();
            evidenceManifest.Suite = "ExternalScenario";
            try
            {
                var frozen = JsonSerializer.Deserialize<ScenarioPlan>(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(settings.autoTestScenarioPlan)));
                if (frozen == null || frozen.SchemaVersion != 1) throw new ScenarioConfigurationException("Invalid frozen scenario plan");
                var plan = ScenarioLoader.Compile(frozen.Inputs);
                foreach (var definition in plan.Cases)
                {
                    scenarioQueue.Enqueue(definition);
                    var entry = new AutoTestManifestEntry { CaseId = definition.CaseId, CaseType = "ExternalScenario", Scope = "ExternalScenario", Category = "Scenario", DefType = "TestRule", DefName = definition.TestDefName, Label = definition.Name, Planned = true, DiscoverOrder = discoveredManifest.Count + 1, PlannedOrder = plannedManifest.Count + 1 };
                    discoveredManifest.Add(entry); plannedManifest.Add(entry);
                }
                Log.Message("[LogSyncAutoTest] Frozen external XML plan: " + plan.Cases.Count + " cases; ordinary coverage is not replaced.");
            }
            catch (Exception ex)
            {
                evidenceBuilder.AddError(new AutoTestOperationError { Code = "scenario.configuration", Message = ex.ToString() });
                Finish();
            }
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
                    if (scenarioQueue.Count == 0) { Finish(); return; }
                    var definition = scenarioQueue.Dequeue();
                    scenarioResult = new AutoTestResult { PackageId = targetPackageId, CaseId = definition.CaseId, CaseType = "ExternalScenario", Scope = "ExternalScenario", Category = "Scenario", DefType = "TestRule", DefName = definition.TestDefName, Label = definition.Name, Status = "running", Stage = "prepare" };
                    RegisterSemanticCase(scenarioResult);
                    scenarioBackend = new ScenarioGameBackend(map, pocketMapService, QueueScenarioScreenshot,
                        (stage, details) => scenarioExecution?.Mark(stage, Find.TickManager.TicksGame, details));
                    scenarioExecution = new ScenarioExecution(definition, scenarioBackend, evidenceClock);
                    ScenarioGameBackend.NativeObservation = () => {
                        try { scenarioExecution?.Observe(Find.TickManager.TicksGame); }
                        catch (Exception ex) { scenarioObservationError = ex; }
                    };
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
                    var outcome = scenarioExecution.Outcome;
                    scenarioResult.Scenario = outcome;
                    scenarioResult.Status = outcome.Status;
                    scenarioResult.Stage = outcome.Status == "passed" ? "completed" : outcome.ErrorCode;
                    scenarioResult.ErrorMessage = outcome.Error;
                    scenarioResult.ResultReasonCode = outcome.ErrorCode;
                    scenarioResult.Notes = "testDefName=" + outcome.TestDefName + "; inputHash=" + outcome.InputHash + "; source=" + outcome.Source;
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
                        foreach (var remaining in scenarioQueue)
                            results.Add(new AutoTestResult { PackageId = targetPackageId, CaseId = remaining.CaseId, CaseType = "ExternalScenario", Scope = "ExternalScenario", Category = "Scenario", DefType = "TestRule", DefName = remaining.TestDefName, Label = remaining.TestDefName, Status = "cancelled", Stage = "cancel", ErrorMessage = "Explicit cancellation before scene preparation" });
                        scenarioQueue.Clear(); cancelRequested = false;
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
