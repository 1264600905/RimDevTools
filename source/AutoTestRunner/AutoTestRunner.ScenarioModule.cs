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
        private float scenarioCaptureStartedRealtime;
        private bool scenarioCaptured;
        private bool pollingScenario;
        private XElement scenarioView;
        private int scenarioViewIndex;
        private readonly List<string> scenarioViewImages = new List<string>();
        private readonly List<Rect> scenarioViewPawnViewports = new List<Rect>();
        private Rect? scenarioViewPawnViewport;
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
                    if (manualOptions != null) { scenarioResult.Scope = "View"; scenarioResult.Category = "LoadoutView"; scenarioResult.DefType = "PawnKindDef"; scenarioResult.DefName = manualOptions.Choice.NativeDefName; }
                    RegisterSemanticCase(scenarioResult);
                    scenarioBackend = new ScenarioGameBackend(map, pocketMapService, QueueScenarioScreenshot,
                        (stage, details) => scenarioExecution?.Mark(stage, Find.TickManager.TicksGame, details), CreateScenarioViewStudio);
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
                    scenarioExecution = null; scenarioBackend = null; scenarioCapture = null; scenarioCaptured = false; scenarioView = null; scenarioViewImages.Clear();
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
            if (step.Name == "View") return QueueScenarioView(step);
            if (scenarioCapture == null)
            {
                scenarioCapture = step;
                scenarioCaptureFrame = Time.frameCount;
                scenarioCaptureStartedRealtime = Time.realtimeSinceStartup;
                scenarioCaptured = false;
                var focus = ScenarioLoader.A(step, "focus");
                Current.Game.CurrentMap = scenarioBackend.Map;
                Find.World.renderer.wantedMode = WorldRenderMode.None;
                var cell = scenarioBackend.CaptureCell(focus);
                Find.CameraDriver.SetRootPosAndSize(new Vector3(cell.x + .5f, 0, cell.z + .5f), (float)ScenarioLoader.D(step, "cameraSize", 12));
                return false;
            }
            if (!scenarioCaptured) return false;
            scenarioCapture = null;
            return true;
        }

        private void CaptureScenarioUi()
        {
            if (scenarioView != null && !scenarioCaptured) scenarioBackend.MaintainViewFrame();
            if (scenarioView != null) { AutoTestScreenshotOverlay.Request(scenarioResult); AutoTestScreenshotOverlay.DrawOverlay(); }
            if (scenarioCapture == null || scenarioCaptured || Time.frameCount - scenarioCaptureFrame < ScenarioLoader.I(scenarioCapture, "stableFrames", 3) || Event.current.type != EventType.Repaint) return;
            if (scenarioView != null && Time.realtimeSinceStartup - scenarioCaptureStartedRealtime < 1f) return;
            if (Find.CurrentMap != scenarioBackend.Map) return;
            if (!AutoTestScreenshotService.PrepareCaptureFrame()) return;
            var focus = ScenarioLoader.A(scenarioCapture, "focus");
            if (focus != null)
            {
                var cell = scenarioBackend.CaptureCell(focus);
                if (!cell.InBounds(scenarioBackend.Map) || cell.Fogged(scenarioBackend.Map)) throw new InvalidOperationException("Screenshot target is not visible");
            }
            if (scenarioView != null) scenarioBackend.ValidateViewFrame(ScenarioViews.Directions(scenarioView)[scenarioViewIndex]);
            scenarioCaptured = screenshotService.TryCaptureMap(scenarioResult, true, true, results.Count, ScenarioLoader.A(scenarioCapture, "id"), null);
            if (scenarioCaptured && scenarioView != null)
                scenarioViewPawnViewport = externalPlan != null && manualOptions == null && ScenarioLoader.A(scenarioView, "composite", "true") == "true"
                    ? scenarioBackend.CompositePawnViewport() : null;
            if (scenarioCaptured) scenarioExecution.Mark("screenshot.captured", Find.TickManager.TicksGame, scenarioResult.ScreenshotPath);
        }

        private ScenarioViewStudio CreateScenarioViewStudio(XElement scene)
        {
            if (scene.Attribute("generatorDef") != null) throw new ScenarioConfigurationException("View uses the existing main-map studio; generatorDef belongs to map-based behavior tests");
            var width = ScenarioLoader.I(scene, "sizeX", 50); var height = ScenarioLoader.I(scene, "sizeZ", 50);
            if (!TryAllocateThreeViewStudioRoot(width, height, out var root, excludeExistingPawns: true))
            {
                // Keep the existing studio renderer and preparation policy. A crowded, mountainous
                // or roofed player map must not make an otherwise valid external view un-runnable.
                var temporary = new AutoTestMainMap(map, 170031, new IntVec3(Math.Max(90, width + 30), 1, Math.Max(90, height + 30)));
                var studioRoot = new IntVec3(15, 0, 15);
                try
                {
                    threeViewService.PrepareStudioArea(temporary.Map, studioRoot, width, height);
                    scenarioExecution?.Mark("studio.host", Find.TickManager.TicksGame, "temporary ordinary main map=" + temporary.Map.uniqueID + "; original=" + map.uniqueID + "; pocketMap=false");
                    return new ScenarioViewStudio { Map = temporary.Map, Root = studioRoot, Width = width, Height = height, Restore = temporary.Dispose };
                }
                catch { temporary.Dispose(); throw; }
            }
            var area = new CellRect(root.x, root.z, width, height);
            if (map.mapPawns.AllPawnsSpawned.Any(p => area.Contains(p.Position))) throw new ScenarioConfigurationException("Studio area contains existing Pawns; they must not be displaced");
            var snapshot = new StaticViewContext { Map = map, RootCell = root, AnchorCell = root + new IntVec3(width / 2, 0, height / 2) };
            var previousWeather = map.weatherManager.curWeather; var previousLastWeather = map.weatherManager.lastWeather;
            var previousCameraPosition = Find.CameraDriver.GetComponent<Camera>().transform.position; var previousCameraSize = Find.CameraDriver.RootSize;
            var previousSelection = Find.Selector.SelectedObjectsListForReading.OfType<Thing>().ToList();
            Action restore = () => {
                map.weatherManager.TransitionTo(previousWeather); map.weatherManager.curWeather = previousWeather; map.weatherManager.lastWeather = previousLastWeather;
                if (!RestoreStaticViewContext(snapshot, scenarioResult)) throw new InvalidOperationException("Existing three-view studio could not restore its source area");
                Find.Selector.ClearSelection();
                foreach (var thing in previousSelection.Where(t => !t.Destroyed && t.Spawned && t.Map == map)) Find.Selector.Select(thing, false, false);
                Find.CameraDriver.SetRootPosAndSize(previousCameraPosition, previousCameraSize);
            };
            if (!TrySnapshotAndClearStaticViewArea(snapshot, width, height, out var error)) { restore(); throw new ScenarioConfigurationException(error); }
            threeViewService.PrepareStudioArea(map, root, width, height);
            return new ScenarioViewStudio { Map = map, Root = root, Width = width, Height = height, Restore = restore };
        }

        private bool QueueScenarioView(XElement step)
        {
            var directions = ScenarioViews.Directions(step);
            if (scenarioView == null)
            {
                if (!scenarioBackend.CanBeginView) return false;
                scenarioView = step; scenarioViewIndex = 0; scenarioViewImages.Clear(); scenarioViewPawnViewports.Clear(); scenarioViewPawnViewport = null;
                scenarioBackend.BeginView(step);
                scenarioBackend.UpdateView(directions[0]);
                scenarioResult.OverlayLine1 = scenarioResult.Label;
                scenarioResult.OverlayLine2 = "Same actor: " + ScenarioLoader.A(step, "actor") + " / " + directions[0];
                scenarioResult.OverlayLine3 = "Pose: " + ScenarioLoader.A(step, "pose", "standing") + " / loadout: " + ScenarioLoader.A(step, "loadout", "preserve");
                scenarioResult.OverlayLine4 = scenarioBackend.ViewEquipmentSummary;
            }
            if (scenarioCaptured && !AutoTestViewComposite.IsReady(scenarioResult.ScreenshotPath)) return false;
            var frame = new XElement("Screenshot", new XAttribute("id", ScenarioLoader.A(step, "id") + "_" + directions[scenarioViewIndex]),
                new XAttribute("focus", ScenarioLoader.A(step, "actor")), new XAttribute("cameraSize", ScenarioLoader.A(step, "cameraSize", "11")), new XAttribute("stableFrames", ScenarioLoader.A(step, "stableFrames", "5")));
            if (!QueueScenarioScreenshot(frame)) return false;
            scenarioViewImages.Add(scenarioResult.ScreenshotPath);
            if (scenarioViewPawnViewport.HasValue) scenarioViewPawnViewports.Add(scenarioViewPawnViewport.Value);
            scenarioCaptured = false;
            scenarioViewIndex++;
            if (scenarioViewIndex < directions.Length)
            {
                scenarioBackend.UpdateView(directions[scenarioViewIndex]);
                scenarioResult.OverlayLine2 = "Same actor: " + ScenarioLoader.A(step, "actor") + " / " + directions[scenarioViewIndex];
                return false;
            }
            if (ScenarioLoader.A(step, "composite", "true") == "true")
            {
                var path = AutoTestViewComposite.Compose(scenarioViewImages, scenarioViewPawnViewports.Count > 0 ? scenarioViewPawnViewports : null);
                scenarioResult.ScreenshotPaths.Add(path);
                scenarioExecution.Mark("view.composite", Find.TickManager.TicksGame, path);
            }
            scenarioBackend.CompleteView();
            scenarioView = null; scenarioViewImages.Clear(); scenarioViewPawnViewports.Clear(); scenarioViewPawnViewport = null;
            return true;
        }
    }
}
