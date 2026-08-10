using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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
        WaitingAfterDamage,
        CapturingScreenshot,
        CleaningUp,
        Done
    }

    internal enum RangedWeaponTestPhase
    {
        Initializing,
        WaitingForMap,
        SpawningShooter,
        EquippingWeapon,
        SpawningEnemies,
        Fighting,
        WaitingAfterHit,
        CapturingScreenshot,
        CleaningUp,
        Done
    }

    internal enum MeleeWeaponTestPhase
    {
        Initializing,
        WaitingForMap,
        SpawningShooter,
        EquippingWeapon,
        SpawningEnemies,
        Fighting,
        WaitingAfterHit,
        CapturingScreenshot,
        CleaningUp,
        Done
    }

    internal enum ThreeViewTestPhase
    {
        Initializing,
        WaitingForMap,
        SpawningTargets,
        PausingForCapture,
        CleaningUp,
        Done
    }

    internal sealed class StageScreenshotRequest
    {
        public string StageSuffix;
        public bool Force;
        public bool AllowCapture;
        public Vector3? FocusRootPos;
        public float? FocusRootSize;
        public int FocusDelayTicks;
        public bool PrimeOnly;
        public int RetryAttemptsRemaining;
    }

    internal sealed partial class AutoTestRunner
    {
        // --- 常量和字段 ---
        private const int PreOpenSettleRepaintFrames = 10;
        private const int OpenStableFrameRequirement = 3;
        private const int OpenTimeoutRepaintFrames = 180;
        private const int WaitBeforeCloseRepaintFrames = 12;
        private const int CloseTimeoutRepaintFrames = 180;
        private const int PocketMapSharedCooldownTicks = 120;
        private const int ThreeViewSharedCooldownTicks = 60;
        private const float DefaultTurretScreenshotAspect = 16f / 9f;

        private static readonly FieldInfo ProjectileUsedTargetField = typeof(Projectile).GetField("usedTarget", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ProjectileIntendedTargetField = typeof(Projectile).GetField("intendedTarget", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly LogSyncSettings settings;
        private readonly Map map; // 主地图，用于UI测试等
        private readonly Queue<AutoTestWorkItem> pendingItems = new Queue<AutoTestWorkItem>();
        private readonly List<AutoTestResult> results = new List<AutoTestResult>();
        private readonly List<AutoTestManifestEntry> discoveredManifest = new List<AutoTestManifestEntry>();
        private readonly List<AutoTestManifestEntry> plannedManifest = new List<AutoTestManifestEntry>();
        private readonly HashSet<string> discoveredCaseIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> plannedCaseIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<ThingDef> turretMountedGunDefs = new HashSet<ThingDef>();
        private readonly Dictionary<ThingDef, Thing> placedRecipeWorktables = new Dictionary<ThingDef, Thing>();

        // 新增炮塔测试相关字段
        private readonly List<TurretTestSession> activeTurretSessions = new List<TurretTestSession>();
        private readonly Queue<ThingDef> pendingTurretDefs = new Queue<ThingDef>();
        private readonly List<RangedWeaponTestSession> activeRangedWeaponSessions = new List<RangedWeaponTestSession>();
        private readonly Queue<ThingDef> pendingRangedWeaponDefs = new Queue<ThingDef>();
        private readonly List<MeleeWeaponTestSession> activeMeleeWeaponSessions = new List<MeleeWeaponTestSession>();
        private readonly Queue<ThingDef> pendingMeleeWeaponDefs = new Queue<ThingDef>();
        private readonly List<SkillPocketMapTestSession> activeSkillPocketMapSessions = new List<SkillPocketMapTestSession>();
        private readonly Queue<AbilityDef> pendingSkillAbilityDefs = new Queue<AbilityDef>();
        private readonly List<ThreeViewTestSession> activeThreeViewSessions = new List<ThreeViewTestSession>();
        private readonly Queue<AutoTestThreeViewTargetSpec> pendingThreeViewTargets = new Queue<AutoTestThreeViewTargetSpec>();
        private readonly List<CombatPawnTestSession> activeCombatPawnSessions = new List<CombatPawnTestSession>();
        private readonly Queue<AutoTestCombatPawnTargetSpec> pendingCombatPawnTargets = new Queue<AutoTestCombatPawnTargetSpec>();
        private int pocketMapCooldownUntilTick = -1;
        private int threeViewCooldownUntilTick = -1;
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
        private ThreeViewTestSession activeThreeViewUiSession;
        private AutoTestRunArtifacts runArtifacts;
        private AutoTestRunSummary runSummary;
        private AutoTestScreenshotService screenshotService;
        private string runId;
        private readonly AutoTestPocketMapService pocketMapService;
        private readonly AutoTestThreeViewService threeViewService;
        private readonly AutoTestCombatPawnService combatPawnService;
        private readonly AutoTestReadinessSnapshot initialReadiness;
        private bool? cameraPlusActive;
        private int nextDiscoverOrder = 1;
        private int nextPlannedOrder = 1;
        private bool batchLimitApplied;
        private AutoTestGameClock evidenceClock;
        private AutoTestOperationJournal operationJournal;
        private AutoTestLogJournal logJournal;
        private AutoTestOperationRunner operationRunner;
        private AutoTestEvidenceBuilder evidenceBuilder;
        private AutoTestEvidenceManifest evidenceManifest;
        private AutoTestLogCaptureSubscription logCaptureSubscription;
        private IDisposable runContextScope;
        private volatile bool cancelRequested;

        public AutoTestRunner(LogSyncSettings settings, Map map, AutoTestReadinessSnapshot initialReadiness = null)
        {
            this.settings = settings;
            this.map = map;
            this.initialReadiness = initialReadiness;
            pocketMapService = new AutoTestPocketMapService();
            threeViewService = new AutoTestThreeViewService();
            combatPawnService = new AutoTestCombatPawnService();
        }

        public bool Completed => completed;
        internal string RuntimeRunId => runId;
        internal string RuntimePackageId => targetPackageId;
        internal int RuntimeResultCount => results.Count;
        internal AutoTestEvidenceSnapshotDto GetRuntimeEvidenceSnapshot()
        {
            return new AutoTestEvidenceSnapshotDto
            {
                Found = true,
                RunId = runId,
                Success = evidenceManifest?.Success ?? false,
                OperationCount = evidenceManifest?.Operations?.Count ?? 0,
                AssertionCount = evidenceManifest?.Assertions?.Count ?? 0,
                FailedAssertionCount = evidenceManifest?.Assertions?.Count(item => !item.Success) ?? 0,
                ErrorCount = evidenceManifest?.Errors?.Count ?? 0,
                CaseCount = evidenceManifest?.Cases?.Count ?? 0
            };
        }

        internal void RequestCancel(string requestedRunId)
        {
            if (string.IsNullOrEmpty(requestedRunId) || string.Equals(requestedRunId, runId, StringComparison.OrdinalIgnoreCase)) cancelRequested = true;
        }

        public void Initialize()
        {
            targetPackageId = (settings.autoTestTargetPackageId ?? string.Empty).Trim().ToLowerInvariant();
            runId = (settings.autoTestRunId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(runId)) runId = Guid.NewGuid().ToString("N");
            evidenceClock = new AutoTestGameClock();
            operationJournal = new AutoTestOperationJournal();
            logJournal = new AutoTestLogJournal();
            operationRunner = new AutoTestOperationRunner(runId, evidenceClock, operationJournal);
            runContextScope = AutoTestOperationContext.PushRun(runId);
            evidenceManifest = new AutoTestEvidenceManifest
            {
                RunId = runId,
                PackageId = targetPackageId,
                Suite = string.Join(",", settings.EffectiveAutoTestScopes()),
                StartedAtUtc = evidenceClock.UtcNow,
                Environment = AutoTestRuntimeObservation.Capture(targetPackageId, map)
            };
            evidenceManifest.Readiness = initialReadiness;
            evidenceBuilder = new AutoTestEvidenceBuilder(evidenceManifest, evidenceClock);
            InitializeSemanticInfrastructure();
            if (initialReadiness != null)
            {
                evidenceBuilder.Assert("automation.ready", initialReadiness.AutomationReady, "AutomationReady=true after 3 stable polls", initialReadiness.Summary, initialReadiness.Summary);
            }
            logCaptureSubscription = new AutoTestLogCaptureSubscription(runId, logJournal);
            operationJournal.RecordEvent("run.started", runId, null, null, null, "autotest.run", "Auto-test run started.", evidenceClock.UtcNow);
            outputDirectory = ResolveOutputDirectory(settings);
            runArtifacts = AutoTestReportWriter.CreateRunArtifacts(outputDirectory, targetPackageId, runId);
            screenshotService = new AutoTestScreenshotService(runArtifacts, AppendNote, TrimForNote);
            targetMod = LoadedModManager.RunningMods.FirstOrDefault(mod =>
                string.Equals(mod.PackageIdPlayerFacing, targetPackageId, StringComparison.OrdinalIgnoreCase));

            if (targetMod == null)
            {
                evidenceBuilder.AddError(new AutoTestOperationError { Code = "runtime.not_ready", Message = "Target mod not found: " + targetPackageId });
                Log.Error("[LogSyncAutoTest] Target mod not found: " + targetPackageId);
                Finish();
                return;
            }

            if (initialReadiness != null && !initialReadiness.AutomationReady)
            {
                evidenceBuilder.AddError(new AutoTestOperationError { Code = "runtime.not_ready", Message = "Automation readiness timed out: " + initialReadiness.Summary });
                Log.Error("[LogSyncAutoTest] Automation readiness timed out: " + initialReadiness.Summary);
                Finish();
                return;
            }

            LoadActiveSpecs();
            RebuildTurretMountedGunDefs();

            nextTerrainX = Math.Max(map.Size.x / 2, 10);

            if (settings.ShouldRunStandardSuite())
            {
                Log.Message("[LogSyncAutoTest] Standard suite enabled.");
            }

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

            if (settings.ShouldTestTurrets())
            {
                BeginTurretTestSession();
            }

            if (settings.ShouldTestRangedWeapons())
            {
                BeginRangedWeaponTestSession();
            }

            if (settings.ShouldTestMeleeWeapons())
            {
                BeginMeleeWeaponTestSession();
            }

            if (settings.ShouldTestThreeViews())
            {
                BeginThreeViewTestSession();
            }

            if (settings.ShouldTestSkillPocketMaps())
            {
                BeginSkillPocketMapTestSession();
            }

            if (settings.ShouldTestCombatPawns())
            {
                BeginCombatPawnTestSession();
            }

            ApplyBatchLimit();
            BuildPlannedManifest();

            int totalPending = pendingItems.Count + pendingTurretDefs.Count + pendingRangedWeaponDefs.Count + pendingMeleeWeaponDefs.Count + pendingSkillAbilityDefs.Count + pendingThreeViewTargets.Count + pendingCombatPawnTargets.Count;
            int pocketMapMaxConcurrent = settings.GetPocketMapMaxConcurrent();
            Log.Message(string.Format(
                "[LogSyncAutoTest] START packageId={0} map={1} scope={2} pending={3} pocketMapMaxConcurrent={4}",
                targetPackageId,
                map.info?.parent?.LabelCap ?? map.ToString(),
                string.Join(",", settings.EffectiveAutoTestScopes()),
                totalPending,
                pocketMapMaxConcurrent));
            Log.Message(string.Format(
                "[LogSyncAutoTest] ENABLED scopes: content={0}, recipes={1}, hediffs={2}, turrets={3}, ranged={4}, melee={5}, threeViews={6}, combatPawns={7}",
                settings.ShouldTestContent(),
                settings.ShouldTestRecipes(),
                settings.ShouldTestHediffs(),
                settings.ShouldTestTurrets(),
                settings.ShouldTestRangedWeapons(),
                settings.ShouldTestMeleeWeapons(),
                settings.ShouldTestThreeViews(),
                settings.ShouldTestCombatPawns()));
            Log.Message("[LogSyncAutoTest] TARGET DefNames: " + (settings.autoTestTargetDefNames.Count == 0 ? "<all>" : string.Join(",", settings.autoTestTargetDefNames)));

            if (totalPending == 0)
            {
                Log.Warning("[LogSyncAutoTest] No matching auto-test targets were found for " + targetPackageId);
                Finish();
            }
        }

        private void RebuildTurretMountedGunDefs()
        {
            turretMountedGunDefs.Clear();
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                ThingDef gunDef = def?.building?.turretGunDef;
                if (gunDef != null)
                {
                    turretMountedGunDefs.Add(gunDef);
                }
            }
        }

        private bool IsTurretMountedGunDef(ThingDef def)
        {
            return def != null && turretMountedGunDefs.Contains(def);
        }

        public void Tick()
        {
            if (completed) return;

            if (cancelRequested)
            {
                CancelActiveSessions();
                cancelRequested = false;
            }

            if (activeSession == null && pendingItems.Count > 0)
            {
                StartNextSession();
            }

            if (activeSession != null || pendingItems.Count > 0)
            {
                return;
            }

            if (!IsPocketMapCooldownActive())
            {
                TryStartNextPocketMapSession();
            }

            if (Find.TickManager.TicksGame % 500 == 0 && activeTurretSessions.Count > 0)
            {
                string statusReport = string.Join(", ", activeTurretSessions.Select(s => $"{s.TurretDef.defName}({s.Phase})"));
                Log.Message($"[LogSyncAutoTest] Heartbeat: {activeTurretSessions.Count} active turret sessions. TotalPocketMaps={GetActivePocketMapSessionCount()}/{settings.GetPocketMapMaxConcurrent()}. Details: {statusReport}");
            }

            if (Find.TickManager.TicksGame % 500 == 0 && activeRangedWeaponSessions.Count > 0)
            {
                string statusReport = string.Join(", ", activeRangedWeaponSessions.Select(s => $"{s.WeaponDef.defName}({s.Phase})"));
                Log.Message($"[LogSyncAutoTest] Ranged heartbeat: {activeRangedWeaponSessions.Count} active sessions. TotalPocketMaps={GetActivePocketMapSessionCount()}/{settings.GetPocketMapMaxConcurrent()}. Details: {statusReport}");
            }

            if (Find.TickManager.TicksGame % 500 == 0 && activeMeleeWeaponSessions.Count > 0)
            {
                string statusReport = string.Join(", ", activeMeleeWeaponSessions.Select(s => $"{s.WeaponDef.defName}({s.Phase})"));
                Log.Message($"[LogSyncAutoTest] Melee heartbeat: {activeMeleeWeaponSessions.Count} active sessions. TotalPocketMaps={GetActivePocketMapSessionCount()}/{settings.GetPocketMapMaxConcurrent()}. Details: {statusReport}");
            }

            if (Find.TickManager.TicksGame % 500 == 0 && activeSkillPocketMapSessions.Count > 0)
            {
                string statusReport = string.Join(", ", activeSkillPocketMapSessions.Select(s => $"{s.AbilityDef?.defName ?? "null"}({s.Phase})"));
                Log.Message($"[LogSyncAutoTest] SkillPocketMap heartbeat: {activeSkillPocketMapSessions.Count} active sessions. TotalPocketMaps={GetActivePocketMapSessionCount()}/{settings.GetPocketMapMaxConcurrent()}. Details: {statusReport}");
            }

            if (Find.TickManager.TicksGame % 500 == 0 && activeThreeViewSessions.Count > 0)
            {
                string statusReport = string.Join(", ", activeThreeViewSessions.Select(s => $"{s.Spec.DefName}({s.Phase})"));
                if (activeThreeViewSessions.Count > 1)
                {
                    Log.Warning($"[LogSyncAutoTest] ThreeView heartbeat invariant broken: {activeThreeViewSessions.Count} active sessions. TotalPocketMaps={GetActivePocketMapSessionCount()}/{settings.GetPocketMapMaxConcurrent()}. Details: {statusReport}");
                }
                else
                {
                    Log.Message($"[LogSyncAutoTest] ThreeView heartbeat: {activeThreeViewSessions.Count} active sessions. TotalPocketMaps={GetActivePocketMapSessionCount()}/{settings.GetPocketMapMaxConcurrent()}. Details: {statusReport}");
                }
            }

            if (Find.TickManager.TicksGame % 500 == 0 && activeCombatPawnSessions.Count > 0)
            {
                string statusReport = string.Join(", ", activeCombatPawnSessions.Select(s => $"{s.Spec.DefName}({s.Phase}) {s.BuildDebugStatus()}"));
                Log.Message($"[LogSyncAutoTest] CombatPawn heartbeat: {activeCombatPawnSessions.Count} active sessions. TotalPocketMaps={GetActivePocketMapSessionCount()}/{settings.GetPocketMapMaxConcurrent()}. Details: {statusReport}");
            }

            foreach (var session in activeTurretSessions)
            {
                using (AutoTestOperationContext.PushCase(session.Result?.CaseId)) session.Update();
            }

            foreach (var session in activeRangedWeaponSessions)
            {
                using (AutoTestOperationContext.PushCase(session.Result?.CaseId)) session.Update();
            }

            foreach (var session in activeMeleeWeaponSessions)
            {
                using (AutoTestOperationContext.PushCase(session.Result?.CaseId)) session.Update();
            }

            foreach (var session in activeSkillPocketMapSessions)
            {
                using (AutoTestOperationContext.PushCase(session.Result?.CaseId)) session.Update();
            }

            foreach (var session in activeThreeViewSessions)
            {
                using (AutoTestOperationContext.PushCase(session.Result?.CaseId)) session.Update();
            }

            foreach (var session in activeCombatPawnSessions)
            {
                using (AutoTestOperationContext.PushCase(session.Result?.CaseId)) session.Update();
            }

            activeTurretSessions.RemoveAll(session => {
                if (session.Phase == TurretTestPhase.Done) {
                    RecordResult(session.Result);
                    return true;
                }
                return false;
            });

            activeRangedWeaponSessions.RemoveAll(session => {
                if (session.Phase == RangedWeaponTestPhase.Done) {
                    RecordResult(session.Result);
                    return true;
                }
                return false;
            });

            activeMeleeWeaponSessions.RemoveAll(session => {
                if (session.Phase == MeleeWeaponTestPhase.Done) {
                    RecordResult(session.Result);
                    return true;
                }
                return false;
            });

            activeSkillPocketMapSessions.RemoveAll(session => {
                if (session.Phase == SkillPocketMapTestPhase.Done) {
                    RecordResult(session.Result);
                    return true;
                }
                return false;
            });

            activeThreeViewSessions.RemoveAll(session => {
                if (session.Phase == ThreeViewTestPhase.Done) {
                    RecordResult(session.Result);
                    return true;
                }
                return false;
            });

            activeCombatPawnSessions.RemoveAll(session => {
                if (session.Phase == CombatPawnTestPhase.Done) {
                    RecordResult(session.Result);
                    return true;
                }
                return false;
            });

            if (pendingItems.Count == 0 &&
                activeSession == null &&
                pendingTurretDefs.Count == 0 &&
                pendingRangedWeaponDefs.Count == 0 &&
                pendingMeleeWeaponDefs.Count == 0 &&
                pendingSkillAbilityDefs.Count == 0 &&
                pendingThreeViewTargets.Count == 0 &&
                pendingCombatPawnTargets.Count == 0 &&
                activeTurretSessions.Count == 0 &&
                activeRangedWeaponSessions.Count == 0 &&
                activeMeleeWeaponSessions.Count == 0 &&
                activeSkillPocketMapSessions.Count == 0 &&
                activeThreeViewSessions.Count == 0 &&
                activeCombatPawnSessions.Count == 0)
            {
                Finish();
            }
        }

        private bool TryStartNextPocketMapSession()
        {
            int maxConcurrentPocketMaps = settings.GetPocketMapMaxConcurrent();
            int activePocketMaps = GetActivePocketMapSessionCount();
            if (activePocketMaps >= maxConcurrentPocketMaps)
            {
                return false;
            }

            if (pendingTurretDefs.Count > 0)
            {
                StartNewTurretSession(pendingTurretDefs.Dequeue());
                return true;
            }

            if (pendingRangedWeaponDefs.Count > 0)
            {
                StartNewRangedWeaponSession(pendingRangedWeaponDefs.Dequeue());
                return true;
            }

            if (pendingMeleeWeaponDefs.Count > 0)
            {
                StartNewMeleeWeaponSession(pendingMeleeWeaponDefs.Dequeue());
                return true;
            }

            if (pendingSkillAbilityDefs.Count > 0)
            {
                StartNewSkillPocketMapSession(pendingSkillAbilityDefs.Dequeue());
                return true;
            }

            if (pendingThreeViewTargets.Count > 0)
            {
                if (HasPendingOrActivePreThreeViewPocketMapWork())
                {
                    if (Find.TickManager != null && Find.TickManager.TicksGame % 300 == 0)
                    {
                        Log.Message("[LogSyncAutoTest] ThreeView launch blocked: waiting for pocket map phase to finish. pendingThreeView=" + pendingThreeViewTargets.Count + " activePocketMaps=" + GetActivePocketMapSessionCount());
                    }

                    return false;
                }

                if (activeThreeViewSessions.Count > 0)
                {
                    if (Find.TickManager != null && Find.TickManager.TicksGame % 300 == 0)
                    {
                        Log.Message("[LogSyncAutoTest] ThreeView launch blocked: active session still running. pending=" + pendingThreeViewTargets.Count + " active=" + activeThreeViewSessions.Count);
                    }

                    return false;
                }

                if (IsThreeViewCooldownActive())
                {
                    return false;
                }

                StartNewThreeViewSession(pendingThreeViewTargets.Dequeue());
                return true;
            }

            if (pendingCombatPawnTargets.Count > 0)
            {
                if (HasPendingOrActivePreThreeViewPocketMapWork())
                {
                    if (Find.TickManager != null && Find.TickManager.TicksGame % 300 == 0)
                    {
                        Log.Message("[LogSyncAutoTest] CombatPawn launch blocked: waiting for pocket map phase to finish. pendingCombatPawns=" + pendingCombatPawnTargets.Count + " activePocketMaps=" + GetActivePocketMapSessionCount());
                    }

                    return false;
                }

                if (pendingThreeViewTargets.Count > 0 || activeThreeViewSessions.Count > 0)
                {
                    if (Find.TickManager != null && Find.TickManager.TicksGame % 300 == 0)
                    {
                        Log.Message("[LogSyncAutoTest] CombatPawn launch blocked: waiting for three-view phase to finish. pendingCombatPawns=" + pendingCombatPawnTargets.Count + " pendingThreeView=" + pendingThreeViewTargets.Count + " activeThreeView=" + activeThreeViewSessions.Count);
                    }

                    return false;
                }

                StartNewCombatPawnSession(pendingCombatPawnTargets.Dequeue());
                return true;
            }

            return false;
        }

        private bool IsPocketMapCooldownActive()
        {
            return Find.TickManager != null && Find.TickManager.TicksGame <= pocketMapCooldownUntilTick;
        }

        private bool IsThreeViewCooldownActive()
        {
            return Find.TickManager != null && Find.TickManager.TicksGame <= threeViewCooldownUntilTick;
        }

        private int GetActivePocketMapSessionCount()
        {
            return activeTurretSessions.Count +
                   activeRangedWeaponSessions.Count +
                   activeMeleeWeaponSessions.Count +
                   activeSkillPocketMapSessions.Count +
                   activeCombatPawnSessions.Count;
        }

        private bool HasPendingOrActivePreThreeViewPocketMapWork()
        {
            return pendingTurretDefs.Count > 0 ||
                   pendingRangedWeaponDefs.Count > 0 ||
                   pendingMeleeWeaponDefs.Count > 0 ||
                   pendingSkillAbilityDefs.Count > 0 ||
                   activeTurretSessions.Count > 0 ||
                   activeRangedWeaponSessions.Count > 0 ||
                   activeMeleeWeaponSessions.Count > 0 ||
                   activeSkillPocketMapSessions.Count > 0 ||
                   activeCombatPawnSessions.Count > 0;
        }

        private void RefreshPocketMapCooldown(string reason)
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            int nextTick = currentTick + PocketMapSharedCooldownTicks;
            if (nextTick > pocketMapCooldownUntilTick)
            {
                pocketMapCooldownUntilTick = nextTick;
            }

            Log.Message("[LogSyncAutoTest] Pocket map shared cooldown refreshed: reason=" + reason + " untilTick=" + pocketMapCooldownUntilTick);
        }

        private void RefreshThreeViewCooldown(string reason)
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            int nextTick = currentTick + ThreeViewSharedCooldownTicks;
            if (nextTick > threeViewCooldownUntilTick)
            {
                threeViewCooldownUntilTick = nextTick;
            }

            Log.Message("[LogSyncAutoTest] ThreeView shared cooldown refreshed: reason=" + reason + " untilTick=" + threeViewCooldownUntilTick);
        }

        private void AddImmediateResult(AutoTestResult result, string status, string stage, string errorMessage)
        {
            result.Status = status;
            result.Stage = stage;
            result.ErrorMessage = errorMessage ?? string.Empty;
            RecordResult(result);
        }

        private void Finish()
        {
            if (completed) return;
            completed = true;
            CleanupSelectionAndWindows();
            CleanupSharedHediffPawn();

            runSummary = BuildRunSummary();
            AutoTestEvidenceManifest completedEvidence = null;
            try
            {
                foreach (AutoTestResult result in results.ToList()) CompleteSemanticCase(result);
                LegacyResultEvidenceAdapter.AddResults(evidenceBuilder, results);
                operationJournal?.RecordEvent("run.completed", runId, null, null, null, "autotest.run", "Auto-test run completed.", evidenceClock?.UtcNow ?? DateTime.UtcNow);
                logCaptureSubscription?.Dispose();
                logCaptureSubscription = null;
                completedEvidence = evidenceBuilder?.Complete(operationJournal, logJournal);
                AutoTestReportWriter.Write(runArtifacts, targetPackageId ?? "unknown", runId, results, targetMod?.Name ?? "Unknown", discoveredManifest, plannedManifest, runSummary, completedEvidence);
            }
            finally
            {
                logCaptureSubscription?.Dispose();
                runContextScope?.Dispose();
                runContextScope = null;
                global::LogSyncMod.TestAudioController.Restore();
            }
            Log.Message(string.Format(
                "[LogSyncAutoTest] COMPLETED runId={0} packageId={1} total={2} passed={3} failed={4} discovered={5} planned={6} missing={7} missingDefNames={8}",
                runId,
                targetPackageId ?? "unknown",
                results.Count,
                results.Count(result => result.Status == "passed"),
                results.Count(result => result.Status != "passed" && result.Status != "skipped"),
                runSummary?.DiscoveredCount ?? 0,
                runSummary?.PlannedCount ?? 0,
                runSummary?.MissingCount ?? 0,
                runSummary == null || runSummary.MissingDefNames.Count == 0 ? "<none>" : string.Join(",", runSummary.MissingDefNames)));
        }

        private void CancelActiveSessions()
        {
            pendingItems.Clear();
            pendingTurretDefs.Clear();
            pendingRangedWeaponDefs.Clear();
            pendingMeleeWeaponDefs.Clear();
            pendingSkillAbilityDefs.Clear();
            pendingThreeViewTargets.Clear();
            pendingCombatPawnTargets.Clear();
            if (activeSession != null)
            {
                FailActiveSession("cancelled", "cancel", "Auto-test run cancelled by runtime API.");
            }
            foreach (TurretTestSession session in activeTurretSessions) { session.Result.Status = "cancelled"; session.Result.ErrorMessage = "Auto-test run cancelled."; session.Phase = TurretTestPhase.CleaningUp; }
            foreach (RangedWeaponTestSession session in activeRangedWeaponSessions) { session.Result.Status = "cancelled"; session.Result.ErrorMessage = "Auto-test run cancelled."; session.Phase = RangedWeaponTestPhase.CleaningUp; }
            foreach (MeleeWeaponTestSession session in activeMeleeWeaponSessions) { session.Result.Status = "cancelled"; session.Result.ErrorMessage = "Auto-test run cancelled."; session.Phase = MeleeWeaponTestPhase.CleaningUp; }
            foreach (SkillPocketMapTestSession session in activeSkillPocketMapSessions) { session.Result.Status = "cancelled"; session.Result.ErrorMessage = "Auto-test run cancelled."; session.Phase = SkillPocketMapTestPhase.CleaningUp; }
            foreach (ThreeViewTestSession session in activeThreeViewSessions) { session.Result.Status = "cancelled"; session.Result.ErrorMessage = "Auto-test run cancelled."; session.Phase = ThreeViewTestPhase.CleaningUp; }
            foreach (CombatPawnTestSession session in activeCombatPawnSessions) { session.Result.Status = "cancelled"; session.Result.ErrorMessage = "Auto-test run cancelled."; session.Phase = CombatPawnTestPhase.CleaningUp; }
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
            if (IsFullCoverageRun() || (settings.autoTestTargetDefNames != null && settings.autoTestTargetDefNames.Count > 0))
            {
                Log.Message("[LogSyncAutoTest] Full-coverage or exact-target run detected. Batch limit is ignored.");
                return;
            }

            int limit = Math.Max(1, settings.autoTestBatchSize);
            int originalTotal = pendingItems.Count + pendingTurretDefs.Count + pendingRangedWeaponDefs.Count + pendingMeleeWeaponDefs.Count + pendingSkillAbilityDefs.Count + pendingThreeViewTargets.Count + pendingCombatPawnTargets.Count;
            if (originalTotal <= limit)
            {
                return;
            }

            var limitedItems = new Queue<AutoTestWorkItem>();
            var limitedTurrets = new Queue<ThingDef>();
            var limitedRangedWeapons = new Queue<ThingDef>();
            var limitedMeleeWeapons = new Queue<ThingDef>();
            var limitedSkillAbilities = new Queue<AbilityDef>();
            var limitedThreeViews = new Queue<AutoTestThreeViewTargetSpec>();
            var limitedCombatPawns = new Queue<AutoTestCombatPawnTargetSpec>();
            int keptCount = 0;

            while (keptCount < limit)
            {
                bool itemAddedInRound = false;

                if (pendingItems.Count > 0)
                {
                    limitedItems.Enqueue(pendingItems.Dequeue());
                    keptCount++;
                    itemAddedInRound = true;
                    if (keptCount >= limit) break;
                }

                if (pendingTurretDefs.Count > 0)
                {
                    limitedTurrets.Enqueue(pendingTurretDefs.Dequeue());
                    keptCount++;
                    itemAddedInRound = true;
                    if (keptCount >= limit) break;
                }

                if (pendingRangedWeaponDefs.Count > 0)
                {
                    limitedRangedWeapons.Enqueue(pendingRangedWeaponDefs.Dequeue());
                    keptCount++;
                    itemAddedInRound = true;
                    if (keptCount >= limit) break;
                }

                if (pendingMeleeWeaponDefs.Count > 0)
                {
                    limitedMeleeWeapons.Enqueue(pendingMeleeWeaponDefs.Dequeue());
                    keptCount++;
                    itemAddedInRound = true;
                    if (keptCount >= limit) break;
                }

                if (pendingSkillAbilityDefs.Count > 0)
                {
                    limitedSkillAbilities.Enqueue(pendingSkillAbilityDefs.Dequeue());
                    keptCount++;
                    itemAddedInRound = true;
                    if (keptCount >= limit) break;
                }

                if (pendingThreeViewTargets.Count > 0)
                {
                    limitedThreeViews.Enqueue(pendingThreeViewTargets.Dequeue());
                    keptCount++;
                    itemAddedInRound = true;
                    if (keptCount >= limit) break;
                }

                if (pendingCombatPawnTargets.Count > 0)
                {
                    limitedCombatPawns.Enqueue(pendingCombatPawnTargets.Dequeue());
                    keptCount++;
                    itemAddedInRound = true;
                    if (keptCount >= limit) break;
                }

                if (!itemAddedInRound)
                {
                    break; // All queues are empty
                }
            }

            // Clear the remaining items in the original queues
            pendingItems.Clear();
            pendingTurretDefs.Clear();
            pendingRangedWeaponDefs.Clear();
            pendingMeleeWeaponDefs.Clear();
            pendingSkillAbilityDefs.Clear();
            pendingThreeViewTargets.Clear();
            pendingCombatPawnTargets.Clear();

            // Repopulate with the limited items
            foreach (var item in limitedItems) pendingItems.Enqueue(item);
            foreach (var item in limitedTurrets) pendingTurretDefs.Enqueue(item);
            foreach (var item in limitedRangedWeapons) pendingRangedWeaponDefs.Enqueue(item);
            foreach (var item in limitedMeleeWeapons) pendingMeleeWeaponDefs.Enqueue(item);
            foreach (var item in limitedSkillAbilities) pendingSkillAbilityDefs.Enqueue(item);
            foreach (var item in limitedThreeViews) pendingThreeViewTargets.Enqueue(item);
            foreach (var item in limitedCombatPawns) pendingCombatPawnTargets.Enqueue(item);

            batchLimitApplied = true;

            Log.Message(string.Format(
                "[LogSyncAutoTest] Batch limit applied. Original={0}, Kept={1}",
                originalTotal,
                keptCount));
        }

        private AutoTestCaseType ResolveCaseType(AutoTestKind kind)
        {
            switch (kind)
            {
                case AutoTestKind.Building:
                    return AutoTestCaseType.BuildingInfoCard;
                case AutoTestKind.Item:
                    return AutoTestCaseType.ItemInfoCard;
                case AutoTestKind.Terrain:
                    return AutoTestCaseType.TerrainInfoCard;
                case AutoTestKind.Recipe:
                    return AutoTestCaseType.RecipeBillUi;
                case AutoTestKind.Hediff:
                    return AutoTestCaseType.HediffInfoCard;
                case AutoTestKind.TurretPocketMap:
                    return AutoTestCaseType.TurretPocketMap;
                case AutoTestKind.RangedWeaponPocketMap:
                    return AutoTestCaseType.RangedWeaponPocketMap;
                case AutoTestKind.MeleeWeaponPocketMap:
                    return AutoTestCaseType.MeleeWeaponPocketMap;
                case AutoTestKind.SkillPocketMap:
                    return AutoTestCaseType.SkillPocketMap;
                case AutoTestKind.HumanoidThreeView:
                    return AutoTestCaseType.HumanoidThreeView;
                case AutoTestKind.AnimalThreeView:
                    return AutoTestCaseType.AnimalThreeView;
                case AutoTestKind.MechanoidThreeView:
                    return AutoTestCaseType.MechanoidThreeView;
                case AutoTestKind.AlienThreeView:
                    return AutoTestCaseType.AlienThreeView;
                case AutoTestKind.ApparelThreeView:
                    return AutoTestCaseType.ApparelThreeView;
                case AutoTestKind.WeaponThreeView:
                    return AutoTestCaseType.WeaponThreeView;
                case AutoTestKind.ApparelCombat:
                    return AutoTestCaseType.ApparelCombat;
                case AutoTestKind.AnimalCombat:
                    return AutoTestCaseType.AnimalCombat;
                case AutoTestKind.MechanoidCombat:
                    return AutoTestCaseType.MechanoidCombat;
                case AutoTestKind.AlienCombat:
                    return AutoTestCaseType.AlienCombat;
                default:
                    return AutoTestCaseType.ItemInfoCard;
            }
        }

        private string ResolveCategory(AutoTestCaseType caseType)
        {
            switch (caseType)
            {
                case AutoTestCaseType.BuildingInfoCard:
                    return "Building";
                case AutoTestCaseType.ItemInfoCard:
                    return "Item";
                case AutoTestCaseType.TerrainInfoCard:
                    return "Terrain";
                case AutoTestCaseType.RecipeInfoCard:
                case AutoTestCaseType.RecipeBillUi:
                    return "Recipe";
                case AutoTestCaseType.HediffInfoCard:
                    return "Hediff";
                case AutoTestCaseType.TurretPocketMap:
                    return "Turret";
                case AutoTestCaseType.RangedWeaponPocketMap:
                    return "RangedWeapon";
                case AutoTestCaseType.MeleeWeaponPocketMap:
                    return "MeleeWeapon";
                case AutoTestCaseType.SkillPocketMap:
                    return "SkillPocketMap";
                case AutoTestCaseType.HumanoidThreeView:
                    return "HumanoidThreeView";
                case AutoTestCaseType.AnimalThreeView:
                    return "AnimalThreeView";
                case AutoTestCaseType.MechanoidThreeView:
                    return "MechanoidThreeView";
                case AutoTestCaseType.AlienThreeView:
                    return "AlienThreeView";
                case AutoTestCaseType.ApparelThreeView:
                    return "ApparelThreeView";
                case AutoTestCaseType.WeaponThreeView:
                    return "WeaponThreeView";
                case AutoTestCaseType.ApparelCombat:
                    return "ApparelCombat";
                case AutoTestCaseType.AnimalCombat:
                    return "AnimalCombat";
                case AutoTestCaseType.MechanoidCombat:
                    return "MechanoidCombat";
                case AutoTestCaseType.AlienCombat:
                    return "AlienCombat";
                default:
                    return caseType.ToString();
            }
        }

        private AutoTestScope ResolveScope(AutoTestCaseType caseType)
        {
            switch (caseType)
            {
                case AutoTestCaseType.RecipeInfoCard:
                case AutoTestCaseType.RecipeBillUi:
                    return AutoTestScope.Recipes;
                case AutoTestCaseType.HediffInfoCard:
                    return AutoTestScope.Hediffs;
                case AutoTestCaseType.TurretPocketMap:
                    return AutoTestScope.Turrets;
                case AutoTestCaseType.RangedWeaponPocketMap:
                    return AutoTestScope.RangedWeapons;
                case AutoTestCaseType.MeleeWeaponPocketMap:
                    return AutoTestScope.MeleeWeapons;
                case AutoTestCaseType.SkillPocketMap:
                    return AutoTestScope.SkillPocketMaps;
                case AutoTestCaseType.HumanoidThreeView:
                case AutoTestCaseType.AnimalThreeView:
                case AutoTestCaseType.MechanoidThreeView:
                case AutoTestCaseType.AlienThreeView:
                case AutoTestCaseType.ApparelThreeView:
                case AutoTestCaseType.WeaponThreeView:
                    return AutoTestScope.ThreeViews;
                case AutoTestCaseType.ApparelCombat:
                case AutoTestCaseType.AnimalCombat:
                case AutoTestCaseType.MechanoidCombat:
                case AutoTestCaseType.AlienCombat:
                    return AutoTestScope.CombatPawns;
                default:
                    return AutoTestScope.Content;
            }
        }

        private string BuildCaseId(AutoTestCaseType caseType, string defName)
        {
            return (targetPackageId ?? "unknown") + "|" + caseType + "|" + (defName ?? string.Empty);
        }

        private AutoTestManifestEntry RegisterDiscoveredCase(AutoTestCaseType caseType, string defType, string defName, string label)
        {
            string caseId = BuildCaseId(caseType, defName);
            if (!discoveredCaseIds.Add(caseId))
            {
                return discoveredManifest.First(manifestEntry => string.Equals(manifestEntry.CaseId, caseId, StringComparison.OrdinalIgnoreCase));
            }

            AutoTestManifestEntry entry = new AutoTestManifestEntry
            {
                CaseId = caseId,
                CaseType = caseType.ToString(),
                Category = ResolveCategory(caseType),
                Scope = ResolveScope(caseType).ToString(),
                DefType = defType ?? string.Empty,
                DefName = defName ?? string.Empty,
                Label = label ?? string.Empty,
                DiscoverOrder = nextDiscoverOrder++
            };

            discoveredManifest.Add(entry);
            return entry;
        }

        private AutoTestWorkItem CreateWorkItem(
            AutoTestKind kind,
            ThingDef thingDef = null,
            TerrainDef terrainDef = null,
            RecipeDef recipeDef = null,
            ThingDef worktableDef = null,
            HediffDef hediffDef = null,
            LogSyncAutoTestSpecDef specDef = null)
        {
            AutoTestCaseType caseType = ResolveCaseType(kind);
            // Recipe cases are keyed by the RecipeDef even though the produced ThingDef
            // is also carried on the work item for setup and result labeling.
            Def targetDef = (Def)recipeDef ?? (Def)thingDef ?? (Def)terrainDef ?? hediffDef;
            string defType = targetDef?.GetType().Name ?? string.Empty;
            AutoTestManifestEntry entry = RegisterDiscoveredCase(caseType, defType, targetDef?.defName, targetDef?.label);

            return new AutoTestWorkItem
            {
                Kind = kind,
                CaseType = caseType,
                CaseId = entry.CaseId,
                ThingDef = thingDef,
                TerrainDef = terrainDef,
                RecipeDef = recipeDef,
                WorktableDef = worktableDef,
                HediffDef = hediffDef,
                SpecDef = specDef,
                ManifestEntry = entry
            };
        }

        private AutoTestResult CreateResult(AutoTestManifestEntry entry, string defType, string defName, string label)
        {
            AutoTestCaseType caseType;
            if (!Enum.TryParse(entry?.CaseType ?? string.Empty, out caseType))
            {
                caseType = AutoTestCaseType.ItemInfoCard;
            }

            AutoTestResult result = new AutoTestResult
            {
                PackageId = targetPackageId,
                CaseId = entry?.CaseId ?? BuildCaseId(caseType, defName),
                CaseType = caseType.ToString(),
                Scope = entry?.Scope ?? ResolveScope(caseType).ToString(),
                Category = entry?.Category ?? ResolveCategory(caseType),
                DefType = defType ?? string.Empty,
                DefName = defName ?? string.Empty,
                Label = label ?? string.Empty,
                Status = "pending",
                Stage = "collect",
                ErrorMessage = string.Empty,
                ScreenshotPath = string.Empty,
                Notes = string.Empty
            };
            RegisterSemanticCase(result);
            return result;
        }

        private AutoTestResult CreateResult(AutoTestCaseType caseType, string defType, string defName, string label)
        {
            AutoTestManifestEntry entry = RegisterDiscoveredCase(caseType, defType, defName, label);
            return CreateResult(entry, defType, defName, label);
        }

        private bool IsFullCoverageRun()
        {
            return !settings.UseXmlDrivenAutoTest() && settings.ShouldRunStandardSuite() && settings.EffectiveAutoTestScopes().Count == 1;
        }

        private AutoTestPocketMapHandle CreatePocketMapHandle(string mapGeneratorDefName, IntVec3 mapSize, Action<Map> onGenerated, out string errorMessage)
        {
            AutoTestPocketMapHandle handle = pocketMapService.Create(new AutoTestPocketMapRequest
            {
                SourceMap = map,
                MapGeneratorDefName = mapGeneratorDefName,
                MapSize = mapSize,
                OnGenerated = onGenerated
            }, out errorMessage);

            if (handle != null)
            {
                RefreshPocketMapCooldown("create");
            }

            return handle;
        }

        private void DestroyPocketMap(Map pocketMap, MapParent parent)
        {
            pocketMapService.Destroy(pocketMap, parent);
            RefreshPocketMapCooldown("destroy");
        }

        private string ResolveRunMode()
        {
            return IsFullCoverageRun() ? "Standard" : "Targeted";
        }

        private void BuildPlannedManifest()
        {
            plannedManifest.Clear();
            plannedCaseIds.Clear();
            nextPlannedOrder = 1;

            foreach (AutoTestWorkItem item in pendingItems)
            {
                AddPlannedManifestEntry(item.ManifestEntry);
            }

            foreach (ThingDef def in pendingTurretDefs)
            {
                AddPlannedManifestEntry(RegisterDiscoveredCase(AutoTestCaseType.TurretPocketMap, nameof(ThingDef), def?.defName, def?.label));
            }

            foreach (ThingDef def in pendingRangedWeaponDefs)
            {
                AddPlannedManifestEntry(RegisterDiscoveredCase(AutoTestCaseType.RangedWeaponPocketMap, nameof(ThingDef), def?.defName, def?.label));
            }

            foreach (ThingDef def in pendingMeleeWeaponDefs)
            {
                AddPlannedManifestEntry(RegisterDiscoveredCase(AutoTestCaseType.MeleeWeaponPocketMap, nameof(ThingDef), def?.defName, def?.label));
            }

            foreach (AbilityDef def in pendingSkillAbilityDefs)
            {
                AddPlannedManifestEntry(RegisterDiscoveredCase(AutoTestCaseType.SkillPocketMap, nameof(AbilityDef), def?.defName, def?.label));
            }

            foreach (AutoTestThreeViewTargetSpec spec in pendingThreeViewTargets)
            {
                AddPlannedManifestEntry(spec?.ManifestEntry);
            }

            foreach (AutoTestCombatPawnTargetSpec spec in pendingCombatPawnTargets)
            {
                AddPlannedManifestEntry(spec?.ManifestEntry);
            }

            foreach (AutoTestResult result in results)
            {
                AddPlannedManifestEntry(RegisterDiscoveredCase(
                    ParseCaseType(result.CaseType),
                    result.DefType,
                    result.DefName,
                    result.Label));
            }
        }

        private void AddPlannedManifestEntry(AutoTestManifestEntry entry)
        {
            if (entry == null || !plannedCaseIds.Add(entry.CaseId))
            {
                return;
            }

            entry.Planned = true;
            entry.PlannedOrder = nextPlannedOrder++;
            plannedManifest.Add(entry);
        }

        private AutoTestCaseType ParseCaseType(string text)
        {
            AutoTestCaseType caseType;
            return Enum.TryParse(text ?? string.Empty, out caseType)
                ? caseType
                : AutoTestCaseType.ItemInfoCard;
        }

        private AutoTestRunSummary BuildRunSummary()
        {
            AutoTestRunSummary summary = new AutoTestRunSummary();
            summary.RunMode = ResolveRunMode();
            summary.DiscoveredCount = discoveredManifest.Count;
            summary.PlannedCount = plannedManifest.Count;
            summary.ExecutedCount = results.Count;
            summary.PassedCount = results.Count(result => result.Status == "passed");
            summary.SkippedCount = results.Count(result => result.Status == "skipped");
            summary.FailedCount = results.Count(result => result.Status != "passed" && result.Status != "skipped");

            HashSet<string> executedCaseIds = new HashSet<string>(
                results
                    .Where(result => !string.IsNullOrWhiteSpace(result.CaseId))
                    .Select(result => result.CaseId),
                StringComparer.OrdinalIgnoreCase);

            foreach (AutoTestManifestEntry entry in plannedManifest)
            {
                if (!executedCaseIds.Contains(entry.CaseId))
                {
                    summary.MissingCaseIds.Add(entry.CaseId);
                }
            }

            summary.MissingCount = summary.MissingCaseIds.Count;
            summary.BatchSize = Math.Max(1, settings.autoTestBatchSize);
            summary.BatchLimitApplied = batchLimitApplied;
            if (settings.autoTestTargetDefNames != null && settings.autoTestTargetDefNames.Count > 0)
            {
                foreach (string requested in settings.autoTestTargetDefNames)
                {
                    bool matched = plannedManifest.Any(entry =>
                        string.Equals(entry.DefName, requested, StringComparison.OrdinalIgnoreCase) ||
                        entry.DefName.StartsWith(requested + "@", StringComparison.OrdinalIgnoreCase));
                    if (!matched) summary.MissingDefNames.Add(requested);
                }
                foreach (string missing in summary.MissingDefNames)
                {
                    summary.MissingCaseIds.Add("DefName:" + missing);
                }
                summary.MissingCount = summary.MissingCaseIds.Count;
            }
            summary.Complete = summary.MissingCount == 0 && summary.ExecutedCount >= summary.PlannedCount;
            return summary;
        }

        private bool TryCaptureTurretScreenshot(TurretTestSession session, bool force)
        {
            if (session == null)
            {
                return false;
            }

            if (force)
            {
                return TryCaptureTurretFailureScreenshot(session);
            }

            return TryCaptureTurretStageScreenshot(session, session.Result?.Status ?? session.Phase.ToString(), force: false, allowCapture: session.AllowScreenshots);
        }

        private bool TryCaptureTurretStageScreenshot(TurretTestSession session, string stageSuffix, bool force, bool allowCapture)
        {
            if (session == null || screenshotService == null)
            {
                return false;
            }

            if (session.TestMap == null || !Find.Maps.Contains(session.TestMap))
            {
                return false;
            }

            return screenshotService.TryCapture(new AutoTestScreenshotRequest
            {
                Result = session.Result,
                AllowCapture = allowCapture,
                Force = force,
                ResultIndex = screenshotService.ResolveTurretResultIndex(results, activeTurretSessions.IndexOf(session)),
                StageSuffix = stageSuffix,
                Prepare = () => FocusTurretScreenshotMap(session),
                Guard = () => session.TestMap != null && Find.Maps.Contains(session.TestMap),
                CaptureProbe = () => ProbeTurretCapture(session, stageSuffix),
                RequireCaptureProbePass = IsProjectileStage(stageSuffix),
                Profile = AutoTestScreenshotProfiles.TurretPrimary,
                SuccessNoteKey = "TurretScreenshotCaptured=True",
                FailureNoteKey = "TurretFailureScreenshotCaptured=True"
            });
        }

        private bool TryCaptureTurretFailureScreenshot(TurretTestSession session)
        {
            if (session == null || screenshotService == null || session.TestMap == null || !Find.Maps.Contains(session.TestMap))
            {
                return false;
            }

            return screenshotService.TryCapture(new AutoTestScreenshotRequest
            {
                Result = session.Result,
                AllowCapture = true,
                Force = true,
                IsFailureArtifact = true,
                ResultIndex = screenshotService.ResolveTurretResultIndex(results, activeTurretSessions.IndexOf(session)),
                StageSuffix = session.Result?.Status ?? "failed",
                Prepare = () => FocusTurretScreenshotMap(session),
                Guard = () => session.TestMap != null && Find.Maps.Contains(session.TestMap),
                Profile = AutoTestScreenshotProfiles.TurretPrimary,
                SuccessNoteKey = "TurretScreenshotCaptured=True",
                FailureNoteKey = "TurretFailureScreenshotCaptured=True"
            });
        }

        private bool TryCaptureRangedWeaponScreenshot(RangedWeaponTestSession session, bool force)
        {
            if (session == null) return false;
            if (force) return TryCaptureRangedWeaponFailureScreenshot(session);
            return TryCaptureRangedWeaponStageScreenshot(session, session.Result?.Status ?? session.Phase.ToString(), force: false, allowCapture: session.AllowScreenshots);
        }

        private bool TryCaptureRangedWeaponStageScreenshot(RangedWeaponTestSession session, string stageSuffix, bool force, bool allowCapture)
        {
            if (session == null || screenshotService == null || session.TestMap == null || !Find.Maps.Contains(session.TestMap)) return false;
            return screenshotService.TryCapture(new AutoTestScreenshotRequest
            {
                Result = session.Result,
                AllowCapture = allowCapture,
                Force = force,
                ResultIndex = screenshotService.ResolveTurretResultIndex(results, activeRangedWeaponSessions.IndexOf(session)),
                StageSuffix = stageSuffix,
                Prepare = () => FocusRangedWeaponScreenshotMap(session),
                Guard = () => session.TestMap != null && Find.Maps.Contains(session.TestMap),
                CaptureProbe = () => ProbeRangedWeaponCapture(session, stageSuffix),
                RequireCaptureProbePass = IsProjectileStage(stageSuffix),
                Profile = AutoTestScreenshotProfiles.RangedPrimary,
                SuccessNoteKey = "TurretScreenshotCaptured=True",
                FailureNoteKey = "TurretFailureScreenshotCaptured=True"
            });
        }

        private bool TryCaptureRangedWeaponFailureScreenshot(RangedWeaponTestSession session)
        {
            if (session == null || screenshotService == null || session.TestMap == null || !Find.Maps.Contains(session.TestMap)) return false;
            return screenshotService.TryCapture(new AutoTestScreenshotRequest
            {
                Result = session.Result,
                AllowCapture = true,
                Force = true,
                IsFailureArtifact = true,
                ResultIndex = screenshotService.ResolveTurretResultIndex(results, activeRangedWeaponSessions.IndexOf(session)),
                StageSuffix = session.Result?.Status ?? "failed",
                Prepare = () => FocusPocketMapOverview(session.TestMap),
                Guard = () => session.TestMap != null && Find.Maps.Contains(session.TestMap),
                Profile = AutoTestScreenshotProfiles.PocketMapOverview,
                SuccessNoteKey = "TurretScreenshotCaptured=True",
                FailureNoteKey = "TurretFailureScreenshotCaptured=True"
            });
        }

        private bool TryCaptureMeleeWeaponScreenshot(MeleeWeaponTestSession session, bool force)
        {
            if (session == null) return false;
            if (force) return TryCaptureMeleeWeaponFailureScreenshot(session);
            return TryCaptureMeleeWeaponStageScreenshot(session, session.Result?.Status ?? session.Phase.ToString(), force: false, allowCapture: session.AllowScreenshots);
        }

        private bool TryCaptureMeleeWeaponStageScreenshot(MeleeWeaponTestSession session, string stageSuffix, bool force, bool allowCapture)
        {
            if (session == null || screenshotService == null || session.TestMap == null || !Find.Maps.Contains(session.TestMap)) return false;
            return screenshotService.TryCapture(new AutoTestScreenshotRequest
            {
                Result = session.Result,
                AllowCapture = allowCapture,
                Force = force,
                ResultIndex = screenshotService.ResolveTurretResultIndex(results, activeMeleeWeaponSessions.IndexOf(session)),
                StageSuffix = stageSuffix,
                Prepare = () => FocusMeleeWeaponScreenshotMap(session),
                Guard = () => session.TestMap != null && Find.Maps.Contains(session.TestMap),
                Profile = AutoTestScreenshotProfiles.MeleePrimary,
                SuccessNoteKey = "TurretScreenshotCaptured=True",
                FailureNoteKey = "TurretFailureScreenshotCaptured=True"
            });
        }

        private bool TryCaptureMeleeWeaponFailureScreenshot(MeleeWeaponTestSession session)
        {
            if (session == null || screenshotService == null || session.TestMap == null || !Find.Maps.Contains(session.TestMap)) return false;
            return screenshotService.TryCapture(new AutoTestScreenshotRequest
            {
                Result = session.Result,
                AllowCapture = true,
                Force = true,
                IsFailureArtifact = true,
                ResultIndex = screenshotService.ResolveTurretResultIndex(results, activeMeleeWeaponSessions.IndexOf(session)),
                StageSuffix = session.Result?.Status ?? "failed",
                Prepare = () => FocusPocketMapOverview(session.TestMap),
                Guard = () => session.TestMap != null && Find.Maps.Contains(session.TestMap),
                Profile = AutoTestScreenshotProfiles.PocketMapOverview,
                SuccessNoteKey = "TurretScreenshotCaptured=True",
                FailureNoteKey = "TurretFailureScreenshotCaptured=True"
            });
        }

        private bool TryCaptureMeleeActionScreenshot(MeleeWeaponTestSession session)
        {
            if (session == null || screenshotService == null) return false;

            return screenshotService.TryCapture(new AutoTestScreenshotRequest
            {
                Result = session.Result,
                AllowCapture = true,
                Force = true,
                ResultIndex = screenshotService.ResolveTurretResultIndex(results, activeMeleeWeaponSessions.IndexOf(session)),
                StageSuffix = "action_t15",
                Prepare = () => FocusMeleeAction(session),
                Guard = () => session.TestMap != null && Find.Maps.Contains(session.TestMap),
                Profile = AutoTestScreenshotProfiles.MeleeAction,
                SuccessNoteKey = "TurretScreenshotCaptured=True",
                FailureNoteKey = "TurretFailureScreenshotCaptured=True"
            });
        }

        private bool FocusTurretScreenshotMap(TurretTestSession session)
        {
            if (session?.TestMap == null || !Find.Maps.Contains(session.TestMap)) return false;

            try
            {
                if (Current.Game != null) Current.Game.CurrentMap = session.TestMap;
                CameraDriver cameraDriver = Find.CameraDriver;
                if (cameraDriver == null) return false;

                float aspect = ResolveTurretScreenshotAspect(cameraDriver);
                if (TryResolveTurretScreenshotView(session, aspect, out Vector3 targetPos, out float rootSize))
                {
                    // 显式重置 Y 轴高度并使用 SetRootPosAndSize，确保相机位于地图上方并重新计算正确的 Y 轴。
                    cameraDriver.SetRootPosAndSize(new Vector3(targetPos.x, AutoTestScreenshotProfiles.TurretPrimary.CameraHeight, targetPos.z), rootSize);
                }
                else
                {
                    Vector3 center = ToMapWorldCenter(session.SpawnedTurret?.Position ?? session.TestMap.Center);
                    cameraDriver.SetRootPosAndSize(new Vector3(center.x, AutoTestScreenshotProfiles.TurretPrimary.CameraHeight, center.z), AutoTestScreenshotProfiles.TurretPrimary.MinRootSize);
                }

                return true;
            }
            catch (Exception ex)
            {
                session.Result.Notes = AppendNote(session.Result.Notes, "TurretScreenshotFocusError=" + TrimForNote(ex.Message));
                Log.Warning("[LogSyncAutoTest] Turret screenshot focus failed for " + session.Result.DefName + ": " + ex.Message);
                return false;
            }
        }

        private bool FocusRangedWeaponScreenshotMap(RangedWeaponTestSession session)
        {
            if (session?.TestMap == null || !Find.Maps.Contains(session.TestMap)) return false;

            try
            {
                if (Current.Game != null) Current.Game.CurrentMap = session.TestMap;

                CameraDriver cameraDriver = Find.CameraDriver;
                if (cameraDriver == null) return false;

                float aspect = ResolveTurretScreenshotAspect(cameraDriver);
                if (TryResolveRangedWeaponScreenshotView(session, aspect, out Vector3 targetPos, out float rootSize))
                {
                    cameraDriver.SetRootPosAndSize(new Vector3(targetPos.x, AutoTestScreenshotProfiles.RangedPrimary.CameraHeight, targetPos.z), rootSize);
                }
                else
                {
                    Vector3 center = ToMapWorldCenter(session.ShooterPawn?.Position ?? session.TestMap.Center);
                    cameraDriver.SetRootPosAndSize(new Vector3(center.x, AutoTestScreenshotProfiles.RangedPrimary.CameraHeight, center.z), AutoTestScreenshotProfiles.RangedPrimary.MinRootSize);
                }

                return true;
            }
            catch (Exception ex)
            {
                session.Result.Notes = AppendNote(session.Result.Notes, "RangedScreenshotFocusError=" + TrimForNote(ex.Message));
                Log.Warning("[LogSyncAutoTest] Ranged screenshot focus failed for " + session.Result.DefName + ": " + ex.Message);
                return false;
            }
        }

        private bool FocusMeleeWeaponScreenshotMap(MeleeWeaponTestSession session)
        {
            if (session?.TestMap == null || !Find.Maps.Contains(session.TestMap)) return false;

            try
            {
                if (Current.Game != null) Current.Game.CurrentMap = session.TestMap;

                CameraDriver cameraDriver = Find.CameraDriver;
                if (cameraDriver == null) return false;

                if (TryResolveMeleeWeaponScreenshotView(session, out Vector3 targetPos, out float rootSize))
                {
                    cameraDriver.SetRootPosAndSize(new Vector3(targetPos.x, AutoTestScreenshotProfiles.MeleePrimary.CameraHeight, targetPos.z), rootSize);
                }
                else
                {
                    Vector3 center = ToMapWorldCenter(session.ShooterPawn?.Position ?? session.TestMap.Center);
                    cameraDriver.SetRootPosAndSize(new Vector3(center.x, AutoTestScreenshotProfiles.MeleePrimary.CameraHeight, center.z), AutoTestScreenshotProfiles.MeleePrimary.FixedRootSize ?? 11f);
                }

                return true;
            }
            catch (Exception ex)
            {
                session.Result.Notes = AppendNote(session.Result.Notes, "MeleeScreenshotFocusError=" + TrimForNote(ex.Message));
                Log.Warning("[LogSyncAutoTest] Melee screenshot focus failed for " + session.Result.DefName + ": " + ex.Message);
                return false;
            }
        }

        private void FocusMeleeAction(MeleeWeaponTestSession session)
        {
            if (session?.ShooterPawn == null || session.TestMap == null) return;

            try
            {
                if (Current.Game != null) Current.Game.CurrentMap = session.TestMap;

                CameraDriver cameraDriver = Find.CameraDriver;
                if (cameraDriver == null) return;

                Vector3 center = session.ShooterPawn.DrawPos;
                cameraDriver.SetRootPosAndSize(new Vector3(center.x, AutoTestScreenshotProfiles.MeleeAction.CameraHeight, center.z), AutoTestScreenshotProfiles.MeleeAction.FixedRootSize ?? 11f);
            }
            catch (Exception ex)
            {
                Log.Warning("[LogSyncAutoTest] Melee action focus failed: " + ex.Message);
            }
        }

        private bool FocusPocketMapOverview(Map targetMap)
        {
            if (targetMap == null || !Find.Maps.Contains(targetMap)) return false;

            try
            {
                if (Current.Game != null) Current.Game.CurrentMap = targetMap;
                CameraDriver cameraDriver = Find.CameraDriver;
                if (cameraDriver == null) return false;

                float aspect = ResolveTurretScreenshotAspect(cameraDriver);
                float normalizedAspect = aspect > 0.01f ? aspect : DefaultTurretScreenshotAspect;
                float rootSize = Mathf.Max(
                    targetMap.Size.z * AutoTestScreenshotProfiles.PocketMapOverview.OverviewScale,
                    (targetMap.Size.x / normalizedAspect) * AutoTestScreenshotProfiles.PocketMapOverview.OverviewScale);
                rootSize = Mathf.Max(rootSize, AutoTestScreenshotProfiles.PocketMapOverview.MinRootSize);

                cameraDriver.SetRootPosAndSize(new Vector3(targetMap.Center.x, AutoTestScreenshotProfiles.PocketMapOverview.CameraHeight, targetMap.Center.z), rootSize);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning("[LogSyncAutoTest] Pocket map overview focus failed: " + ex.Message);
                return false;
            }
        }

        private bool TryResolveTurretScreenshotView(TurretTestSession session, float aspect, out Vector3 rootPos, out float rootSize)
        {
            if (TryGetStageScreenshotOverride(session, out rootPos, out rootSize))
            {
                return true;
            }

            rootPos = default;
            bool useCameraPlusZoom = IsCameraPlusActive();
            ResolveScreenshotBounds(AutoTestScreenshotProfiles.TurretPrimary, useCameraPlusZoom, out float minRootSize, out float maxRootSize, out float cellMargin);
            rootSize = minRootSize;

            if (session?.SpawnedTurret == null || session.SpawnedTurret.Destroyed)
            {
                return false;
            }

            Vector3 turretPos = ToMapWorldCenter(session.SpawnedTurret.Position);
            Pawn targetPawn = ResolveTurretScreenshotTarget(session);
            if (targetPawn == null)
            {
                rootPos = turretPos;
                rootSize = minRootSize;
                return true;
            }

            Vector3 targetPos = ToMapWorldCenter(targetPawn.Position);
            rootPos = (turretPos + targetPos) * 0.5f;

            float halfWidth = Mathf.Abs(turretPos.x - targetPos.x) * 0.5f + cellMargin;
            float halfHeight = Mathf.Abs(turretPos.z - targetPos.z) * 0.5f + cellMargin;

            float normalizedAspect = aspect > 0.01f ? aspect : DefaultTurretScreenshotAspect;
            float requiredRootSize = Mathf.Max(halfHeight, halfWidth / normalizedAspect) * AutoTestScreenshotProfiles.TurretPrimary.RangeScale;
            rootSize = Mathf.Clamp(requiredRootSize, minRootSize, maxRootSize);
            return true;
        }

        private bool TryResolveProjectileFocusView(TurretTestSession session, Thing projectileThing, float aspect, out Vector3 rootPos, out float rootSize)
        {
            rootPos = default;
            rootSize = AutoTestScreenshotProfiles.TurretPrimary.MinRootSize;
            if (session?.SpawnedTurret == null || projectileThing == null)
            {
                return false;
            }

            LocalTargetInfo target = ResolveProjectileTargetInfo(projectileThing as Projectile);
            if (!target.IsValid)
            {
                return false;
            }

            Vector3 turretPos = ToMapWorldCenter(session.SpawnedTurret.Position);
            Vector3 targetPos = target.Thing != null
                ? target.Thing.DrawPos
                : ToMapWorldCenter(target.Cell);

            bool useCameraPlusZoom = IsCameraPlusActive();
            ResolveScreenshotBounds(AutoTestScreenshotProfiles.TurretPrimary, useCameraPlusZoom, out float minRootSize, out float maxRootSize, out float cellMargin);

            rootPos = (turretPos + targetPos) * 0.5f;
            float normalizedAspect = aspect > 0.01f ? aspect : DefaultTurretScreenshotAspect;
            float halfWidth = Mathf.Abs(turretPos.x - targetPos.x) * 0.5f + cellMargin;
            float halfHeight = Mathf.Abs(turretPos.z - targetPos.z) * 0.5f + cellMargin;
            float requiredRootSize = Mathf.Max(halfHeight, halfWidth / normalizedAspect) * AutoTestScreenshotProfiles.TurretPrimary.RangeScale;
            rootSize = Mathf.Clamp(requiredRootSize, minRootSize, maxRootSize);
            return true;
        }

        private bool TryResolveMeleeWeaponScreenshotView(MeleeWeaponTestSession session, out Vector3 rootPos, out float rootSize)
        {
            rootPos = default;
            rootSize = AutoTestScreenshotProfiles.MeleePrimary.FixedRootSize ?? 11f;

            if (session?.ShooterPawn == null || session.ShooterPawn.Destroyed)
            {
                return false;
            }

            Vector3 shooterPos = session.ShooterPawn.DrawPos;
            Pawn targetPawn = session.EnemyPawns?.Where(p => p != null && !p.Destroyed && p.Spawned && p.Map == session.TestMap).OrderBy(p => p.Position.DistanceToSquared(session.ShooterPawn.Position)).FirstOrDefault();

            if (targetPawn == null || targetPawn.Destroyed)
            {
                rootPos = shooterPos;
                return true;
            }

            Vector3 targetPos = targetPawn.DrawPos;
            rootPos = (shooterPos + targetPos) * 0.5f;
            return true;
        }

        private static Pawn ResolveTurretScreenshotTarget(TurretTestSession session)
        {
            if (session?.SpawnedTurret == null || session.EnemyPawns == null)
            {
                return null;
            }

            IntVec3 turretCell = session.SpawnedTurret.Position;
            return session.EnemyPawns
                .Where(pawn => pawn != null && !pawn.Destroyed && pawn.Spawned && pawn.Map == session.TestMap)
                .OrderBy(pawn => pawn.Position.DistanceToSquared(turretCell))
                .ThenByDescending(pawn => pawn.Downed ? 0 : 1)
                .FirstOrDefault();
        }

        private static Vector3 ToMapWorldCenter(IntVec3 cell)
        {
            return cell.ToVector3Shifted();
        }

        private static void ConfigureTestPawnCombatStats(Pawn pawn)
        {
            if (pawn?.skills == null)
            {
                return;
            }

            SkillRecord shootingSkill = pawn.skills.GetSkill(SkillDefOf.Shooting);
            if (shootingSkill != null)
            {
                shootingSkill.Level = 20;
            }

            SkillRecord meleeSkill = pawn.skills.GetSkill(SkillDefOf.Melee);
            if (meleeSkill != null)
            {
                meleeSkill.Level = 20;
            }
        }

        private static bool TryEnableSearchAndDestroy(Pawn pawn, out string status)
        {
            status = "Unavailable";
            if (pawn == null)
            {
                status = "PawnMissing";
                return false;
            }

            try
            {
                Assembly searchAndDestroyAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, "SearchAndDestroy", StringComparison.OrdinalIgnoreCase));
                if (searchAndDestroyAssembly == null)
                {
                    status = "AssemblyMissing";
                    return false;
                }

                Type baseType = searchAndDestroyAssembly.GetType("SearchAndDestroy.Base");
                Type storageType = searchAndDestroyAssembly.GetType("SearchAndDestroy.Storage.ExtendedDataStorage");
                Type pawnDataType = searchAndDestroyAssembly.GetType("SearchAndDestroy.Storage.ExtendedPawnData");
                if (baseType == null || storageType == null || pawnDataType == null)
                {
                    status = "TypeMissing";
                    return false;
                }

                PropertyInfo instanceProperty = baseType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                PropertyInfo storageProperty = baseType.GetProperty("ExtendedDataStorage", BindingFlags.Public | BindingFlags.Instance);
                MethodInfo loadStorageMethod = baseType.GetMethod("LoadExtendedDataStorage", BindingFlags.Public | BindingFlags.Static);
                MethodInfo getExtendedDataMethod = storageType.GetMethod("GetExtendedDataFor", BindingFlags.Public | BindingFlags.Instance);
                FieldInfo enabledField = pawnDataType.GetField("SD_enabled", BindingFlags.Public | BindingFlags.Instance);
                if (instanceProperty == null || storageProperty == null || getExtendedDataMethod == null || enabledField == null)
                {
                    status = "MemberMissing";
                    return false;
                }

                object baseInstance = instanceProperty.GetValue(null, null);
                object storage = baseInstance == null ? null : storageProperty.GetValue(baseInstance, null);
                if (storage == null && loadStorageMethod != null)
                {
                    loadStorageMethod.Invoke(null, null);
                    baseInstance = instanceProperty.GetValue(null, null);
                    storage = baseInstance == null ? null : storageProperty.GetValue(baseInstance, null);
                }

                if (storage == null)
                {
                    status = "StorageMissing";
                    return false;
                }

                object pawnData = getExtendedDataMethod.Invoke(storage, new object[] { pawn });
                if (pawnData == null)
                {
                    status = "PawnDataMissing";
                    return false;
                }

                enabledField.SetValue(pawnData, true);
                status = "Enabled";
                return true;
            }
            catch (Exception ex)
            {
                status = "Error=" + TrimForNote(ex.Message);
                return false;
            }
        }

        private static LocalTargetInfo ResolveProjectileTargetInfo(Projectile projectile)
        {
            if (projectile == null)
            {
                return LocalTargetInfo.Invalid;
            }

            if (ProjectileIntendedTargetField?.GetValue(projectile) is LocalTargetInfo intendedTarget && intendedTarget.IsValid)
            {
                return intendedTarget;
            }

            if (ProjectileUsedTargetField?.GetValue(projectile) is LocalTargetInfo usedTarget && usedTarget.IsValid)
            {
                return usedTarget;
            }

            return LocalTargetInfo.Invalid;
        }

        private bool TryResolveRangedWeaponScreenshotView(RangedWeaponTestSession session, float aspect, out Vector3 rootPos, out float rootSize)
        {
            if (TryGetStageScreenshotOverride(session, out rootPos, out rootSize))
            {
                return true;
            }

            rootPos = default;
            bool useCameraPlusZoom = IsCameraPlusActive();
            ResolveScreenshotBounds(AutoTestScreenshotProfiles.RangedPrimary, useCameraPlusZoom, out float minRootSize, out float maxRootSize, out float cellMargin);
            if (session?.ShooterPawn == null || session.ShooterPawn.Destroyed)
            {
                rootSize = minRootSize;
                return false;
            }

            Vector3 shooterPos = ToMapWorldCenter(session.ShooterPawn.Position);
            Pawn targetPawn = session.ResolvePrimaryTarget();
            if (targetPawn == null)
            {
                rootPos = shooterPos;
                rootSize = minRootSize;
                return true;
            }

            Vector3 targetPos = ToMapWorldCenter(targetPawn.Position);
            rootPos = (shooterPos + targetPos) * 0.5f;

            float normalizedAspect = aspect > 0.01f ? aspect : DefaultTurretScreenshotAspect;
            float halfWidth = Mathf.Abs(shooterPos.x - targetPos.x) * 0.5f + cellMargin;
            float halfHeight = Mathf.Abs(shooterPos.z - targetPos.z) * 0.5f + cellMargin;
            rootSize = Mathf.Clamp(Mathf.Max(halfHeight, halfWidth / normalizedAspect) * AutoTestScreenshotProfiles.RangedPrimary.RangeScale, minRootSize, maxRootSize);
            return true;
        }

        private bool TryResolveRangedWeaponProjectileFocusView(RangedWeaponTestSession session, Thing projectileThing, float aspect, out Vector3 rootPos, out float rootSize)
        {
            rootPos = default;
            rootSize = AutoTestScreenshotProfiles.RangedPrimary.MinRootSize;
            if (session?.ShooterPawn == null || projectileThing == null)
            {
                return false;
            }

            LocalTargetInfo target = ResolveProjectileTargetInfo(projectileThing as Projectile);
            Vector3 shooterPos = ToMapWorldCenter(session.ShooterPawn.Position);
            Vector3 projectilePos = projectileThing.DrawPos;
            Pawn primaryTarget = session.ResolvePrimaryTarget();
            Vector3? primaryTargetPos = primaryTarget != null && !primaryTarget.Destroyed
                ? primaryTarget.DrawPos
                : (Vector3?)null;
            Vector3 targetPos;
            if (target.IsValid)
            {
                targetPos = target.Thing != null ? target.Thing.DrawPos : ToMapWorldCenter(target.Cell);
            }
            else if (primaryTargetPos.HasValue)
            {
                targetPos = primaryTargetPos.Value;
            }
            else
            {
                return false;
            }

            if (!TryResolveRangedWeaponScreenshotView(session, aspect, out Vector3 baseRootPos, out float baseRootSize))
            {
                return false;
            }

            bool useCameraPlusZoom = IsCameraPlusActive();
            ResolveScreenshotBounds(AutoTestScreenshotProfiles.RangedPrimary, useCameraPlusZoom, out float minRootSize, out float maxRootSize, out float cellMargin);

            float minX = Mathf.Min(shooterPos.x, projectilePos.x, targetPos.x);
            float maxX = Mathf.Max(shooterPos.x, projectilePos.x, targetPos.x);
            float minZ = Mathf.Min(shooterPos.z, projectilePos.z, targetPos.z);
            float maxZ = Mathf.Max(shooterPos.z, projectilePos.z, targetPos.z);
            if (primaryTargetPos.HasValue)
            {
                Vector3 primary = primaryTargetPos.Value;
                minX = Mathf.Min(minX, primary.x);
                maxX = Mathf.Max(maxX, primary.x);
                minZ = Mathf.Min(minZ, primary.z);
                maxZ = Mathf.Max(maxZ, primary.z);
            }

            rootPos = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);
            rootSize = Mathf.Clamp(Mathf.Max(baseRootSize, minRootSize + cellMargin * 0.5f), minRootSize, maxRootSize);

            float maxCenterShift = Mathf.Max(0.5f, cellMargin);
            rootPos.x = Mathf.Clamp(rootPos.x, baseRootPos.x - maxCenterShift, baseRootPos.x + maxCenterShift);
            rootPos.z = Mathf.Clamp(rootPos.z, baseRootPos.z - maxCenterShift, baseRootPos.z + maxCenterShift);
            return true;
        }

        private static bool IsProjectileStage(string stageSuffix)
        {
            return !string.IsNullOrWhiteSpace(stageSuffix) &&
                stageSuffix.StartsWith("projectile_center_", StringComparison.OrdinalIgnoreCase);
        }

        private AutoTestCaptureProbeResult ProbeTurretCapture(TurretTestSession session, string stageSuffix)
        {
            if (session?.SpawnedTurret == null || session.TestMap == null)
            {
                return new AutoTestCaptureProbeResult { Ready = false, Details = "TurretProbe:MissingSessionState" };
            }

            CameraDriver cameraDriver = Find.CameraDriver;
            Camera camera = cameraDriver?.GetComponent<Camera>();
            Pawn targetPawn = ResolveTurretScreenshotTarget(session);
            if (camera == null || targetPawn == null)
            {
                return new AutoTestCaptureProbeResult
                {
                    Ready = false,
                    Details = "TurretProbe:CameraOrTargetMissing camera=" + (camera != null) + " target=" + (targetPawn != null)
                };
            }

            const float margin = 0.06f;
            Vector3 turretViewport = camera.WorldToViewportPoint(session.SpawnedTurret.DrawPos);
            Vector3 targetViewport = camera.WorldToViewportPoint(targetPawn.DrawPos);
            bool turretVisible = IsViewportPointSafe(turretViewport, margin);
            bool targetVisible = IsViewportPointSafe(targetViewport, margin);

            StringBuilder builder = new StringBuilder();
            builder.Append(BuildCameraDebugInfo(cameraDriver, camera, stageSuffix));
            builder.Append(" turret=").Append(FormatViewportPoint(turretViewport));
            builder.Append(" target=").Append(FormatViewportPoint(targetViewport));
            if (session.ActiveStageScreenshot?.FocusRootPos != null)
            {
                builder.Append(" requestedRootPos=").Append(FormatVector3(session.ActiveStageScreenshot.FocusRootPos.Value));
            }
            if (session.ActiveStageScreenshot?.FocusRootSize != null)
            {
                builder.Append(" requestedRootSize=").Append(session.ActiveStageScreenshot.FocusRootSize.Value.ToString("0.00"));
            }

            return new AutoTestCaptureProbeResult
            {
                Ready = turretVisible && targetVisible,
                Details = builder.ToString()
            };
        }

        private AutoTestCaptureProbeResult ProbeRangedWeaponCapture(RangedWeaponTestSession session, string stageSuffix)
        {
            if (session?.ShooterPawn == null || session.TestMap == null)
            {
                return new AutoTestCaptureProbeResult { Ready = false, Details = "RangedProbe:MissingSessionState" };
            }

            CameraDriver cameraDriver = Find.CameraDriver;
            Camera camera = cameraDriver?.GetComponent<Camera>();
            Pawn targetPawn = session.ResolvePrimaryTarget();
            if (camera == null || targetPawn == null)
            {
                return new AutoTestCaptureProbeResult
                {
                    Ready = false,
                    Details = "RangedProbe:CameraOrTargetMissing camera=" + (camera != null) + " target=" + (targetPawn != null)
                };
            }

            const float margin = 0.06f;
            Vector3 shooterViewport = camera.WorldToViewportPoint(session.ShooterPawn.DrawPos);
            Vector3 targetViewport = camera.WorldToViewportPoint(targetPawn.DrawPos);
            bool shooterVisible = IsViewportPointSafe(shooterViewport, margin);
            bool targetVisible = IsViewportPointSafe(targetViewport, margin);

            Thing projectile = null;
            Vector3 projectileViewport = default;
            bool projectileVisible = true;
            if (IsProjectileStage(stageSuffix))
            {
                projectile = ResolveViewportClosestProjectile(session.TestMap, camera);
                if (projectile != null)
                {
                    projectileViewport = camera.WorldToViewportPoint(projectile.DrawPos);
                    projectileVisible = IsViewportPointSafe(projectileViewport, 0.02f);
                }
                else
                {
                    projectileVisible = false;
                }
            }

            StringBuilder builder = new StringBuilder();
            builder.Append(BuildCameraDebugInfo(cameraDriver, camera, stageSuffix));
            builder.Append(" shooter=").Append(FormatViewportPoint(shooterViewport));
            builder.Append(" target=").Append(FormatViewportPoint(targetViewport));
            builder.Append(" shooterLabel=").Append(session.ShooterPawn.LabelShortCap);
            builder.Append(" targetLabel=").Append(targetPawn.LabelShortCap);
            if (projectile != null)
            {
                builder.Append(" projectile=").Append(FormatViewportPoint(projectileViewport));
                builder.Append(" projectileDef=").Append(projectile.def?.defName ?? "null");
            }
            else if (IsProjectileStage(stageSuffix))
            {
                builder.Append(" projectile=missing");
            }
            if (session.ActiveStageScreenshot?.FocusRootPos != null)
            {
                builder.Append(" requestedRootPos=").Append(FormatVector3(session.ActiveStageScreenshot.FocusRootPos.Value));
            }
            if (session.ActiveStageScreenshot?.FocusRootSize != null)
            {
                builder.Append(" requestedRootSize=").Append(session.ActiveStageScreenshot.FocusRootSize.Value.ToString("0.00"));
            }

            return new AutoTestCaptureProbeResult
            {
                Ready = shooterVisible && targetVisible && projectileVisible,
                Details = builder.ToString()
            };
        }

        private static Thing ResolveViewportClosestProjectile(Map map, Camera camera)
        {
            if (map == null || camera == null)
            {
                return null;
            }

            IEnumerable<Thing> projectiles = map.listerThings?.ThingsInGroup(ThingRequestGroup.Projectile);
            if (projectiles == null)
            {
                return null;
            }

            Thing bestProjectile = null;
            float bestDistance = float.MaxValue;
            foreach (Thing projectile in projectiles)
            {
                if (projectile == null || projectile.Destroyed || !projectile.Spawned)
                {
                    continue;
                }

                Vector3 viewport = camera.WorldToViewportPoint(projectile.DrawPos);
                if (viewport.z <= 0f)
                {
                    continue;
                }

                float distance = Vector2.Distance(new Vector2(viewport.x, viewport.y), new Vector2(0.5f, 0.5f));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestProjectile = projectile;
                }
            }

            return bestProjectile;
        }

        private static bool IsViewportPointSafe(Vector3 viewportPoint, float margin)
        {
            return viewportPoint.z > 0f &&
                viewportPoint.x >= margin &&
                viewportPoint.x <= 1f - margin &&
                viewportPoint.y >= margin &&
                viewportPoint.y <= 1f - margin;
        }

        private static string BuildCameraDebugInfo(CameraDriver cameraDriver, Camera camera, string stageSuffix)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("stage=").Append(stageSuffix ?? "default");
            builder.Append(" map=").Append(Current.Game?.CurrentMap?.uniqueID ?? -1);
            if (cameraDriver != null)
            {
                builder.Append(" cameraDriverPos=").Append(FormatVector3(cameraDriver.transform.position));
            }
            if (camera != null)
            {
                builder.Append(" cameraPos=").Append(FormatVector3(camera.transform.position));
                builder.Append(" ortho=").Append(camera.orthographicSize.ToString("0.00"));
                builder.Append(" aspect=").Append(camera.aspect.ToString("0.000"));
                builder.Append(" pixel=").Append(camera.pixelWidth).Append("x").Append(camera.pixelHeight);
            }
            builder.Append(" screen=").Append(Screen.width).Append("x").Append(Screen.height);
            return builder.ToString();
        }

        private static string FormatViewportPoint(Vector3 point)
        {
            return string.Format("({0:0.000},{1:0.000},z={2:0.000})", point.x, point.y, point.z);
        }

        private static string FormatVector3(Vector3 point)
        {
            return string.Format("({0:0.00},{1:0.00},{2:0.00})", point.x, point.y, point.z);
        }

        private static bool TryGetStageScreenshotOverride(TurretTestSession session, out Vector3 rootPos, out float rootSize)
        {
            rootPos = default;
            rootSize = 0f;

            if (session?.ActiveStageScreenshot?.FocusRootPos == null || session.ActiveStageScreenshot.FocusRootSize == null)
            {
                return false;
            }

            rootPos = session.ActiveStageScreenshot.FocusRootPos.Value;
            rootSize = session.ActiveStageScreenshot.FocusRootSize.Value;
            return true;
        }

        private static bool TryGetStageScreenshotOverride(RangedWeaponTestSession session, out Vector3 rootPos, out float rootSize)
        {
            rootPos = default;
            rootSize = 0f;

            if (session?.ActiveStageScreenshot?.FocusRootPos == null || session.ActiveStageScreenshot.FocusRootSize == null)
            {
                return false;
            }

            rootPos = session.ActiveStageScreenshot.FocusRootPos.Value;
            rootSize = session.ActiveStageScreenshot.FocusRootSize.Value;
            return true;
        }

        private static void ResolveScreenshotBounds(AutoTestScreenshotProfile profile, bool useCameraPlusZoom, out float minRootSize, out float maxRootSize, out float cellMargin)
        {
            profile = profile ?? AutoTestScreenshotProfiles.TurretPrimary;
            minRootSize = useCameraPlusZoom && profile.CameraPlusMinRootSize > 0f
                ? profile.CameraPlusMinRootSize
                : profile.MinRootSize;
            maxRootSize = useCameraPlusZoom && profile.CameraPlusMaxRootSize > 0f
                ? profile.CameraPlusMaxRootSize
                : profile.MaxRootSize;
            cellMargin = useCameraPlusZoom && profile.CameraPlusCellMargin > 0f
                ? profile.CameraPlusCellMargin
                : profile.CellMargin;
        }

        private static float ResolveTurretScreenshotAspect(CameraDriver cameraDriver)
        {
            if (cameraDriver == null)
            {
                return DefaultTurretScreenshotAspect;
            }

            Camera camera = cameraDriver.GetComponent<Camera>();
            return camera != null && camera.aspect > 0.01f
                ? camera.aspect
                : DefaultTurretScreenshotAspect;
        }

        private bool IsCameraPlusActive()
        {
            if (cameraPlusActive.HasValue)
            {
                return cameraPlusActive.Value;
            }

            cameraPlusActive = LoadedModManager.RunningMods.Any(mod =>
                string.Equals(mod.PackageIdPlayerFacing, "brrainz.cameraplus", StringComparison.OrdinalIgnoreCase));
            return cameraPlusActive.Value;
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
