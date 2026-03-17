using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    // 炮塔测试阶段枚举
    internal enum TurretTestPhase
    {
        Initializing,
        WaitingForMap,   // 新增：等待地图稳定注册
        SpawningPower,
        SpawningTurret,
        Reloading,      // 新增：自动补弹
        SpawningEnemies, // 新增：生成敌人
        Fighting,       // 新增：战斗监控
        WaitingAfterKill, // 新增：杀敌后延迟
        CapturingScreenshot,
        CleaningUp,
        Done
    }

    internal sealed partial class AutoTestRunner
    {
        // --- 常量和字段 ---
        private const int MaxConcurrentTurretTests = 3;
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
        private AutoTestScreenshotService screenshotService;

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
            screenshotService = new AutoTestScreenshotService(settings, runArtifacts, AppendNote, TrimForNote);
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

                // 心跳日志：每 500 帧打印一次状态
                if (Find.TickManager.TicksGame % 500 == 0 && activeTurretSessions.Count > 0)
                {
                    string statusReport = string.Join(", ", activeTurretSessions.Select(s => $"{s.TurretDef.defName}({s.Phase})"));
                    Log.Message($"[LogSyncAutoTest] Heartbeat: {activeTurretSessions.Count} active sessions. Details: {statusReport}");
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

        private bool TryCaptureTurretScreenshot(TurretTestSession session, bool force)
        {
            if (session == null || screenshotService == null)
            {
                return false;
            }

            if (session.TestMap == null || !Find.Maps.Contains(session.TestMap))
            {
                return false;
            }

            return screenshotService.TryCaptureMap(
                session.Result,
                force,
                screenshotService.ResolveTurretResultIndex(results, activeTurretSessions.IndexOf(session)),
                session.Result.Status ?? session.Phase.ToString(),
                () => FocusTurretScreenshotMap(session));
        }

        private bool FocusTurretScreenshotMap(TurretTestSession session)
        {
            if (session?.TestMap == null || !Find.Maps.Contains(session.TestMap))
            {
                return false;
            }

            try
            {
                if (Current.Game != null)
                {
                    Current.Game.CurrentMap = session.TestMap;
                }

                IntVec3 focusCell = session.SpawnedTurret?.Position ?? session.TestMap.Center;
                Find.CameraDriver?.JumpToCurrentMapLoc(focusCell);
                return true;
            }
            catch (Exception ex)
            {
                session.Result.Notes = AppendNote(session.Result.Notes, "TurretScreenshotFocusError=" + TrimForNote(ex.Message));
                Log.Warning("[LogSyncAutoTest] Turret screenshot focus failed for " + session.Result.DefName + ": " + ex.Message);
                return false;
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

    }
}
