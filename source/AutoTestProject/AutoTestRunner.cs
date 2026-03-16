using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal enum AutoTestKind
    {
        Building,
        Item,
        Terrain
    }

    internal sealed class AutoTestWorkItem
    {
        public AutoTestKind Kind;
        public ThingDef ThingDef;
        public TerrainDef TerrainDef;
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
        public string Notes;
    }

    internal sealed class AutoTestRunner
    {
        private enum SessionPhase
        {
            WaitingForPreOpenSettle,
            WaitingForInfoCardOpen,
            WaitingBeforeClose,
            WaitingForInfoCardClose
        }

        private sealed class ActiveSession
        {
            public AutoTestWorkItem Item;
            public AutoTestResult Result;
            public Thing SpawnedThing;
            public TerrainDef TerrainDef;
            public Def InfoCardDef;
            public IAutoTestUiHandle UiHandle;
            public bool CloseIssued;
            public bool ScreenshotCaptured;
            public int OpenStableRepaintFrames;
            public int RemainingUiFrames;
            public SessionPhase Phase;
        }

        private const int PreOpenSettleRepaintFrames = 10;
        private const int OpenStableFrameRequirement = 3;
        private const int OpenTimeoutRepaintFrames = 180;
        private const int WaitBeforeCloseRepaintFrames = 12;
        private const int CloseTimeoutRepaintFrames = 180;

        private readonly LogSyncSettings settings;
        private readonly Map map;
        private readonly Queue<AutoTestWorkItem> pendingItems = new Queue<AutoTestWorkItem>();
        private readonly List<AutoTestResult> results = new List<AutoTestResult>();

        private ModContentPack targetMod;
        private string targetPackageId;
        private string outputDirectory;
        private int nextThingX = 10;
        private int nextThingZ = 10;
        private int currentThingRowDepth;
        private int nextTerrainX;
        private int nextTerrainZ = 10;
        private bool completed;
        private ActiveSession activeSession;
        private AutoTestRunArtifacts runArtifacts;

        public AutoTestRunner(LogSyncSettings settings, Map map)
        {
            this.settings = settings;
            this.map = map;
        }

        public bool Completed => completed;

        public void Initialize()
        {
            targetPackageId = (settings.autoTestTargetPackageId ?? string.Empty).Trim().ToLowerInvariant();
            outputDirectory = ResolveOutputDirectory(settings);
            runArtifacts = AutoTestReportWriter.CreateRunArtifacts(outputDirectory, targetPackageId);
            targetMod = LoadedModManager.RunningMods.FirstOrDefault(mod =>
                string.Equals(mod.PackageIdPlayerFacing, targetPackageId, StringComparison.OrdinalIgnoreCase));

            if (targetMod == null)
            {
                Log.Error("[LogSyncAutoTest] Target mod not found: " + targetPackageId);
                Finish();
                return;
            }

            nextTerrainX = Math.Max(map.Size.x / 2, 10);

            EnqueueThingDefs();
            EnqueueTerrainDefs();
            ApplyBatchLimit();

            Log.Message(string.Format(
                "[LogSyncAutoTest] START packageId={0} map={1} pending={2}",
                targetPackageId,
                map.info?.parent?.LabelCap ?? map.ToString(),
                pendingItems.Count));

            if (pendingItems.Count == 0)
            {
                Log.Warning("[LogSyncAutoTest] No matching buildings/items/terrains were found for " + targetPackageId);
                Finish();
            }
        }

        public void Tick()
        {
            if (completed) return;

            if (activeSession != null)
            {
                return;
            }

            if (pendingItems.Count == 0)
            {
                Finish();
                return;
            }

            StartNextSession();
        }

        public void ProcessUi()
        {
            if (completed || activeSession == null) return;
            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            try
            {
                switch (activeSession.Phase)
                {
                    case SessionPhase.WaitingForPreOpenSettle:
                        ProcessWaitingForPreOpenSettle();
                        break;
                    case SessionPhase.WaitingForInfoCardOpen:
                        ProcessWaitingForInfoCardOpen();
                        break;
                    case SessionPhase.WaitingBeforeClose:
                        ProcessWaitingBeforeClose();
                        break;
                    case SessionPhase.WaitingForInfoCardClose:
                        ProcessWaitingForInfoCardClose();
                        break;
                }
            }
            catch (Exception ex)
            {
                activeSession.Result.Status = activeSession.Phase == SessionPhase.WaitingForInfoCardClose
                    ? "failed_info_card_close_request"
                    : "failed_info_card_open_request";
                activeSession.Result.Stage = activeSession.Phase == SessionPhase.WaitingForInfoCardClose ? "info_close" : "info_open";
                activeSession.Result.ErrorMessage = ex.ToString();
                CleanupSelectionAndWindows();
                activeSession = null;
            }
        }

        private void StartNextSession()
        {
            AutoTestWorkItem item = pendingItems.Dequeue();
            switch (item.Kind)
            {
                case AutoTestKind.Building:
                case AutoTestKind.Item:
                    StartThingSession(item);
                    break;
                case AutoTestKind.Terrain:
                    StartTerrainSession(item);
                    break;
            }
        }

        private void StartThingSession(AutoTestWorkItem item)
        {
            AutoTestResult result = CreateResult(item.Kind.ToString(), "ThingDef", item.ThingDef.defName, item.ThingDef.label);
            results.Add(result);

            try
            {
                ThingDef def = item.ThingDef;
                ThingDef stuff = ResolveStuff(def);
                Thing thing = ThingMaker.MakeThing(def, stuff);
                result.Stage = "create";

                IntVec2 size = def.Size;
                int width = Math.Max(1, size.x);
                int height = Math.Max(1, size.z);
                if (!TryAllocateThingRoot(width, height, out IntVec3 rootCell))
                {
                    result.Status = "skipped";
                    result.Stage = "layout";
                    result.ErrorMessage = "No free layout space remained on the current map.";
                    return;
                }

                ClearArea(rootCell, width, height);

                if (item.Kind == AutoTestKind.Building)
                {
                    GenSpawn.Spawn(thing, rootCell, map, Rot4.North, WipeMode.Vanish, false, false);
                }
                else
                {
                    GenSpawn.Spawn(thing, rootCell, map, WipeMode.Vanish);
                }

                result.Stage = "spawn";
                result.Label = thing.LabelCap;
                result.Notes = TrimForNote(thing.GetInspectString());

                int gizmoCount = 0;
                foreach (Gizmo gizmo in thing.GetGizmos())
                {
                    if (gizmo != null) gizmoCount++;
                }
                result.Notes = AppendNote(result.Notes, "Gizmos=" + gizmoCount);

                Find.Selector.ClearSelection();
                Find.Selector.Select(thing, false, false);

                activeSession = new ActiveSession
                {
                    Item = item,
                    Result = result,
                    SpawnedThing = thing,
                    InfoCardDef = def,
                    RemainingUiFrames = PreOpenSettleRepaintFrames,
                    Phase = SessionPhase.WaitingForPreOpenSettle
                };
                result.Stage = "info_open";
            }
            catch (Exception ex)
            {
                result.Status = result.Stage == "create" ? "failed_spawn" : "failed_create";
                result.ErrorMessage = ex.ToString();
                CleanupSelectionAndWindows();
            }
        }

        private void StartTerrainSession(AutoTestWorkItem item)
        {
            AutoTestResult result = CreateResult("Terrain", "TerrainDef", item.TerrainDef.defName, item.TerrainDef.label);
            results.Add(result);

            try
            {
                if (!TryAllocateTerrainCell(out IntVec3 cell))
                {
                    result.Status = "skipped";
                    result.Stage = "layout";
                    result.ErrorMessage = "No free terrain layout space remained on the current map.";
                    return;
                }

                ClearArea(cell, 1, 1);
                map.terrainGrid.SetTerrain(cell, item.TerrainDef);
                TerrainDef applied = map.terrainGrid.TerrainAt(cell);
                result.Notes = applied == null ? "Terrain applied but could not be read back." : "Applied terrain: " + applied.defName;

                activeSession = new ActiveSession
                {
                    Item = item,
                    Result = result,
                    TerrainDef = item.TerrainDef,
                    InfoCardDef = item.TerrainDef,
                    RemainingUiFrames = PreOpenSettleRepaintFrames,
                    Phase = SessionPhase.WaitingForPreOpenSettle
                };
                result.Stage = "info_open";
            }
            catch (Exception ex)
            {
                result.Status = "failed_terrain_apply";
                result.Stage = "terrain";
                result.ErrorMessage = ex.ToString();
                CleanupSelectionAndWindows();
            }
        }

        private void ProcessWaitingForPreOpenSettle()
        {
            if (activeSession == null) return;

            activeSession.RemainingUiFrames--;
            if (activeSession.RemainingUiFrames > 0) return;

            activeSession.OpenStableRepaintFrames = 0;
            activeSession.RemainingUiFrames = OpenTimeoutRepaintFrames;
            activeSession.Phase = SessionPhase.WaitingForInfoCardOpen;
            Log.Message("[LogSyncAutoTest] InfoCard pre-open settle completed for " + activeSession.Result.DefName);
        }

        private void ProcessWaitingForInfoCardOpen()
        {
            if (activeSession == null) return;

            if (activeSession.UiHandle == null)
            {
                OpenInfoCardOnUiThread();
                return;
            }

            if (IsInfoCardPresent())
            {
                if (activeSession.OpenStableRepaintFrames == 0)
                {
                    InfoCardCloseTrace.AddReason("OpenDetected");
                    Log.Message("[LogSyncAutoTest] InfoCard visible for " + activeSession.Result.DefName);
                }

                activeSession.OpenStableRepaintFrames++;
                if (activeSession.OpenStableRepaintFrames >= OpenStableFrameRequirement)
                {
                    InfoCardCloseTrace.AddReason("OpenConfirmed.RepaintFrames=" + activeSession.OpenStableRepaintFrames);
                    Log.Message("[LogSyncAutoTest] InfoCard open confirmed for " + activeSession.Result.DefName);
                    TryCaptureInfoCardScreenshot();
                    activeSession.Phase = SessionPhase.WaitingBeforeClose;
                    activeSession.RemainingUiFrames = WaitBeforeCloseRepaintFrames;
                }
                return;
            }

            activeSession.OpenStableRepaintFrames = 0;
            activeSession.RemainingUiFrames--;
            if (activeSession.RemainingUiFrames > 0) return;

            FailActiveSession(
                "failed_info_card_open",
                "info_open",
                "InfoCard did not stay open long enough to confirm opening. " + BuildWindowSummary());
        }

        private void ProcessWaitingBeforeClose()
        {
            if (activeSession == null) return;

            if (!IsInfoCardPresent())
            {
                FailActiveSession(
                    "failed_info_card_interrupted",
                    "info_open",
                    "InfoCard disappeared before auto-test requested close.");
                return;
            }

            activeSession.RemainingUiFrames--;
            if (activeSession.RemainingUiFrames > 0) return;

            if (activeSession.UiHandle == null || !activeSession.UiHandle.IsOpen)
            {
                FailActiveSession(
                    "failed_info_card_interrupted",
                    "info_open",
                    "InfoCard disappeared before auto-test requested close.");
                return;
            }

            activeSession.Phase = SessionPhase.WaitingForInfoCardClose;
            activeSession.CloseIssued = false;
            activeSession.RemainingUiFrames = CloseTimeoutRepaintFrames;
            Log.Message("[LogSyncAutoTest] InfoCard close phase entered for " + activeSession.Result.DefName);
        }

        private void ProcessWaitingForInfoCardClose()
        {
            if (activeSession == null) return;

            if (!IsInfoCardPresent())
            {
                PassActiveSession();
                return;
            }

            if (!activeSession.CloseIssued &&
                activeSession.UiHandle != null &&
                activeSession.UiHandle.IsOpen)
            {
                InfoCardCloseTrace.AddReason("CloseActionIssued.CloseXClick");
                activeSession.UiHandle.RequestClose();
                activeSession.CloseIssued = true;
                Log.Message("[LogSyncAutoTest] InfoCard close requested for " + activeSession.Result.DefName);
            }

            activeSession.RemainingUiFrames--;
            if (activeSession.RemainingUiFrames > 0) return;

            FailActiveSession(
                "failed_info_card_close_timeout",
                "info_close",
                "InfoCard remained open after auto-test requested close.");
        }

        private bool IsInfoCardPresent()
        {
            if (activeSession?.UiHandle != null && activeSession.UiHandle.IsOpen)
            {
                return true;
            }

            Dialog_InfoCard currentInfoCard = Find.WindowStack?.WindowOfType<Dialog_InfoCard>();
            if (currentInfoCard == null) return false;

            activeSession.UiHandle = new AutoTestInfoCardHandle(currentInfoCard, activeSession.InfoCardDef);
            InfoCardCloseTrace.Attach(currentInfoCard);
            return true;
        }

        private void CleanupSelectionAndWindows()
        {
            try
            {
                if (activeSession?.UiHandle != null)
                {
                    activeSession.UiHandle.ForceClose();
                }
                else
                {
                    CloseExistingInfoCards();
                }
            }
            catch
            {
            }
            Find.Selector.ClearSelection();
        }

        private void FailActiveSession(string status, string stage, string errorMessage)
        {
            if (activeSession == null) return;

            activeSession.Result.Status = status;
            activeSession.Result.Stage = stage;
            activeSession.Result.ErrorMessage = errorMessage;
            activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "Trace=" + InfoCardCloseTrace.Describe());
            CleanupSelectionAndWindows();
            InfoCardCloseTrace.Stop();
            activeSession = null;
        }

        private void PassActiveSession()
        {
            if (activeSession == null) return;

            if (activeSession.SpawnedThing != null && !activeSession.SpawnedThing.Destroyed)
            {
                activeSession.Result.Label = activeSession.SpawnedThing.LabelCap;
                activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "InfoCardOpened=True");
            }
            else if (activeSession.TerrainDef != null)
            {
                activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "InfoCardOpened=True");
            }

            activeSession.Result.Status = "passed";
            activeSession.Result.Stage = "info_close";
            activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "Trace=" + InfoCardCloseTrace.Describe());
            Log.Message("[LogSyncAutoTest] InfoCard close confirmed for " + activeSession.Result.DefName);
            CleanupSelectionAndWindows();
            InfoCardCloseTrace.Stop();
            activeSession = null;
        }

        private void EnqueueThingDefs()
        {
            if (settings.autoTestBuildings)
            {
                foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading
                    .Where(IsTargetBuildingDef)
                    .OrderBy(def => def.defName))
                {
                    pendingItems.Enqueue(new AutoTestWorkItem { Kind = AutoTestKind.Building, ThingDef = def });
                }
            }

            if (settings.autoTestItems)
            {
                foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading
                    .Where(IsTargetItemDef)
                    .OrderBy(def => def.defName))
                {
                    pendingItems.Enqueue(new AutoTestWorkItem { Kind = AutoTestKind.Item, ThingDef = def });
                }
            }
        }

        private void EnqueueTerrainDefs()
        {
            if (!settings.autoTestTerrains) return;

            foreach (TerrainDef def in DefDatabase<TerrainDef>.AllDefsListForReading
                .Where(IsTargetTerrainDef)
                .OrderBy(def => def.defName))
            {
                pendingItems.Enqueue(new AutoTestWorkItem { Kind = AutoTestKind.Terrain, TerrainDef = def });
            }
        }

        private bool IsTargetBuildingDef(ThingDef def)
        {
            return def != null &&
                   def.modContentPack == targetMod &&
                   def.category == ThingCategory.Building &&
                   def.thingClass != null &&
                   !def.IsBlueprint &&
                   !def.IsFrame;
        }

        private bool IsTargetItemDef(ThingDef def)
        {
            return def != null &&
                   def.modContentPack == targetMod &&
                   def.category == ThingCategory.Item &&
                   def.thingClass != null &&
                   !def.IsBlueprint &&
                   !def.IsFrame;
        }

        private bool IsTargetTerrainDef(TerrainDef def)
        {
            return def != null && def.modContentPack == targetMod;
        }

        private ThingDef ResolveStuff(ThingDef def)
        {
            if (def == null || !def.MadeFromStuff) return null;
            if (def.defaultStuff != null) return def.defaultStuff;
            if (def.stuffCategories == null || def.stuffCategories.Count == 0) return null;

            foreach (ThingDef candidate in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (candidate == null || !candidate.IsStuff || candidate.stuffProps == null || candidate.stuffProps.categories == null)
                    continue;

                if (candidate.stuffProps.categories.Any(category => def.stuffCategories.Contains(category)))
                {
                    return candidate;
                }
            }

            return null;
        }

        private bool TryAllocateThingRoot(int width, int height, out IntVec3 rootCell)
        {
            int safeWidth = Math.Max(1, width);
            int safeHeight = Math.Max(1, height);

            if (nextThingX + safeWidth + 5 >= map.Size.x)
            {
                nextThingX = 10;
                nextThingZ += currentThingRowDepth + 4;
                currentThingRowDepth = 0;
            }

            if (nextThingZ + safeHeight + 5 >= map.Size.z)
            {
                rootCell = IntVec3.Invalid;
                return false;
            }

            rootCell = new IntVec3(nextThingX, 0, nextThingZ);
            nextThingX += safeWidth + 2;
            currentThingRowDepth = Math.Max(currentThingRowDepth, safeHeight);
            return true;
        }

        private bool TryAllocateTerrainCell(out IntVec3 cell)
        {
            if (nextTerrainX + 2 >= map.Size.x)
            {
                nextTerrainX = Math.Max(map.Size.x / 2, 10);
                nextTerrainZ += 2;
            }

            if (nextTerrainZ + 2 >= map.Size.z)
            {
                cell = IntVec3.Invalid;
                return false;
            }

            cell = new IntVec3(nextTerrainX, 0, nextTerrainZ);
            nextTerrainX += 2;
            return true;
        }

        private void ClearArea(IntVec3 rootCell, int width, int height)
        {
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    IntVec3 cell = new IntVec3(rootCell.x + x, 0, rootCell.z + z);
                    if (!cell.InBounds(map)) continue;

                    foreach (Thing thing in cell.GetThingList(map).ToList())
                    {
                        if (thing == null || thing.Destroyed) continue;
                        try
                        {
                            thing.Destroy(DestroyMode.Vanish);
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        private void CloseExistingInfoCards()
        {
            Find.WindowStack.TryRemoveAssignableFromType(typeof(Dialog_InfoCard), false);
        }

        private void OpenInfoCardOnUiThread()
        {
            if (activeSession == null || activeSession.InfoCardDef == null || activeSession.UiHandle != null) return;

            activeSession.UiHandle = activeSession.SpawnedThing != null
                ? AutoTestInfoCardUiAdapter.Open(activeSession.SpawnedThing)
                : AutoTestInfoCardUiAdapter.Open(activeSession.InfoCardDef);
            InfoCardCloseTrace.Begin(activeSession.Result.DefName);
            InfoCardCloseTrace.Attach(Find.WindowStack.WindowOfType<Dialog_InfoCard>());
            InfoCardCloseTrace.AddReason("OpenRequested");

            Log.Message(string.Format(
                "[LogSyncAutoTest] InfoCard requested for {0} handle={1} windows={2} summary={3}",
                activeSession.Result.DefName,
                activeSession.UiHandle.DebugName,
                Find.WindowStack.Windows.Count,
                BuildWindowSummary()));
        }

        private AutoTestResult CreateResult(string category, string defType, string defName, string label)
        {
            return new AutoTestResult
            {
                PackageId = targetPackageId,
                Category = category,
                DefType = defType,
                DefName = defName ?? string.Empty,
                Label = label ?? string.Empty,
                Status = "pending",
                Stage = "collect",
                ErrorMessage = string.Empty,
                ScreenshotPath = string.Empty,
                Notes = string.Empty
            };
        }

        private void Finish()
        {
            if (completed) return;
            completed = true;
            CleanupSelectionAndWindows();

            AutoTestReportWriter.Write(runArtifacts, targetPackageId ?? "unknown", results, targetMod?.Name ?? "Unknown");
            Log.Message(string.Format(
                "[LogSyncAutoTest] COMPLETED packageId={0} total={1} passed={2} failed={3}",
                targetPackageId ?? "unknown",
                results.Count,
                results.Count(result => result.Status == "passed"),
                results.Count(result => result.Status != "passed" && result.Status != "skipped")));
        }

        private static string ResolveOutputDirectory(LogSyncSettings settings)
        {
            string configuredPath = (settings.autoTestOutputPath ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(configuredPath))
            {
                return configuredPath;
            }

            string logDirectory = Path.GetDirectoryName(settings.logPath ?? string.Empty);
            if (!string.IsNullOrEmpty(logDirectory))
            {
                return Path.Combine(logDirectory, "AutoTest");
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RimWorldAutoTest");
        }

        private static string TrimForNote(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            string normalized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return normalized.Length <= 160 ? normalized : normalized.Substring(0, 160) + "...";
        }

        private static string AppendNote(string existing, string next)
        {
            if (string.IsNullOrWhiteSpace(existing)) return next ?? string.Empty;
            if (string.IsNullOrWhiteSpace(next)) return existing;
            return existing + " | " + next;
        }

        private string BuildWindowSummary()
        {
            if (Find.WindowStack == null) return "WindowStack=null";
            return string.Join(", ", Find.WindowStack.Windows.Select(window => window.GetType().Name).ToArray());
        }

        private void ApplyBatchLimit()
        {
            int limit = Math.Max(1, settings.autoTestBatchSize);
            if (pendingItems.Count <= limit) return;

            AutoTestWorkItem[] selectedItems = pendingItems.Take(limit).ToArray();
            pendingItems.Clear();
            foreach (AutoTestWorkItem item in selectedItems)
            {
                pendingItems.Enqueue(item);
            }

            Log.Message(string.Format(
                "[LogSyncAutoTest] Batch limit applied. Keeping first {0} items.",
                pendingItems.Count));
        }

        private void TryCaptureInfoCardScreenshot()
        {
            if (activeSession == null || activeSession.ScreenshotCaptured || !settings.autoTestCaptureScreenshots)
            {
                return;
            }

            if (runArtifacts == null)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(runArtifacts.ScreenshotDirectory);
                string fileName = string.Format(
                    "{0:D4}_{1}.png",
                    results.Count,
                    SanitizeFileName(activeSession.Result.DefName));
                string screenshotPath = Path.Combine(runArtifacts.ScreenshotDirectory, fileName);

                ScreenCapture.CaptureScreenshot(screenshotPath);
                activeSession.Result.ScreenshotPath = screenshotPath;
                activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "ScreenshotCaptured=True");
                activeSession.ScreenshotCaptured = true;

                Log.Message("[LogSyncAutoTest] Screenshot requested for " + activeSession.Result.DefName + " -> " + screenshotPath);
            }
            catch (Exception ex)
            {
                activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "ScreenshotError=" + TrimForNote(ex.Message));
                Log.Warning("[LogSyncAutoTest] Screenshot capture failed for " + activeSession.Result.DefName + ": " + ex.Message);
            }
        }

        private static string SanitizeFileName(string text)
        {
            string value = string.IsNullOrWhiteSpace(text) ? "autotest" : text;
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalidChar, '_');
            }

            return value;
        }
    }
}
