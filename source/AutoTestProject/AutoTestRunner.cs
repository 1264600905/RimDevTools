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
        Terrain,
        Recipe,
        Hediff,
        TurretPocketMap // 新增炮塔测试类型
    }

    // 新增：炮塔测试阶段枚举
    internal enum TurretTestPhase
    {
        Initializing,
        SpawningPower,
        SpawningTurret,
        // 后续阶段占位
        CleaningUp,
        Done
    }

    internal sealed class AutoTestWorkItem
    {
        public AutoTestKind Kind;
        public ThingDef ThingDef;
        public TerrainDef TerrainDef;
        public RecipeDef RecipeDef;
        public ThingDef WorktableDef;
        public HediffDef HediffDef;
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
        // --- 原有内部类 ---
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
            public Pawn TestPawn;
            public Hediff AttachedHediff;
            public IAutoTestUiHandle UiHandle;
            public bool CloseIssued;
            public bool ScreenshotCaptured;
            public int OpenStableRepaintFrames;
            public int RemainingUiFrames;
            public SessionPhase Phase;
        }

        // --- 新增：炮塔测试会话类 ---
        private sealed class TurretTestSession
        {
            public Map TestMap;
            public ThingDef TurretDef;
            public Thing SpawnedTurret;
            public Thing PowerGenerator;
            public TurretTestPhase Phase;
            public AutoTestResult Result;
            public int TimeoutTicks;
            public RimWorld.Planet.MapParent Parent; // 持有父对象

            public TurretTestSession(ThingDef turretDef, AutoTestResult result)
            {
                TurretDef = turretDef;
                Result = result;
                Phase = TurretTestPhase.Initializing;
                TimeoutTicks = 60 * 20; // 20秒超时
            }

            public void Update()
            {
                if (TimeoutTicks > 0)
                {
                    TimeoutTicks--;
                    if (TimeoutTicks == 0)
                    {
                        Fail("timeout");
                        return;
                    }
                }
                
                try
                {
                    switch (Phase)
                    {
                        case TurretTestPhase.Initializing:
                            Phase = TurretTestPhase.SpawningPower;
                            break;

                        case TurretTestPhase.SpawningPower:
                            IntVec3 powerPos = new IntVec3(3, 0, 3);
                            PowerGenerator = GenSpawn.Spawn(ThingDef.Named("LogSync_AutoTestPowerGenerator"), powerPos, TestMap);
                            Result.Notes = AppendNote(Result.Notes, "MapSize=" + TestMap.Size.x + "x" + TestMap.Size.z);
                            Result.Stage = "power";
                            Phase = TurretTestPhase.SpawningTurret;
                            break;

                        case TurretTestPhase.SpawningTurret:
                            SpawnedTurret = GenSpawn.Spawn(TurretDef, TestMap.Center, TestMap);
                            Result.Label = SpawnedTurret.LabelCap;
                            Result.Stage = "spawn";
                            
                            Phase = TurretTestPhase.CleaningUp;
                            Result.Status = "passed";
                            Log.Message($"[LogSyncAutoTest] Turret '{TurretDef.defName}' spawned successfully in map {TestMap.uniqueID}.");
                            break;

                        case TurretTestPhase.CleaningUp:
                            if (TestMap != null && Find.Maps.Contains(TestMap))
                            {
                                Current.Game.DeinitAndRemoveMap(TestMap, false);
                            }
                            if (Parent != null && !Parent.Destroyed)
                            {
                                Parent.Destroy();
                            }
                            Phase = TurretTestPhase.Done;
                            break;
                            
                        case TurretTestPhase.Done:
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Fail("exception", ex.ToString());
                }
            }

            private void Fail(string status, string message = "")
            {
                Result.Status = status;
                Result.ErrorMessage = message;
                Phase = TurretTestPhase.CleaningUp;
            }
        }
        
        // --- 常量和字段 ---
        private const int MaxConcurrentTurretTests = 5;
        private const int PreOpenSettleRepaintFrames = 10;
        private const int OpenStableFrameRequirement = 3;
        private const int OpenTimeoutRepaintFrames = 180;
        private const int WaitBeforeCloseRepaintFrames = 12;
        private const int CloseTimeoutRepaintFrames = 180;

        private readonly LogSyncSettings settings;
        private readonly Map map; // 主地图，用于UI测试等
        private readonly Queue<AutoTestWorkItem> pendingItems = new Queue<AutoTestWorkItem>();
        private readonly List<AutoTestResult> results = new List<AutoTestResult>();
        private readonly Dictionary<ThingDef, Thing> placedRecipeWorktables = new Dictionary<ThingDef, Thing>();

        // 新增炮塔测试相关字段
        private readonly List<TurretTestSession> activeTurretSessions = new List<TurretTestSession>();
        private readonly Queue<ThingDef> pendingTurretDefs = new Queue<ThingDef>();
        
        private ModContentPack targetMod;
        private Pawn sharedHediffPawn;
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
            
            // 根据 scope 填充队列
            if (settings.ShouldTestTurrets())
            {
                BeginTurretTestSession();
            }
            else
            {
                if (settings.ShouldTestContent())
                {
                    EnqueueThingDefs();
                    EnqueueTerrainDefs();
                }

                if (settings.ShouldTestRecipes())
                {
                    UnlockAllResearch();
                    EnqueueRecipeDefs();
                }

                if (settings.ShouldTestHediffs())
                {
                    EnqueueHediffDefs();
                }
            }

            ApplyBatchLimit();

            int totalPending = pendingItems.Count + pendingTurretDefs.Count;
            Log.Message(string.Format(
                "[LogSyncAutoTest] START packageId={0} map={1} scope={2} pending={3}",
                targetPackageId,
                map.info?.parent?.LabelCap ?? map.ToString(),
                settings.autoTestScope,
                totalPending));

            if (totalPending == 0)
            {
                Log.Warning("[LogSyncAutoTest] No matching auto-test targets were found for " + targetPackageId);
                Finish();
            }
        }

        public void Tick()
        {
            if (completed) return;
            
            // 处理炮塔测试
            if (settings.ShouldTestTurrets())
            {
                // 填充并发池
                while (activeTurretSessions.Count < MaxConcurrentTurretTests && pendingTurretDefs.Count > 0)
                {
                    ThingDef turretDef = pendingTurretDefs.Dequeue();
                    StartNewTurretSession(turretDef);
                }

                // 更新活动的炮塔会话
                foreach (var session in activeTurretSessions)
                {
                    session.Update();
                }

                // 清理已完成的炮塔会话
                activeTurretSessions.RemoveAll(session => {
                    if (session.Phase == TurretTestPhase.Done) {
                        results.Add(session.Result);
                        return true;
                    }
                    return false;
                });
                
                // 结束条件
                if (pendingTurretDefs.Count == 0 && activeTurretSessions.Count == 0)
                {
                    Finish();
                }
            }
            // 处理原有测试
            else
            {
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
        }

        public void ProcessUi()
        {
            if (completed || settings.ShouldTestTurrets()) return; // UI测试与炮塔测试不并发
            
            if (activeSession == null) return;
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
                TryCaptureInfoCardScreenshot(true);
                activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "Trace=" + InfoCardCloseTrace.Describe());
                CleanupSelectionAndWindows();
                InfoCardCloseTrace.Stop();
                activeSession = null;
            }
        }
        
        // --- 炮塔测试新方法 ---
        
        private void BeginTurretTestSession()
        {
            var turretDefs = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(IsTargetTurretDef)
                .OrderBy(d => d.defName);
            
            foreach (var def in turretDefs)
            {
                pendingTurretDefs.Enqueue(def);
            }
        }

        private void StartNewTurretSession(ThingDef turretDef)
        {
            var result = CreateResult("Turret", "ThingDef", turretDef.defName, turretDef.label);
            var session = new TurretTestSession(turretDef, result);
            
            try
            {
                var mapGenerator = DefDatabase<MapGeneratorDef>.GetNamed("LogSync_AutoTestPocketMap");
                var mapSize = new IntVec3(100, 1, 100);
                
                int tile = RimWorld.Planet.TileFinder.RandomStartingTile();
                if (tile < 0)
                {
                    result.Status = "failed_map_creation";
                    result.ErrorMessage = "Could not find a valid tile to create a map parent.";
                    results.Add(result);
                    return;
                }

                session.Parent = (RimWorld.Planet.MapParent)RimWorld.Planet.WorldObjectMaker.MakeWorldObject(RimWorld.WorldObjectDefOf.Ambush);
                session.Parent.Tile = tile;
                Find.WorldObjects.Add(session.Parent);

                session.TestMap = MapGenerator.GenerateMap(mapSize, session.Parent, mapGenerator);
                
                activeTurretSessions.Add(session);
                Log.Message($"[LogSyncAutoTest] Starting new turret session for '{turretDef.defName}' in map {session.TestMap.uniqueID}.");
            }
            catch (Exception ex)
            {
                result.Status = "failed_map_creation";
                result.ErrorMessage = ex.ToString();
                results.Add(result);
                // 如果父对象已创建但地图生成失败，也清理掉
                if (session.Parent != null && !session.Parent.Destroyed)
                {
                    session.Parent.Destroy();
                }
            }
        }

        private bool IsTargetTurretDef(ThingDef def)
        {
            if (def == null || def.modContentPack != targetMod || def.category != ThingCategory.Building || def.thingClass == null)
                return false;

            if (def.building?.turretGunDef != null)
                return true;

            Type type = def.thingClass;
            while(type != null && type != typeof(object))
            {
                if (type.Name == "Building_Turret" || type.Name == "Building_TurretGunCE")
                    return true;
                type = type.BaseType;
            }

            return false;
        }

        // --- 原有方法 (部分有微调) ---

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
                case AutoTestKind.Recipe:
                    StartRecipeSession(item);
                    break;
                case AutoTestKind.Hediff:
                    StartHediffSession(item);
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

        private void StartRecipeSession(AutoTestWorkItem item)
        {
            AutoTestResult result = CreateResult("Recipe", "RecipeDef", item.RecipeDef.defName, item.RecipeDef.label);
            results.Add(result);

            Thing worktableThing = GetPlacedRecipeWorktable(item.WorktableDef);
            if (worktableThing == null)
            {
                result.Status = "skipped";
                result.Stage = "setup";
                result.ErrorMessage = "Required worktable could not be placed or was lost.";
                result.Notes = AppendNote(result.Notes, "Worktable=" + SafeDefName(item.WorktableDef));
                return;
            }

            if (!(worktableThing is IBillGiver))
            {
                result.Status = "skipped";
                result.Stage = "setup";
                result.ErrorMessage = "Placed worktable does not implement IBillGiver.";
                result.Notes = AppendNote(result.Notes, "Worktable=" + SafeDefName(item.WorktableDef));
                return;
            }

            result.Label = item.ThingDef?.label ?? item.RecipeDef.label ?? string.Empty;
            result.Notes = AppendNote(result.Notes, "Product=" + SafeDefName(item.ThingDef));
            result.Notes = AppendNote(result.Notes, "Worktable=" + SafeDefName(item.WorktableDef));

            activeSession = new ActiveSession
            {
                Item = item,
                Result = result,
                InfoCardDef = item.ThingDef ?? (Def)item.RecipeDef,
                RemainingUiFrames = PreOpenSettleRepaintFrames,
                Phase = SessionPhase.WaitingForPreOpenSettle
            };
            result.Stage = "info_open";
        }

        private void StartHediffSession(AutoTestWorkItem item)
        {
            AutoTestResult result = CreateResult("Hediff", "HediffDef", item.HediffDef.defName, item.HediffDef.label);
            results.Add(result);

            try
            {
                Pawn pawn = EnsureSharedHediffPawn();
                if (pawn == null)
                {
                    result.Status = "skipped";
                    result.Stage = "pawn";
                    result.ErrorMessage = "No test pawn could be generated for Hediff validation.";
                    return;
                }

                CleanupTargetModHediffs(pawn);

                BodyPartRecord targetPart = ResolveHediffPart(item.HediffDef, pawn);
                Hediff hediff = HediffMaker.MakeHediff(item.HediffDef, pawn, targetPart);
                if (hediff == null)
                {
                    result.Status = "skipped";
                    result.Stage = "hediff";
                    result.ErrorMessage = "HediffMaker.MakeHediff returned null.";
                    return;
                }

                if (item.HediffDef.initialSeverity > 0f)
                {
                    hediff.Severity = item.HediffDef.initialSeverity;
                }

                pawn.health.AddHediff(hediff, targetPart, null, null);
                Find.Selector.ClearSelection();
                Find.Selector.Select(pawn, false, false);

                result.Label = hediff.LabelCap;
                result.Notes = AppendNote(result.Notes, "Pawn=" + pawn.LabelShortCap);
                result.Notes = AppendNote(result.Notes, "Part=" + (targetPart?.def?.defName ?? "WholeBody"));

                activeSession = new ActiveSession
                {
                    Item = item,
                    Result = result,
                    InfoCardDef = item.HediffDef,
                    TestPawn = pawn,
                    AttachedHediff = hediff,
                    RemainingUiFrames = PreOpenSettleRepaintFrames,
                    Phase = SessionPhase.WaitingForPreOpenSettle
                };
                result.Stage = "info_open";
            }
            catch (Exception ex)
            {
                result.Status = "failed_hediff_apply";
                result.Stage = "hediff";
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
                    TryCaptureInfoCardScreenshot(false);
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
                CompleteActiveSession();
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

            CleanupActiveSessionArtifacts();

            Find.Selector.ClearSelection();
        }

        private void CleanupActiveSessionArtifacts()
        {
            if (activeSession?.AttachedHediff == null || activeSession.TestPawn?.health == null)
            {
                return;
            }

            try
            {
                activeSession.TestPawn.health.RemoveHediff(activeSession.AttachedHediff);
            }
            catch
            {
            }
        }

        private void FailActiveSession(string status, string stage, string errorMessage)
        {
            if (activeSession == null) return;

            activeSession.Result.Status = status;
            activeSession.Result.Stage = stage;
            activeSession.Result.ErrorMessage = errorMessage;
            TryCaptureInfoCardScreenshot(true);
            activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "Trace=" + InfoCardCloseTrace.Describe());
            CleanupSelectionAndWindows();
            InfoCardCloseTrace.Stop();
            activeSession = null;
        }

        private void CompleteActiveSession()
        {
            if (activeSession == null) return;

            if (activeSession.Item.Kind == AutoTestKind.Recipe)
            {
                activeSession.Result.Stage = "produce";
                if (!TryProduceRecipe(activeSession, out string errorMessage))
                {
                    activeSession.Result.Status = "failed_recipe_produce";
                    activeSession.Result.ErrorMessage = errorMessage;
                    activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "Trace=" + InfoCardCloseTrace.Describe());
                    CleanupSelectionAndWindows();
                    InfoCardCloseTrace.Stop();
                    activeSession = null;
                    return;
                }
            }
            else if (activeSession.SpawnedThing != null && !activeSession.SpawnedThing.Destroyed)
            {
                activeSession.Result.Label = activeSession.SpawnedThing.LabelCap;
                activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "InfoCardOpened=True");
            }
            else if (activeSession.TerrainDef != null)
            {
                activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "InfoCardOpened=True");
            }
            else if (activeSession.AttachedHediff != null)
            {
                activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "InfoCardOpened=True");
            }

            activeSession.Result.Status = "passed";
            activeSession.Result.Stage = activeSession.Item.Kind == AutoTestKind.Recipe ? "produce" : "info_close";
            activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "Trace=" + InfoCardCloseTrace.Describe());
            Log.Message("[LogSyncAutoTest] InfoCard close confirmed for " + activeSession.Result.DefName);
            CleanupSelectionAndWindows();
            InfoCardCloseTrace.Stop();
            activeSession = null;
        }

        private void EnqueueThingDefs()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading
                .Where(IsTargetBuildingDef)
                .OrderBy(def => def.defName))
            {
                pendingItems.Enqueue(new AutoTestWorkItem { Kind = AutoTestKind.Building, ThingDef = def });
            }

            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading
                .Where(IsTargetItemDef)
                .OrderBy(def => def.defName))
            {
                pendingItems.Enqueue(new AutoTestWorkItem { Kind = AutoTestKind.Item, ThingDef = def });
            }
        }

        private void EnqueueTerrainDefs()
        {
            foreach (TerrainDef def in DefDatabase<TerrainDef>.AllDefsListForReading
                .Where(IsTargetTerrainDef)
                .OrderBy(def => def.defName))
            {
                pendingItems.Enqueue(new AutoTestWorkItem { Kind = AutoTestKind.Terrain, TerrainDef = def });
            }
        }

        private void EnqueueHediffDefs()
        {
            foreach (HediffDef def in DefDatabase<HediffDef>.AllDefsListForReading
                .Where(IsTargetHediffDef)
                .OrderBy(def => def.defName))
            {
                pendingItems.Enqueue(new AutoTestWorkItem { Kind = AutoTestKind.Hediff, HediffDef = def });
            }
        }

        private void EnqueueRecipeDefs()
        {
            List<AutoTestWorkItem> recipeItems = new List<AutoTestWorkItem>();
            HashSet<ThingDef> requiredWorktables = new HashSet<ThingDef>();

            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading
                .Where(IsTargetRecipeDef)
                .OrderBy(recipe => recipe.defName))
            {
                ThingDef productDef = ResolvePrimaryRecipeProduct(recipe);
                if (productDef == null)
                {
                    AddImmediateResult(
                        CreateResult("Recipe", "RecipeDef", recipe.defName, recipe.label),
                        "skipped",
                        "collect",
                        "No supported product ThingDef was found for this recipe.");
                    continue;
                }

                ThingDef worktableDef = ResolveRecipeWorktable(recipe);
                if (worktableDef == null)
                {
                    AutoTestResult result = CreateResult("Recipe", "RecipeDef", recipe.defName, recipe.label);
                    result.Label = productDef.label ?? recipe.label ?? string.Empty;
                    result.Notes = AppendNote(result.Notes, "Product=" + SafeDefName(productDef));
                    AddImmediateResult(
                        result,
                        "skipped",
                        "collect",
                        "No usable worktable recipe user was found.");
                    continue;
                }

                requiredWorktables.Add(worktableDef);
                recipeItems.Add(new AutoTestWorkItem
                {
                    Kind = AutoTestKind.Recipe,
                    RecipeDef = recipe,
                    ThingDef = productDef,
                    WorktableDef = worktableDef
                });
            }

            PlaceRecipeWorktables(requiredWorktables);

            foreach (AutoTestWorkItem item in recipeItems)
            {
                if (GetPlacedRecipeWorktable(item.WorktableDef) != null)
                {
                    pendingItems.Enqueue(item);
                    continue;
                }

                AutoTestResult result = CreateResult("Recipe", "RecipeDef", item.RecipeDef.defName, item.RecipeDef.label);
                result.Label = item.ThingDef?.label ?? item.RecipeDef.label ?? string.Empty;
                result.Notes = AppendNote(result.Notes, "Product=" + SafeDefName(item.ThingDef));
                result.Notes = AppendNote(result.Notes, "Worktable=" + SafeDefName(item.WorktableDef));
                AddImmediateResult(
                    result,
                    "skipped",
                    "setup",
                    "Required worktable could not be placed.");
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
                   !typeof(Corpse).IsAssignableFrom(def.thingClass) &&
                   !def.IsBlueprint &&
                   !def.IsFrame;
        }

        private bool IsTargetTerrainDef(TerrainDef def)
        {
            return def != null && def.modContentPack == targetMod;
        }

        private bool IsTargetHediffDef(HediffDef def)
        {
            return def != null &&
                   def.modContentPack == targetMod &&
                   def.hediffClass != null;
        }

        private Pawn EnsureSharedHediffPawn()
        {
            if (sharedHediffPawn != null &&
                !sharedHediffPawn.Destroyed &&
                !sharedHediffPawn.Dead &&
                sharedHediffPawn.Map == map)
            {
                return sharedHediffPawn;
            }

            Faction faction = Faction.OfPlayer ?? Find.FactionManager?.OfPlayer;
            Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, faction, null);
            if (pawn == null)
            {
                return null;
            }

            if (!TryAllocateThingRoot(1, 1, out IntVec3 rootCell))
            {
                return null;
            }

            ClearArea(rootCell, 1, 1);
            GenSpawn.Spawn(pawn, rootCell, map, WipeMode.Vanish);
            sharedHediffPawn = pawn;
            return sharedHediffPawn;
        }

        private void CleanupTargetModHediffs(Pawn pawn)
        {
            if (pawn?.health?.hediffSet?.hediffs == null)
            {
                return;
            }

            foreach (Hediff hediff in pawn.health.hediffSet.hediffs
                .Where(hediff => hediff?.def?.modContentPack == targetMod)
                .ToList())
            {
                try
                {
                    pawn.health.RemoveHediff(hediff);
                }
                catch
                {
                }
            }
        }

        private BodyPartRecord ResolveHediffPart(HediffDef def, Pawn pawn)
        {
            if (def == null || pawn?.health?.hediffSet == null)
            {
                return null;
            }

            IEnumerable<BodyPartRecord> parts = pawn.health.hediffSet.GetNotMissingParts(default(BodyPartHeight), default(BodyPartDepth), null, null) ?? Enumerable.Empty<BodyPartRecord>();
            if (def.defaultInstallPart != null)
            {
                BodyPartRecord preferredPart = parts.FirstOrDefault(part => part.def == def.defaultInstallPart);
                if (preferredPart != null)
                {
                    return preferredPart;
                }
            }

            if (def.addedPartProps != null || def.injuryProps != null || def.defaultInstallPart != null)
            {
                return parts.FirstOrDefault();
            }

            return null;
        }

        private bool IsTargetRecipeDef(RecipeDef recipe)
        {
            if (recipe == null) return false;
            if (recipe.products == null || recipe.products.Count == 0) return false;

            bool recipeOwnedByTargetMod = recipe.modContentPack == targetMod;
            bool producesTargetModThing = recipe.products.Any(product =>
                product != null &&
                product.thingDef != null &&
                product.thingDef.modContentPack == targetMod &&
                IsSupportedRecipeProductDef(product.thingDef));

            if (!recipeOwnedByTargetMod && !producesTargetModThing) return false;
            return ResolvePrimaryRecipeProduct(recipe) != null;
        }

        private ThingDef ResolvePrimaryRecipeProduct(RecipeDef recipe)
        {
            if (recipe?.products == null) return null;

            ThingDef targetProduct = recipe.products
                .Where(product => product != null && IsSupportedRecipeProductDef(product.thingDef) && product.thingDef.modContentPack == targetMod)
                .Select(product => product.thingDef)
                .FirstOrDefault();

            if (targetProduct != null) return targetProduct;

            return recipe.products
                .Where(product => product != null && IsSupportedRecipeProductDef(product.thingDef))
                .Select(product => product.thingDef)
                .FirstOrDefault();
        }

        private bool IsSupportedRecipeProductDef(ThingDef def)
        {
            return def != null &&
                   def.thingClass != null &&
                   !typeof(Corpse).IsAssignableFrom(def.thingClass) &&
                   !def.IsBlueprint &&
                   !def.IsFrame;
        }

        private ThingDef ResolveRecipeWorktable(RecipeDef recipe)
        {
            IEnumerable<ThingDef> candidates = recipe.AllRecipeUsers ?? Enumerable.Empty<ThingDef>();
            return candidates
                .Where(def => def != null &&
                              def.category == ThingCategory.Building &&
                              def.thingClass != null &&
                              !def.IsBlueprint &&
                              !def.IsFrame)
                .OrderByDescending(def => def.modContentPack == targetMod ? 1 : 0)
                .ThenBy(def => def.defName)
                .FirstOrDefault();
        }

        private void UnlockAllResearch()
        {
            try
            {
                if (Find.ResearchManager != null)
                {
                    Find.ResearchManager.DebugSetAllProjectsFinished();
                    Log.Message("[LogSyncAutoTest] FinishAllResearch applied.");
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[LogSyncAutoTest] Failed to finish all research: " + ex);
            }
        }

        private void PlaceRecipeWorktables(IEnumerable<ThingDef> worktableDefs)
        {
            foreach (ThingDef worktableDef in worktableDefs
                .Where(def => def != null && !placedRecipeWorktables.ContainsKey(def))
                .OrderByDescending(def => def.modContentPack == targetMod ? 1 : 0)
                .ThenBy(def => def.defName))
            {
                try
                {
                    ThingDef stuff = ResolveStuff(worktableDef);
                    Thing worktableThing = ThingMaker.MakeThing(worktableDef, stuff);
                    if (!(worktableThing is IBillGiver))
                    {
                        Log.Warning("[LogSyncAutoTest] Skipping non-billgiver worktable: " + worktableDef.defName);
                        continue;
                    }

                    IntVec2 size = worktableDef.Size;
                    int width = Math.Max(1, size.x);
                    int height = Math.Max(1, size.z);
                    if (!TryAllocateThingRoot(width, height, out IntVec3 rootCell))
                    {
                        Log.Warning("[LogSyncAutoTest] No layout space remained for worktable: " + worktableDef.defName);
                        continue;
                    }

                    ClearArea(rootCell, width, height);
                    GenSpawn.Spawn(worktableThing, rootCell, map, Rot4.North, WipeMode.Vanish, false, false);
                    placedRecipeWorktables[worktableDef] = worktableThing;

                    Log.Message(string.Format(
                        "[LogSyncAutoTest] Placed worktable {0} at {1}",
                        worktableDef.defName,
                        rootCell));
                }
                catch (Exception ex)
                {
                    Log.Warning("[LogSyncAutoTest] Failed to place worktable " + worktableDef.defName + ": " + ex);
                }
            }
        }

        private Thing GetPlacedRecipeWorktable(ThingDef worktableDef)
        {
            if (worktableDef == null) return null;
            if (!placedRecipeWorktables.TryGetValue(worktableDef, out Thing worktableThing)) return null;

            if (worktableThing == null || worktableThing.Destroyed || worktableThing.Map != map)
            {
                placedRecipeWorktables.Remove(worktableDef);
                return null;
            }

            return worktableThing;
        }

        private bool TryProduceRecipe(ActiveSession session, out string errorMessage)
        {
            errorMessage = null;

            RecipeDef recipe = session.Item.RecipeDef;
            Thing worktableThing = GetPlacedRecipeWorktable(session.Item.WorktableDef);
            if (recipe == null)
            {
                errorMessage = "RecipeDef was missing from the active session.";
                return false;
            }

            if (!(worktableThing is IBillGiver billGiver))
            {
                errorMessage = "Placed worktable is unavailable or no longer implements IBillGiver.";
                return false;
            }

            Pawn worker = map.mapPawns?.FreeColonistsSpawned?.FirstOrDefault(pawn => pawn != null && !pawn.Dead);
            if (worker == null)
            {
                errorMessage = "No spawned colonist was available to evaluate the recipe.";
                return false;
            }

            Bill_Production productionBill = BillUtility.MakeNewBill(recipe, null) as Bill_Production ?? new Bill_Production(recipe, null);
            if (productionBill == null)
            {
                errorMessage = "Failed to create Bill_Production for recipe.";
                return false;
            }

            BillStack billStack = billGiver.BillStack;
            if (billStack == null)
            {
                errorMessage = "Worktable BillStack was null.";
                return false;
            }

            bool billAdded = false;
            List<Thing> ingredients = new List<Thing>();
            List<Thing> spawnedProducts = new List<Thing>();

            try
            {
                billStack.AddBill(productionBill);
                billAdded = true;

                if (!TryCreateRecipeIngredients(recipe, productionBill, worktableThing, out ingredients, out string ingredientSummary, out errorMessage))
                {
                    return false;
                }

                Thing dominantIngredient = ingredients.FirstOrDefault(thing => thing != null && !thing.Destroyed);
                IEnumerable<Thing> producedThings = GenRecipe.MakeRecipeProducts(
                    recipe,
                    worker,
                    ingredients,
                    dominantIngredient,
                    billGiver,
                    null,
                    null,
                    null);

                if (producedThings != null)
                {
                    foreach (Thing producedThing in producedThings)
                    {
                        if (producedThing == null) continue;

                        if (!TryAllocateThingRoot(1, 1, out IntVec3 spawnCell))
                        {
                            spawnCell = ResolveDropCellNear(worktableThing);
                        }

                        GenSpawn.Spawn(producedThing, spawnCell, map, WipeMode.Vanish);
                        spawnedProducts.Add(producedThing);
                    }
                }

                if (spawnedProducts.Count == 0)
                {
                    errorMessage = "Recipe execution returned no products.";
                    return false;
                }

                productionBill.Notify_IterationCompleted(worker, ingredients);
                session.Result.Notes = AppendNote(session.Result.Notes, "InfoCardOpened=True");
                session.Result.Notes = AppendNote(session.Result.Notes, "Ingredients=" + ingredientSummary);
                session.Result.Notes = AppendNote(session.Result.Notes, "Products=" + BuildThingSummary(spawnedProducts));
                session.Result.Notes = AppendNote(session.Result.Notes, "Worker=" + worker.LabelShortCap);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.ToString();
                return false;
            }
            finally
            {
                foreach (Thing ingredient in ingredients)
                {
                    try
                    {
                        if (ingredient != null && !ingredient.Destroyed)
                        {
                            ingredient.Destroy(DestroyMode.Vanish);
                        }
                    }
                    catch
                    {
                    }
                }

                if (billAdded)
                {
                    try
                    {
                        billStack.Delete(productionBill);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private bool TryCreateRecipeIngredients(
            RecipeDef recipe,
            Bill productionBill,
            Thing worktableThing,
            out List<Thing> ingredients,
            out string ingredientSummary,
            out string errorMessage)
        {
            ingredients = new List<Thing>();
            List<string> summaryParts = new List<string>();
            errorMessage = null;

            if (recipe.ingredients == null || recipe.ingredients.Count == 0)
            {
                ingredientSummary = "None";
                return true;
            }

            foreach (IngredientCount ingredientCount in recipe.ingredients)
            {
                if (!TryResolveIngredientDef(ingredientCount, out ThingDef ingredientDef))
                {
                    ingredientSummary = string.Join(",", summaryParts.ToArray());
                    errorMessage = "No valid ingredient ThingDef could be resolved for recipe ingredient.";
                    return false;
                }

                int requiredCount = ResolveRequiredIngredientCount(ingredientCount, ingredientDef, recipe, productionBill);
                int remainingCount = requiredCount;
                int placedCount = 0;

                while (remainingCount > 0)
                {
                    Thing ingredientThing = ThingMaker.MakeThing(ingredientDef, ResolveStuff(ingredientDef));
                    if (ingredientThing == null)
                    {
                        ingredientSummary = string.Join(",", summaryParts.ToArray());
                        errorMessage = "ThingMaker.MakeThing returned null for ingredient " + ingredientDef.defName;
                        return false;
                    }

                    int stackLimit = Math.Max(1, ingredientThing.def.stackLimit);
                    int stackCount = Math.Max(1, Math.Min(remainingCount, stackLimit));
                    ingredientThing.stackCount = stackCount;

                    GenSpawn.Spawn(ingredientThing, ResolveDropCellNear(worktableThing), map, WipeMode.Vanish);
                    ingredients.Add(ingredientThing);

                    placedCount += stackCount;
                    remainingCount -= stackCount;
                }

                summaryParts.Add(ingredientDef.defName + "x" + placedCount);
            }

            ingredientSummary = string.Join(", ", summaryParts.ToArray());
            return true;
        }

        private bool TryResolveIngredientDef(IngredientCount ingredientCount, out ThingDef ingredientDef)
        {
            ingredientDef = null;
            if (ingredientCount == null) return false;

            if (ingredientCount.filter == null) return false;

            ingredientDef = ingredientCount.filter.AllowedThingDefs
                .Where(IsUsableIngredientDef)
                .OrderByDescending(def => def.modContentPack == targetMod ? 1 : 0)
                .ThenBy(def => def.defName)
                .FirstOrDefault();

            return ingredientDef != null;
        }

        private bool IsUsableIngredientDef(ThingDef def)
        {
            return def != null &&
                   def.thingClass != null &&
                   !typeof(Corpse).IsAssignableFrom(def.thingClass) &&
                   !def.IsBlueprint &&
                   !def.IsFrame;
        }

        private int ResolveRequiredIngredientCount(IngredientCount ingredientCount, ThingDef ingredientDef, RecipeDef recipe, Bill bill)
        {
            int requiredCount = 0;

            try
            {
                requiredCount = Mathf.CeilToInt(ingredientCount.CountRequiredOfFor(ingredientDef, recipe, bill));
            }
            catch
            {
            }

            if (requiredCount > 0) return requiredCount;

            try
            {
                requiredCount = Mathf.CeilToInt(ingredientCount.GetBaseCount());
            }
            catch
            {
            }

            return Math.Max(1, requiredCount);
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

        private IntVec3 ResolveDropCellNear(Thing anchorThing)
        {
            if (anchorThing != null && !anchorThing.Destroyed)
            {
                CellRect occupiedRect = anchorThing.OccupiedRect();
                for (int x = occupiedRect.minX - 1; x <= occupiedRect.maxX + 1; x++)
                {
                    for (int z = occupiedRect.minZ - 1; z <= occupiedRect.maxZ + 1; z++)
                    {
                        IntVec3 candidate = new IntVec3(x, 0, z);
                        if (!candidate.InBounds(map)) continue;
                        if (!candidate.Walkable(map)) continue;
                        return candidate;
                    }
                }
            }

            return new IntVec3(10, 0, 10);
        }

        private void CloseExistingInfoCards()
        {
            Find.WindowStack.TryRemoveAssignableFromType(typeof(Dialog_InfoCard), false);
        }

        private void OpenInfoCardOnUiThread()
        {
            if (activeSession == null || activeSession.InfoCardDef == null || activeSession.UiHandle != null) return;

            if (activeSession.AttachedHediff != null)
            {
                activeSession.UiHandle = AutoTestInfoCardUiAdapter.Open(activeSession.AttachedHediff);
            }
            else
            {
                activeSession.UiHandle = activeSession.SpawnedThing != null
                    ? AutoTestInfoCardUiAdapter.Open(activeSession.SpawnedThing)
                    : AutoTestInfoCardUiAdapter.Open(activeSession.InfoCardDef);
            }
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

        private void AddImmediateResult(AutoTestResult result, string status, string stage, string errorMessage)
        {
            result.Status = status;
            result.Stage = stage;
            result.ErrorMessage = errorMessage ?? string.Empty;
            results.Add(result);
        }

        private void Finish()
        {
            if (completed) return;
            completed = true;
            CleanupSelectionAndWindows();
            CleanupSharedHediffPawn();

            AutoTestReportWriter.Write(runArtifacts, targetPackageId ?? "unknown", results, targetMod?.Name ?? "Unknown");
            Log.Message(string.Format(
                "[LogSyncAutoTest] COMPLETED packageId={0} total={1} passed={2} failed={3}",
                targetPackageId ?? "unknown",
                results.Count,
                results.Count(result => result.Status == "passed"),
                results.Count(result => result.Status != "passed" && result.Status != "skipped")));
        }

        private void CleanupSharedHediffPawn()
        {
            if (sharedHediffPawn == null)
            {
                return;
            }

            try
            {
                CleanupTargetModHediffs(sharedHediffPawn);
                if (!sharedHediffPawn.Destroyed)
                {
                    sharedHediffPawn.Destroy(DestroyMode.Vanish);
                }
            }
            catch
            {
            }

            sharedHediffPawn = null;
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
            Queue<object> genericQueue = new Queue<object>();
            if (settings.ShouldTestTurrets())
            {
                foreach(var item in pendingTurretDefs) genericQueue.Enqueue(item);
            }
            else
            {
                foreach(var item in pendingItems) genericQueue.Enqueue(item);
            }

            if (genericQueue.Count <= limit) return;

            var selectedItems = genericQueue.Take(limit).ToArray();
            
            if (settings.ShouldTestTurrets())
            {
                pendingTurretDefs.Clear();
                foreach (var item in selectedItems.Cast<ThingDef>()) pendingTurretDefs.Enqueue(item);
            }
            else
            {
                pendingItems.Clear();
                foreach (var item in selectedItems.Cast<AutoTestWorkItem>()) pendingItems.Enqueue(item);
            }

            Log.Message(string.Format(
                "[LogSyncAutoTest] Batch limit applied. Keeping first {0} items.",
                settings.ShouldTestTurrets() ? pendingTurretDefs.Count : pendingItems.Count));
        }

        private void TryCaptureInfoCardScreenshot(bool force)
        {
            if (activeSession == null || activeSession.ScreenshotCaptured)
            {
                return;
            }

            if (!force && !settings.autoTestCaptureScreenshots)
            {
                return;
            }

            if (runArtifacts == null)
            {
                return;
            }

            if (!IsInfoCardPresent())
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
                activeSession.Result.Notes = AppendNote(
                    activeSession.Result.Notes,
                    force ? "FailureScreenshotCaptured=True" : "ScreenshotCaptured=True");
                activeSession.ScreenshotCaptured = true;

                Log.Message(
                    "[LogSyncAutoTest] Screenshot requested for " +
                    activeSession.Result.DefName +
                    " force=" +
                    force +
                    " -> " +
                    screenshotPath);
            }
            catch (Exception ex)
            {
                activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "ScreenshotError=" + TrimForNote(ex.Message));
                Log.Warning("[LogSyncAutoTest] Screenshot capture failed for " + activeSession.Result.DefName + ": " + ex.Message);
            }
        }

        private string BuildThingSummary(IEnumerable<Thing> things)
        {
            return string.Join(
                ", ",
                things.Where(thing => thing != null)
                    .Select(thing => thing.def.defName + "x" + thing.stackCount)
                    .ToArray());
        }

        private static string SafeDefName(Def def)
        {
            return def?.defName ?? "null";
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
